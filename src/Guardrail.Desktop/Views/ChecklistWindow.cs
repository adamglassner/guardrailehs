using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>
/// Fills in an inspection: every checklist item gets Pass / Fail / N/A and an optional note.
/// Changes are only written back to the inspection when you save or complete.
/// </summary>
public sealed class ChecklistWindow : Window
{
    private readonly Inspection _inspection;
    private readonly List<(ChecklistItem Item, Func<string> Result, Action<string> SetResult, TextBox Note)> _rows = [];
    private readonly TextBlock _progress = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

    public bool DeleteRequested { get; private set; }

    public ChecklistWindow(Inspection inspection)
    {
        _inspection = inspection;

        Title = $"{inspection.Number} · {inspection.TemplateName}";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/guardrail.ico"));
        Width = 820;
        Height = 760;
        MinWidth = 640;
        MinHeight = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Brush.Bg");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);

        var root = new DockPanel();

        // Header
        var header = new StackPanel { Margin = new Thickness(24, 18, 24, 10) };
        header.Children.Add(new TextBlock { Text = inspection.TemplateName, FontSize = 19, FontWeight = FontWeights.SemiBold });
        var sub = new TextBlock
        {
            Text = $"{inspection.Number} · {inspection.DateText} · {inspection.Location}" + (inspection.Inspector.Length > 0 ? $" · {inspection.Inspector}" : ""),
            Margin = new Thickness(0, 3, 0, 8),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        header.Children.Add(sub);
        header.Children.Add(_progress);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Footer
        var footer = new Border { Padding = new Thickness(24, 12, 24, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var footerDock = new DockPanel();

        var delete = new Button { Content = "Delete", Style = (Style)FindResource("GhostButton") };
        delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
        delete.Click += (_, _) =>
        {
            if (delete.Content as string == "Delete") { delete.Content = "Click again to delete"; return; }
            DeleteRequested = true;
            DialogResult = true;
        };
        DockPanel.SetDock(delete, Dock.Left);
        footerDock.Children.Add(delete);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var allPass = new Button { Content = "Mark the rest Pass", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0),
                                   ToolTip = "Sets every unanswered item to Pass" };
        allPass.Click += (_, _) =>
        {
            foreach (var row in _rows.Where(r => r.Result().Length == 0)) row.SetResult(ChecklistItem.Pass);
            UpdateProgress();
        };
        var later = new Button { Content = "Save for later", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        later.Click += (_, _) => Save(complete: false);
        var complete = new Button { Content = "Complete inspection", Style = (Style)FindResource("PrimaryButton") };
        complete.Click += (_, _) => Save(complete: true);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        buttons.Children.Add(_error);
        buttons.Children.Add(allPass);
        buttons.Children.Add(later);
        buttons.Children.Add(complete);
        footerDock.Children.Add(buttons);
        footer.Child = footerDock;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // Checklist
        var list = new StackPanel { Margin = new Thickness(24, 4, 24, 16) };
        int n = 0;
        foreach (var item in inspection.Items)
        {
            n++;
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var num = new TextBlock { Text = $"{n}.", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            num.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            grid.Children.Add(num);

            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            string group = "chk" + Guid.NewGuid().ToString("N");
            var radios = new List<RadioButton>();
            foreach (var option in ChecklistItem.Results)
            {
                var rb = new RadioButton
                {
                    Content = option,
                    GroupName = group,
                    IsChecked = item.Result == option,
                    Style = (Style)FindResource("ChipRadio"),
                    Margin = new Thickness(6, 0, 0, 0),
                };
                rb.Checked += (_, _) => UpdateProgress();
                radios.Add(rb);
                chips.Children.Add(rb);
            }
            Grid.SetColumn(chips, 2);
            grid.Children.Add(chips);

            var note = new TextBox { Text = item.Note, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Note (what's wrong, where, photo reference…)" };
            Grid.SetRow(note, 1);
            Grid.SetColumn(note, 1);
            Grid.SetColumnSpan(note, 2);
            grid.Children.Add(note);

            card.Child = grid;
            list.Children.Add(card);
            _rows.Add((item,
                () => radios.FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "",
                value => { foreach (var r in radios) r.IsChecked = (string)r.Content == value; },
                note));
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Content = root;
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        int answered = _rows.Count(r => r.Result().Length > 0);
        int pass = _rows.Count(r => r.Result() == ChecklistItem.Pass);
        int fail = _rows.Count(r => r.Result() == ChecklistItem.Fail);
        string score = pass + fail == 0 ? "—" : $"{100.0 * pass / (pass + fail):0}%";
        _progress.Text = $"{answered} of {_rows.Count} answered · {pass} pass · {fail} fail · score {score}";
        _error.Visibility = Visibility.Collapsed;
    }

    private void Save(bool complete)
    {
        int left = _rows.Count(r => r.Result().Length == 0);
        if (complete && left > 0)
        {
            _error.Text = $"{left} item{(left == 1 ? "" : "s")} not answered yet.";
            _error.Visibility = Visibility.Visible;
            return;
        }
        foreach (var row in _rows)
        {
            row.Item.Result = row.Result();
            row.Item.Note = row.Note.Text.Trim();
        }
        _inspection.IsComplete = complete;
        DialogResult = true;
    }
}
