using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Guardrail.CrashAi;
using Guardrail.CrashAi.Documents;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>The Crash Review AI report for one video: verdict, fault split, key frames, motion timeline and findings.</summary>
public sealed class CrashReportDialog : DialogFrame
{
    private readonly VideoReview _review;
    private readonly CrashReport _report;
    private CaseResult? _case;

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
        if (_report.CameraKind.StartsWith("Dashcam"))
        {
            var rename = MakeButton(review.CameraName.Length > 0 ? "Rename vehicle…" : "Name vehicle…", "GhostButton", "\uE8AC");
            rename.Margin = new Thickness(0, 0, 10, 0);
            rename.ToolTip = "Say which vehicle the camera was in, e.g. Unit 104";
            rename.Click += (_, _) => NameVehicle();
            right.Children.Insert(0, rename);
        }
        right.Children.Add(open);
        var folder = MakeButton("Show in folder", "GhostButton", "\uE838");
        folder.Margin = new Thickness(0, 0, 10, 0);
        folder.ToolTip = review.VideoPath;
        folder.IsEnabled = review.VideoExists;
        folder.Click += (_, _) => CrashReviewService.ShowInFolder(_review);
        right.Children.Add(folder);
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
        var camera = MakeButton("Camera type…", "GhostButton", "\uE714");
        camera.Margin = new Thickness(0, 0, 10, 0);
        camera.ToolTip = "Tell the AI what filmed this video (a dashcam, a fixed camera or a phone) and analyze it again";
        camera.Click += (_, _) => ChooseCamera();
        right.Children.Add(camera);
        right.Children.Add(again);
        right.Children.Add(close);
        Footer.Children.Add(right);

        Build();
    }

    /// <summary>(Re)builds the report body — after documents are attached the combined call updates in place.</summary>
    private void Build()
    {
        _case = _review.Case;
        var body = new StackPanel { Margin = new Thickness(28, 0, 28, 18) };
        body.Children.Add(VerdictBanner());
        body.Children.Add(Facts());
        if (_report.Narrative.Length > 0)
        {
            body.Children.Add(SectionTitle("WHAT HAPPENED"));
            body.Children.Add(Paragraph(_review.Describe(_report.Narrative)));
        }
        if (Shares().Count > 0)
        {
            body.Children.Add(SectionTitle("WHO WAS AT FAULT, AND WHY"));
            body.Children.Add(FaultCards());
        }
        if (WhereRows().Count > 0)
        {
            body.Children.Add(SectionTitle("WHERE AND WHEN"));
            body.Children.Add(Rows(WhereRows()));
        }
        if (_report.KeyFrames.Count > 0)
        {
            body.Children.Add(SectionTitle("KEY MOMENTS"));
            body.Children.Add(KeyFrames());
        }
        if (_report.DriverNotes.Count > 0 || _report.CabinNotes.Count > 0)
        {
            body.Children.Add(SectionTitle(_review.CameraName.Length > 0 ? $"{_review.CameraName.ToUpperInvariant()}'S DRIVING" : "THE CAMERA VEHICLE'S DRIVING"));
            body.Children.Add(DriverSection());
        }
        if (_report.RoadUsers.Count > 0)
        {
            body.Children.Add(SectionTitle($"EVERY ROAD USER IN THE VIDEO ({_report.RoadUsers.Count})"));
            body.Children.Add(RoadUsers());
        }
        body.Children.Add(SectionTitle(_review.Documents.Count > 0 ? $"DOCUMENTS ({_review.Documents.Count})" : "DOCUMENTS"));
        body.Children.Add(DocumentsSection());
        if (_report.SpeedTrace.Count > 4 && _report.CameraKind.StartsWith("Dashcam"))
        {
            body.Children.Add(SectionTitle(_review.CameraName.Length > 0 ? $"{_review.CameraName.ToUpperInvariant()}'S MOVEMENT" : "CAMERA VEHICLE'S MOVEMENT"));
            body.Children.Add(Timeline());
        }
        if (_report.Findings.Count > 0)
        {
            body.Children.Add(SectionTitle("WHAT THE AI SAW"));
            body.Children.Add(Findings());
        }
        int docs = _review.Documents.Count;
        var note = new TextBlock
        {
            Text = (docs > 0 ? $"An automated estimate from the video and {docs} attached document{(docs == 1 ? "" : "s")}. " : "An automated estimate from the video alone — attach the police report or a news story to sharpen it. ") +
                   $"Not a legal finding of fault. {_report.Engine}. Analyzed in {_report.AnalysisSeconds:0} s on this PC; nothing was uploaded.",
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
            Text = _review.Describe(_case is { UsedDocuments: true } c ? c.Headline : _report.Headline.Length > 0 ? _report.Headline : "No collision found"),
            FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 25, FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        }, "Brush.HeroText"));
        left.Children.Add(Hero(new TextBlock { Text = _review.Describe(_report.Summary), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19 }, "Brush.HeroMuted"));
        grid.Children.Add(left);

        if (_case is { UsedDocuments: true } && _case.Notes.Count > 0)
            left.Children.Add(Hero(new TextBlock { Text = _review.Describe(_case.Notes[0]), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontStyle = FontStyles.Italic }, "Brush.HeroMuted"));

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

        var shares = Shares().Where(x => x.Percent > 0).ToList();
        if (shares.Count >= 1)
        {
            var split = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            Grid.SetRow(split, 1);
            Grid.SetColumnSpan(split, 2);
            for (int i = 0; i < shares.Count; i++)
            {
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(shares[i].Percent, 12), GridUnitType.Star) });
                var radius = new CornerRadius(i == 0 ? 8 : 0, i == shares.Count - 1 ? 8 : 0, i == shares.Count - 1 ? 8 : 0, i == 0 ? 8 : 0);
                var bar = Share($"{Capital(_review.Describe(shares[i].Party))} · {shares[i].Percent}%", i == 0, radius, i);
                bar.ToolTip = $"{_review.Describe(shares[i].Party)}: {shares[i].Percent}% of the fault";
                split.Children.Add(bar);
            }
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
            var dot = new Ellipse { Width = 7, Height = 7, ToolTip = $"{Clock(f.Seconds)} — {_review.Describe(f.Text)}" };
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

    private UIElement Findings() => TimedList(_report.Findings);

    /// <summary>A party's share as shown: combined with the documents when they say something, otherwise the video's.</summary>
    private sealed record UiShare(string Party, int Percent, bool IsCamera, int? Video, int? Docs, List<string> Reasons);

    private List<UiShare> Shares()
    {
        if (_case is { UsedDocuments: true })
            return _case.Shares.Select(x => new UiShare(x.Party, x.Percent, x.IsCamera, x.VideoPercent, x.DocumentPercent, x.Reasons)).ToList();
        if (_report.Verdict == Verdicts.NoCollision) return [];
        if (_report.Shares.Count > 0)
            return _report.Shares.OrderByDescending(x => x.Percent).Select(x => new UiShare(x.Party, x.Percent, x.IsCamera, null, null, x.Reasons)).ToList();
        // Reports from Crash Review 1.x: two parties.
        return _report.Parties.Zip(_report.FaultShares, (p, f) => new UiShare(p, f, p.Contains("camera"), null, null, [])).OrderByDescending(x => x.Percent).ToList();
    }

    private UIElement FaultCards()
    {
        var list = new StackPanel();
        foreach (var sh in Shares())
        {
            var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) };
            card.SetResourceReference(Border.BorderBrushProperty, sh.Percent >= 50 ? "Brush.Accent" : "Brush.Border");
            card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var pct = new TextBlock { Text = $"{sh.Percent}%", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 26, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Top };
            pct.SetResourceReference(TextBlock.ForegroundProperty, sh.Percent >= 50 ? "Brush.Accent" : "Brush.TextMuted");
            g.Children.Add(pct);
            var right = new StackPanel();
            var head = new WrapPanel();
            head.Children.Add(new TextBlock { Text = Capital(_review.Describe(sh.Party)), FontWeight = FontWeights.SemiBold, FontSize = 14.5, Margin = new Thickness(0, 2, 10, 4) });
            if (sh.Video is int v) head.Children.Add(Chip($"Video {v}%"));
            if (sh.Docs is int d) head.Children.Add(Chip($"Documents {d}%"));
            right.Children.Add(head);
            var reasons = sh.Reasons.Count > 0 ? sh.Reasons : [sh.Percent == 0 ? "Nothing points to this party causing the collision." : "No specific behaviour was singled out."];
            foreach (var r in reasons.Take(8))
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new TextBlock { Text = "•" };
                dot.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                row.Children.Add(dot);
                var t = new TextBlock { Text = Capital(_review.Describe(r)), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                right.Children.Add(row);
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            card.Child = g;
            list.Children.Add(card);
        }
        return list;
    }

    private Border Chip(string text)
    {
        var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 6, 4), VerticalAlignment = VerticalAlignment.Top };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var t = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        chip.Child = t;
        return chip;
    }

    private List<(string Label, string Text)> WhereRows()
    {
        var rows = new List<(string, string)>();
        if (_report.Location.Length > 0) rows.Add(("In the video", Capital(_review.Describe(_report.Location))));
        if (_case is { } c)
        {
            var docLocs = c.Where.Where(w => !w.StartsWith("Video: ")).ToList();
            if (docLocs.Count > 0) rows.Add(("Location", string.Join(" · ", docLocs)));
            if (c.When.Count > 0) rows.Add(("When", string.Join(" · ", c.When)));
            if (c.Conditions.Count > 0) rows.Add(("Conditions", string.Join(", ", c.Conditions)));
            if (c.Injuries.Count > 0) rows.Add(("Injuries", string.Join("\n", c.Injuries)));
        }
        return rows;
    }

    private UIElement Rows(List<(string Label, string Text)> rows)
    {
        var list = new StackPanel();
        foreach (var (label, text) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var l = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 12.5 };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            row.Children.Add(l);
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            list.Children.Add(row);
        }
        return list;
    }

    private TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 6) };

    private UIElement DriverSection()
    {
        var stack = new StackPanel();
        if (_report.DriverIssues.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var i in _report.DriverIssues)
            {
                var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6) };
                chip.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceWarm");
                var t = new TextBlock { Text = Capital(i), FontSize = 12, FontWeight = FontWeights.SemiBold };
                t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.WarmText");
                chip.Child = t;
                wrap.Children.Add(chip);
            }
            stack.Children.Add(wrap);
        }
        stack.Children.Add(TimedList(_report.DriverNotes.Concat(_report.CabinNotes).OrderBy(f => f.Seconds)));
        return stack;
    }

    private UIElement RoadUsers()
    {
        var list = new StackPanel();
        foreach (var u in _report.RoadUsers.Take(40))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var tag = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            tag.SetResourceReference(Border.BackgroundProperty, u.Involved ? "Brush.Highlight" : "Brush.SurfaceAlt");
            var tt = new TextBlock { Text = u.Tag, FontSize = 11.5, FontWeight = FontWeights.Bold };
            tt.SetResourceReference(TextBlock.ForegroundProperty, u.Involved ? "Brush.Sidebar" : "Brush.TextMuted");
            tag.Child = tt;
            row.Children.Add(tag);
            var name = new StackPanel();
            name.Children.Add(new TextBlock { Text = Capital(u.Label), FontWeight = u.Involved ? FontWeights.SemiBold : FontWeights.Normal });
            var seen = new TextBlock { Text = $"{Clock(u.FirstSeconds)}–{Clock(u.LastSeconds)}", FontSize = 11 };
            seen.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            name.Children.Add(seen);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var what = string.Join(" · ", new[] { u.Involved ? Capital(u.Role) : "Not involved", u.Where }.Concat(u.Notes).Where(x => x.Length > 0));
            var info = new TextBlock { Text = _review.Describe(what), TextWrapping = TextWrapping.Wrap };
            if (!u.Involved) info.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetColumn(info, 2);
            row.Children.Add(info);
            list.Children.Add(row);
        }
        if (_report.RoadUsers.Count > 40)
        {
            var more = new TextBlock { Text = $"…and {_report.RoadUsers.Count - 40} more, none of them involved.", FontSize = 12 };
            more.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            list.Children.Add(more);
        }
        return list;
    }

    private UIElement DocumentsSection()
    {
        var stack = new StackPanel();
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var file = MakeButton("Attach file…", "GhostButton", "\uE723");
        file.ToolTip = "A police report, insurance or witness statement: PDF, Word, text, a saved web page, or a photo or scan of the page";
        file.Click += async (_, _) => await AttachFiles();
        var link = MakeButton("Add web link…", "GhostButton", "\uE71B");
        link.ToolTip = "A news story or police press release online";
        link.Margin = new Thickness(8, 0, 0, 0);
        link.Click += async (_, _) => await AttachLink();
        var paste = MakeButton("Paste text…", "GhostButton", "\uE77F");
        paste.ToolTip = "Type or paste a narrative, a statement or notes";
        paste.Margin = new Thickness(8, 0, 0, 0);
        paste.Click += (_, _) => PasteText();
        buttons.Children.Add(file); buttons.Children.Add(link); buttons.Children.Add(paste);
        stack.Children.Add(buttons);

        if (_review.Documents.Count == 0)
        {
            stack.Children.Add(Muted("Attach the police report, a news story or a statement. Guardrail reads it on this PC — citations, contributing factors, " +
                                     "who hit whom, where and when — and weighs it with what the video shows. Officers' findings weigh the most."));
            return stack;
        }

        foreach (var doc in _review.Documents)
        {
            var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 10, 10), Margin = new Thickness(0, 0, 0, 8) };
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = doc.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var f = doc.Facts;
            string meta = doc.KindText + (doc.Ocr ? " · read with text recognition" : "") + (doc.Source == "link" ? " · web page" : doc.Source == "text" ? " · pasted text" : "");
            left.Children.Add(Muted(meta));
            var found = f.Parties.Where(p => p.Score > 0 || p.Struck > 0).Select(p =>
            {
                var bits = new List<string>();
                if (p.Citations.Count > 0) bits.Add("cited");
                bits.AddRange(p.Factors.OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
                if (p.AtFault > 0) bits.Add("said to be at fault");
                if (p.RearEnded > 0) bits.Add("rear-ended another vehicle");
                else if (p.Struck > 0 && bits.Count == 0) bits.Add("was struck");
                return $"{p.Label}: {string.Join(", ", bits.Distinct())}";
            }).ToList();
            if (found.Count > 0) foreach (var line in found.Take(5)) left.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            else left.Children.Add(Muted("No one is blamed in this document."));
            g.Children.Add(left);
            var remove = new Button { Style = (Style)FindResource("IconButton"), Content = new TextBlock { Text = "\uE74D", Style = (Style)FindResource("Icon"), FontSize = 13 } };
            remove.ToolTip = "Remove this document";
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Click += (_, _) => { CrashDocuments.Remove(_review, doc); Refresh(); };
            Grid.SetColumn(remove, 1);
            g.Children.Add(remove);
            card.Child = g;
            if (doc.Location.Length > 0) card.ToolTip = doc.Location;
            stack.Children.Add(card);
        }

        // Which document vehicle is the camera vehicle?
        var units = _review.Documents.SelectMany(d => d.Facts.Parties).Where(p => !p.IsPerson || p.Key is "suspect")
            .GroupBy(p => p.Key).Select(g => g.First()).ToList();
        if (units.Count > 0 && _report.CameraKind.StartsWith("Dashcam"))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = "In the documents, the camera vehicle is", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var box = new ComboBox { MinWidth = 240 };
            box.Items.Add(new ComboBoxItem { Content = "Worked out automatically", Tag = "" });
            foreach (var p in units) box.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Key });
            box.Items.Add(new ComboBoxItem { Content = "Not in the documents", Tag = "none" });
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _review.MyDocParty) ?? box.Items[0];
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem { Tag: string key } && key != _review.MyDocParty) { CrashDocuments.SetMyParty(_review, key); Refresh(); }
            };
            row.Children.Add(box);
            stack.Children.Add(row);
        }
        if (_case != null)
        {
            foreach (var n in _case.Notes.Skip(_case.UsedDocuments ? 1 : 0)) stack.Children.Add(Muted(_review.Describe(n)));
            var matched = _case.Matches.Where(m => m.Matched != m.DocParty).Distinct().ToList();
            if (matched.Count > 0)
                stack.Children.Add(Muted("Matched to the video: " + string.Join("; ", matched.Select(m => $"{m.DocParty} → {_review.Describe(m.Matched)}"))));
        }
        return stack;
    }

    private TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 2) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    private void Refresh()
    {
        Build();
        Services.CrashReviewService.NotifyChanged();
    }

    private async Task AttachFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Attach documents to this crash review", Filter = CrashDocuments.Filter, Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var errors = new List<string>();
            foreach (var path in dlg.FileNames)
                if (await CrashDocuments.AddFileAsync(_review, path) is { } err) errors.Add(err);
            Refresh();
            if (errors.Count > 0) MessageDialog.Inform("Some documents couldn't be read", string.Join("\n\n", errors));
            else Toaster.Show(dlg.FileNames.Length == 1 ? "Document attached and read." : $"{dlg.FileNames.Length} documents attached and read.");
        }
        finally { Cursor = null; }
    }

    private async Task AttachLink()
    {
        var form = new FormDialog("Add a web link", "A news story or police press release about this crash. The page's text is read and kept with the review.",
            [new FormField { Key = "url", Label = "Web address", Required = true, Hint = "https://…" }], allowDelete: false, saveText: "Add");
        if (form.ShowDialog() != true) return;
        string url = form.Text("url").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { Toaster.Show("That isn't a web address."); return; }
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            if (await CrashDocuments.AddLinkAsync(_review, url) is { } err) MessageDialog.Inform("Couldn't add the link", err);
            else Toaster.Show("Web page read and attached.");
            Refresh();
        }
        finally { Cursor = null; }
    }

    private void PasteText()
    {
        var form = new FormDialog("Paste text", "Paste a report narrative, a statement or notes. Mention vehicles the way the report does (Unit 1, the red Dodge Charger…).",
            [new FormField { Key = "title", Label = "Title", Value = "Police report narrative" },
             new FormField { Key = "text", Label = "Text", Kind = FieldKind.MultiLine, Required = true }], allowDelete: false, saveText: "Attach");
        if (form.ShowDialog() != true || form.Text("text").Trim().Length == 0) return;
        CrashDocuments.AddText(_review, form.Text("title"), form.Text("text"));
        Refresh();
    }

    private UIElement TimedList(IEnumerable<Finding> items)
    {
        var list = new StackPanel();
        foreach (var f in items)
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
            var text = new TextBlock { Text = _review.Describe(f.Text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
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

    /// <summary>Names the camera vehicle (suggesting fleet units), then reopens the report with the new wording.</summary>
    private void NameVehicle()
    {
        var units = DataStore.Vehicles.Items.Select(v => VideoReview.NameForUnit(v.UnitNumber.Trim())).Where(n => n.Length > 0).Distinct().OrderBy(n => n).ToArray();
        var dialog = new FormDialog("Name the camera vehicle",
            "The report will say this instead of \"the camera vehicle\" (for example \"Unit 104 drifted across the centre line\").",
            [new FormField { Key = "name", Label = "Vehicle", Value = _review.CameraName, Suggestions = units, Hint = units.Length > 0 ? "Pick a fleet unit or type any name. Leave blank for \"the camera vehicle\"." : "e.g. Unit 104, Truck 12, Service van 3. Leave blank for \"the camera vehicle\"." }],
            allowDelete: false, saveText: "Save");
        if (dialog.ShowDialog() != true) return;
        CrashReviewService.SetCameraName(_review, dialog.Text("name"));
        Close();
        new CrashReportDialog(_review).ShowDialog();
    }

    private static readonly (string Key, string Text)[] CameraChoices =
    [
        ("auto", "Work it out from the video"),
        ("moving", "Dashcam in a moving vehicle"),
        ("fixed", "Fixed camera (CCTV or a parked vehicle)"),
        ("handheld", "Phone or handheld camera"),
    ];

    /// <summary>Lets the person say what filmed the video, then re-runs the analysis with that.</summary>
    private void ChooseCamera()
    {
        string current = CameraChoices.FirstOrDefault(c => c.Key == _review.CameraHint).Text ?? CameraChoices[0].Text;
        var form = new FormDialog("What filmed this video?",
            $"The AI judged it as: {_report.CameraKind}. If that's wrong, pick the right one and the video is analyzed again. " +
            "A fixed camera is never a party to the crash; a dashcam's vehicle always is a possible one.",
            [new FormField { Key = "cam", Label = "Camera", Kind = FieldKind.Choice, Options = CameraChoices.Select(c => c.Text).ToArray(), Value = current }],
            allowDelete: false, saveText: "Analyze again");
        if (form.ShowDialog() != true) return;
        string key = CameraChoices.FirstOrDefault(c => c.Text == form.Text("cam")).Key ?? "auto";
        CrashDocuments.SetCameraHint(_review, key);
        Close();
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
