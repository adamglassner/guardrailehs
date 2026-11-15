using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guardrail.CrashAi;
using Guardrail.Data;
using Guardrail.Services;
using Microsoft.Win32;

namespace Guardrail.Views;

/// <summary>Crash Review AI: add videos, set up automatic mode, see every verdict.</summary>
public partial class AiReviewView : UserControl
{
    private string _rowsSignature = "";

    public AiReviewView()
    {
        InitializeComponent();
        AccidentsCheck.IsChecked = App.Settings.CrashAnalyzeAccidents;
        PreventCheck.IsChecked = App.Settings.CrashSetPreventability;
        PreventCheck.IsEnabled = App.Settings.CrashAnalyzeAccidents;
        if (!CrashAnalyzer.ModelAvailable)
            PrivacyText.Text = "The AI model isn't included in this build, so videos can't be analyzed. Install Guardrail from the official release.";

        Loaded += (_, _) =>
        {
            CrashReviewService.Changed += Refresh;
            DataStore.CrashReviews.Changed += OnStoreChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            CrashReviewService.Changed -= Refresh;
            DataStore.CrashReviews.Changed -= OnStoreChanged;
        };
        NowTrack.SizeChanged += (_, _) => UpdateNow();
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.CrashReviews.Items.OrderByDescending(r => r.Seq).ToList();
        string sig = string.Join("|", all.Select(r => $"{r.Seq}:{r.State}:{r.Stage.Length > 0}:{r.Report?.Verdict}"));
        if (sig != _rowsSignature || Rows.ItemsSource is null)
        {
            _rowsSignature = sig;
            Rows.ItemsSource = all;
        }
        EmptyState.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var done = all.Where(r => r.IsDone).ToList();
        KpiTotal.Text = done.Count.ToString();
        KpiCamera.Text = done.Count(r => r.Report!.Verdict == Verdicts.Camera).ToString();
        KpiOther.Text = done.Count(r => r.Report!.Verdict == Verdicts.Other).ToString();
        KpiUnclear.Text = done.Count(r => r.Report!.Verdict is Verdicts.Unclear or Verdicts.NoCollision or Verdicts.A or Verdicts.B).ToString();

        string folder = App.Settings.CrashWatchFolder;
        bool watching = folder.Length > 0 && Directory.Exists(folder);
        WatchText.Text = watching
            ? $"Watching {folder} — new videos there are analyzed as soon as they finish copying."
            : folder.Length > 0 ? $"{folder} can't be found. Choose the folder again." : "Off. Point it at the folder your dashcam uploads or exports go to.";
        WatchButtonText.Text = watching ? "Change folder…" : "Choose folder…";
        StopWatchButton.Visibility = folder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateNow();
    }

    private void UpdateNow()
    {
        var current = CrashReviewService.Current;
        int waiting = CrashReviewService.WaitingCount;
        if (current is null && waiting == 0) { NowPanel.Visibility = Visibility.Collapsed; return; }
        NowPanel.Visibility = Visibility.Visible;
        if (current is null)
        {
            NowTitle.Text = $"{waiting} video{(waiting == 1 ? "" : "s")} waiting";
            NowStage.Text = "Starting…";
            NowPercent.Text = "";
            NowFill.Width = 0;
            return;
        }
        NowTitle.Text = $"{current.Number} · {current.VideoName}";
        NowPercent.Text = $"{current.Progress * 100:0}%";
        NowStage.Text = current.Stage + (waiting > 0 ? $" · {waiting} more waiting" : "");
        NowFill.Width = Math.Max(0, NowTrack.ActualWidth * Math.Clamp(current.Progress, 0, 1));
    }

    // ----- Adding videos -----

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose accident videos",
            Multiselect = true,
            Filter = "Videos|" + string.Join(";", CrashAnalyzer.VideoExtensions.Select(x => "*" + x)) + "|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddVideos(dialog.FileNames);
    }

    private void AddVideos(IEnumerable<string> paths)
    {
        int added = 0, skipped = 0;
        foreach (var p in paths)
        {
            if (CrashReviewService.Add(p) is not null) added++; else skipped++;
        }
        if (added > 0)
            Toaster.Show($"{added} video{(added == 1 ? "" : "s")} added. The AI is on it — you can keep working.");
        if (skipped > 0)
            Toaster.Show($"{skipped} file{(skipped == 1 ? " isn't a video" : "s aren't videos")} Guardrail can read.");
    }

    // ----- Web links -----

    private void AnalyzeLink_Click(object sender, RoutedEventArgs e) => AddLinks(LinkBox.Text);

    private void LinkBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; AddLinks(LinkBox.Text); }
    }

    private void LinkBox_TextChanged(object sender, TextChangedEventArgs e) =>
        LinkHint.Visibility = LinkBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Queues every link in the text (one per line or space-separated).</summary>
    private void AddLinks(string text)
    {
        var links = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Where(WebVideo.LooksLikeUrl).ToList();
        if (links.Count == 0)
        {
            Toaster.Show("Paste a link that starts with http:// or https://.");
            return;
        }
        foreach (var l in links) CrashReviewService.AddUrl(l);
        LinkBox.Clear();
        Toaster.Show(links.Count == 1 ? "Link added. Guardrail downloads the video, then analyzes it." : $"{links.Count} links added.");
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        DropOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Accent");
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) =>
        DropOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Border");

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone_DragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] && e.Data.GetData(DataFormats.UnicodeText) is string dropped)
        {
            AddLinks(dropped); // a link dragged from a browser
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var all = files.SelectMany(f => Directory.Exists(f) ? Directory.EnumerateFiles(f) : new[] { f }).ToList();
            AddVideos(all);
        }
    }

    // ----- Automatic mode -----

    private void WatchFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder to watch for accident videos" };
        if (App.Settings.CrashWatchFolder.Length > 0 && Directory.Exists(App.Settings.CrashWatchFolder))
            dialog.InitialDirectory = App.Settings.CrashWatchFolder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        App.Settings.CrashWatchFolder = dialog.FolderName;
        App.Settings.Save();
        CrashReviewService.ApplyWatchFolder();
        Toaster.Show($"Watching {dialog.FolderName}. Videos already there that haven't been reviewed are queued too.");
        Refresh();
    }

    private void StopWatch_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.CrashWatchFolder = "";
        App.Settings.Save();
        CrashReviewService.ApplyWatchFolder();
        Refresh();
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.CrashAnalyzeAccidents = AccidentsCheck.IsChecked == true;
        App.Settings.CrashSetPreventability = PreventCheck.IsChecked == true;
        PreventCheck.IsEnabled = App.Settings.CrashAnalyzeAccidents;
        App.Settings.Save();
    }

    // ----- Rows -----

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VideoReview review }) return;
        switch (review.State)
        {
            case ReviewState.Done when review.Report is not null:
                new CrashReportDialog(review).ShowDialog();
                break;
            case ReviewState.Failed:
                var pick = MessageDialog.Choose($"{review.Number} couldn't be analyzed", review.Error + "\n\nTry this video again, or remove it from the list?",
                    "Try again", "Close", "Remove from list", danger: true);
                if (pick == "Try again") CrashReviewService.Retry(review);
                else if (pick == "Remove from list") { CrashReviewService.Delete(review); Toaster.Show($"{review.Number} removed."); }
                break;
            default:
                if (MessageDialog.Confirm($"{review.Number} is {(review.State == ReviewState.Analyzing ? "being analyzed" : "waiting its turn")}",
                        "The report opens here as soon as the AI is done. Remove this video from the queue?", "Remove", "Keep it"))
                    CrashReviewService.Delete(review);
                break;
        }
        Refresh();
    }

    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the report
        if (sender is FrameworkElement { DataContext: VideoReview review } element) ShowMenu(element, review);
    }

    private void Row_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: VideoReview review } element) ShowMenu(element, review);
    }

    /// <summary>Everything you can do with one video: open, retry, find the file, open the link, remove.</summary>
    private void ShowMenu(FrameworkElement target, VideoReview review)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string glyph, string text, Action action, bool enabled = true, bool danger = false)
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled, Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 13 } };
            if (danger) item.SetResourceReference(ForegroundProperty, "Brush.Danger");
            item.Click += (_, _) => { action(); Refresh(); };
            menu.Items.Add(item);
        }

        if (review.IsDone) Item("\uE8A5", "Open report", () => new CrashReportDialog(review).ShowDialog());
        if (review.State == ReviewState.Failed || review.IsDone) Item("\uE72C", review.IsDone ? "Analyze again" : "Try again", () => CrashReviewService.Retry(review));
        Item("\uE838", "Show video in folder", () => CrashReviewService.ShowInFolder(review), review.VideoExists || review.VideoPath.Length > 0);
        if (review.IsWeb)
        {
            Item("\uE774", "Open link", () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(review.SourceUrl) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Toaster.Show($"Couldn't open the link: {ex.Message}"); }
            });
            Item("\uE8C8", "Copy link", () => { try { Clipboard.SetText(review.SourceUrl); Toaster.Show("Link copied."); } catch { /* clipboard busy */ } });
        }
        menu.Items.Add(new Separator());
        Item("\uE74D", "Remove from list", () => CrashReviewService.ConfirmAndDelete(review), danger: true);
        menu.IsOpen = true;
    }
}
