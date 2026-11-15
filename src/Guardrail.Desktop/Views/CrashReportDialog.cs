using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Guardrail.CrashAi;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>The Crash Review AI report for one video: verdict, fault split, key frames, motion timeline and findings.</summary>
public sealed class CrashReportDialog : DialogFrame
{
    private readonly VideoReview _review;
    private readonly CrashReport _report;

    public CrashReportDialog(VideoReview review)
        : base($"{review.Number} · Crash review", $"{review.VideoName} · {review.Source} · analyzed {review.DateText}", "", 1000)
    {
        _review = review;
        _report = review.Report ?? new CrashReport();

        // ----- Footer -----
        var delete = MakeButton("Delete", "GhostButton", "");
        delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
        delete.Click += (_, _) =>
        {
            var label = ((StackPanel)delete.Content).Children.OfType<TextBlock>().Last();
            if (label.Text == "Delete") { label.Text = "Click again to delete"; return; }
            CrashReviewService.Delete(_review);
            Close();
        };
        DockPanel.SetDock(delete, Dock.Left);
        Footer.Children.Add(delete);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var open = MakeButton("Play video", "GhostButton", "");
        open.Margin = new Thickness(0, 0, 10, 0);
        open.IsEnabled = review.VideoExists;
        open.Click += (_, _) => OpenVideo();
        var again = MakeButton("Analyze again", "GhostButton", "");
        again.Margin = new Thickness(0, 0, 10, 0);
        again.Click += (_, _) => { CrashReviewService.Retry(_review); Close(); };
        var close = MakeButton("Close", "PrimaryButton");
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        right.Children.Add(open);
        if (review.IsWeb)
        {
            var web = MakeButton("Open link", "GhostButton", "\uE774");
            web.Margin = new Thickness(0, 0, 10, 0);
            web.ToolTip = review.SourceUrl;
            web.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(review.SourceUrl) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Toaster.Show($"Couldn't open the link: {ex.Message}"); }
            };
            right.Children.Add(web);
        }
        right.Children.Add(again);
        right.Children.Add(close);
        Footer.Children.Add(right);

        // ----- Body -----
        var body = new StackPanel { Margin = new Thickness(28, 0, 28, 18) };
        body.Children.Add(VerdictBanner());
        body.Children.Add(Facts());
        if (_report.KeyFrames.Count > 0)
        {
            body.Children.Add(SectionTitle("KEY MOMENTS"));
            body.Children.Add(KeyFrames());
        }
        if (_report.SpeedTrace.Count > 4 && _report.CameraKind.StartsWith("Dashcam"))
        {
            body.Children.Add(SectionTitle("YOUR VEHICLE'S MOVEMENT"));
            body.Children.Add(Timeline());
        }
        if (_report.Findings.Count > 0)
        {
            body.Children.Add(SectionTitle("WHAT THE AI SAW"));
            body.Children.Add(Findings());
        }
        var note = new TextBlock
        {
            Text = "An automated estimate from the video alone (no audio, GPS, statements or police report). " +
                   $"{_report.Engine}. Analyzed in {_report.AnalysisSeconds:0} s on this PC.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 14, 0, 0),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        body.Children.Add(note);
        SetBody(body);
    }

    private UIElement VerdictBanner()
    {
        var banner = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(0, 0, 0, 14) };
        banner.SetResourceReference(Border.BackgroundProperty, "Brush.Hero");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Hero(new TextBlock { Text = "VERDICT", FontSize = 11, FontWeight = FontWeights.Bold }, "Brush.Highlight"));
        left.Children.Add(Hero(new TextBlock
        {
            Text = _report.Headline.Length > 0 ? _report.Headline : "No collision found",
            FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 25, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        }, "Brush.HeroText"));
        left.Children.Add(Hero(new TextBlock { Text = _report.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19 }, "Brush.HeroMuted"));
        grid.Children.Add(left);

        if (_report.Verdict != Verdicts.NoCollision)
        {
            var conf = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
            conf.Children.Add(Hero(new TextBlock
            {
                Text = $"{_report.Confidence}%", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 40,
                FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right,
            }, "Brush.HeroText"));
            conf.Children.Add(Hero(new TextBlock { Text = "CONFIDENCE", FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right }, "Brush.HeroMuted"));
            Grid.SetColumn(conf, 1);
            grid.Children.Add(conf);
        }

        if (_report.Parties.Count == 2 && _report.FaultShares.Count == 2)
        {
            var split = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            Grid.SetRow(split, 1);
            Grid.SetColumnSpan(split, 2);
            int a = Math.Clamp(_report.FaultShares[0], 0, 100), b = 100 - a;
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(a, 12), GridUnitType.Star) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(b, 12), GridUnitType.Star) });
            split.Children.Add(Share($"{Capital(_report.Parties[0])} · {a}%", a >= b, new CornerRadius(8, 0, 0, 8), 0));
            split.Children.Add(Share($"{Capital(_report.Parties[1])} · {b}%", b > a, new CornerRadius(0, 8, 8, 0), 1));
            grid.Children.Add(split);
        }
        banner.Child = grid;
        return banner;
    }

    private Border Share(string text, bool major, CornerRadius radius, int column)
    {
        var border = new Border { CornerRadius = radius, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(column == 0 ? 0 : 2, 0, 0, 0) };
        border.SetResourceReference(Border.BackgroundProperty, major ? "Brush.Highlight" : "Brush.HeroTrack");
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, major ? "Brush.Sidebar" : "Brush.HeroText");
        border.Child = t;
        Grid.SetColumn(border, column);
        return border;
    }

    private UIElement Facts()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Add(string glyph, string text)
        {
            var pill = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
            pill.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 7, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, FontSize = 12.5 });
            pill.Child = row;
            wrap.Children.Add(pill);
        }
        if (_report.CollisionType.Length > 0) Add("", _report.CollisionType);
        if (_report.CameraKind.Length > 0) Add("", _report.CameraKind + (_report.SplitScreen ? " · road view of a split-screen video" : ""));
        if (_report.ImpactSeconds is { } t) Add("", $"Impact at {Clock(t)}");
        Add("", $"Video {Clock(_report.DurationSeconds)}");
        if (_report.VehiclesTracked > 0) Add("", $"{_report.VehiclesTracked} road user{(_report.VehiclesTracked == 1 ? "" : "s")} tracked");
        return wrap;
    }

    private UIElement KeyFrames()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 0, -10, 6) };
        foreach (var k in _report.KeyFrames)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var image = new Border { CornerRadius = new CornerRadius(10), Height = 128, BorderThickness = new Thickness(1) };
            image.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            image.SetResourceReference(Border.BackgroundProperty, "Brush.Skeleton");
            string path = System.IO.Path.Combine(_review.Folder, k.FileName);
            if (File.Exists(path))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
                    bmp.StreamSource = new MemoryStream(DataLock.ReadBytes(path)); // decrypted in memory when the lock is on
                    bmp.DecodePixelWidth = 480;
                    bmp.EndInit();
                    image.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                    image.Cursor = System.Windows.Input.Cursors.Hand;
                    image.ToolTip = "Open full size";
                    image.MouseLeftButtonUp += (_, _) => Shell(path);
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or LockedException or System.Security.Cryptography.CryptographicException) { }
            }
            cell.Children.Add(image);
            var cap = new TextBlock { Text = k.Caption, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 6, 0, 0) };
            cell.Children.Add(cap);
            var time = new TextBlock { Text = Clock(k.Seconds), FontSize = 11, Margin = new Thickness(2, 1, 0, 0) };
            time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            cell.Children.Add(time);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private UIElement Timeline()
    {
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 8), Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        var stack = new StackPanel();
        var canvas = new Canvas { Height = 84, ClipToBounds = true };
        stack.Children.Add(canvas);
        var legend = new TextBlock { FontSize = 11, Margin = new Thickness(0, 6, 0, 0), Text = "Forward motion of the camera vehicle (relative — not miles per hour). Red line: impact." };
        legend.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(legend);
        card.Child = stack;
        canvas.SizeChanged += (_, _) => DrawTimeline(canvas);
        return card;
    }

    private void DrawTimeline(Canvas canvas)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        var trace = _report.SpeedTrace;
        if (w <= 0 || trace.Count < 2) return;
        double max = Math.Max(1e-6, trace.Max()) * 1.1, duration = Math.Max(_report.DurationSeconds, trace.Count * _report.TraceStepSeconds);
        double X(double s) => s / Math.Max(1e-6, duration) * w;

        var fill = new Polygon { Opacity = 0.18 };
        fill.SetResourceReference(Shape.FillProperty, "Brush.Accent");
        var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        line.SetResourceReference(Shape.StrokeProperty, "Brush.Accent");
        fill.Points.Add(new Point(0, h));
        for (int i = 0; i < trace.Count; i++)
        {
            var p = new Point(X(i * _report.TraceStepSeconds), h - 4 - trace[i] / max * (h - 8));
            line.Points.Add(p);
            fill.Points.Add(p);
        }
        fill.Points.Add(new Point(X((trace.Count - 1) * _report.TraceStepSeconds), h));
        canvas.Children.Add(fill);
        canvas.Children.Add(line);

        foreach (var f in _report.Findings)
        {
            var dot = new Ellipse { Width = 7, Height = 7, ToolTip = $"{Clock(f.Seconds)} — {f.Text}" };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Warm");
            Canvas.SetLeft(dot, X(f.Seconds) - 3.5);
            Canvas.SetTop(dot, h - 9);
            canvas.Children.Add(dot);
        }
        if (_report.ImpactSeconds is { } t)
        {
            var mark = new Rectangle { Width = 2, Height = h };
            mark.SetResourceReference(Shape.FillProperty, "Brush.Danger");
            Canvas.SetLeft(mark, X(t) - 1);
            canvas.Children.Add(mark);
        }
    }

    private UIElement Findings()
    {
        var list = new StackPanel();
        foreach (var f in _report.Findings)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            var time = new TextBlock { Text = Clock(f.Seconds), FontSize = 12, FontWeight = FontWeights.SemiBold };
            time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            chip.Child = time;
            row.Children.Add(chip);
            var text = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return list;
    }

    private TextBlock SectionTitle(string text)
    {
        var t = new TextBlock { Text = text, Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 12, 0, 8) };
        return t;
    }

    private static TextBlock Hero(TextBlock t, string brush)
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Clock(double seconds)
    {
        if (seconds < 0) seconds = 0;
        int m = (int)(seconds / 60);
        return $"{m}:{seconds - m * 60:00.0}";
    }

    private void OpenVideo()
    {
        if (!_review.VideoExists) { Toaster.Show("The video file has been moved or deleted."); return; }
        Shell(_review.VideoPath);
    }

    private static void Shell(string path)
    {
        try { FileVault.Open(path); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { Toaster.Show($"Couldn't open it: {ex.Message}"); }
    }
}
