using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>
/// The look shared by every Guardrail dialog: a floating rounded card (no Windows frame) with an icon badge,
/// title, subtitle and close button, a body, and a footer for buttons. The main window dims behind it and the
/// card eases in. Subclasses fill <see cref="Body"/> and <see cref="Footer"/>.
/// </summary>
public abstract class DialogFrame : Window
{
    private const double ShadowRoom = 28, Radius = 18;

    private readonly Border _card = new() { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1) };
    private readonly TextBlock _glyph = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badgeTint = new() { CornerRadius = new CornerRadius(12) };
    private readonly ContentControl _body = new() { Focusable = false };

    /// <summary>Buttons go here: dock things Left (e.g. Delete) and add a right-aligned StackPanel for the rest.</summary>
    protected readonly DockPanel Footer = new();

    /// <summary>The dialog's card (without the transparent shadow margin).</summary>
    public FrameworkElement Card => _card;

    protected DialogFrame(string title, string subtitle, string glyph, double width = 580)
    {
        Title = title;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/guardrail.ico"));
        Width = width + ShadowRoom * 2;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.MainWindow is { IsLoaded: true } owner && owner != this) Owner = owner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13.5;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        SetResourceReference(ForegroundProperty, "Brush.Text");

        // ----- Card -----
        _card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _card.Margin = new Thickness(ShadowRoom);
        _card.Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Direction = 270, Opacity = 0.28, Color = Colors.Black };
        _card.RenderTransformOrigin = new Point(0.5, 0.5);
        _card.RenderTransform = new ScaleTransform(1, 1);
        var root = new DockPanel();

        // ----- Header: badge, title + subtitle, close (drag the header to move the dialog) -----
        var header = new DockPanel { Margin = new Thickness(28, 24, 18, 14), Background = Brushes.Transparent };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                try { DragMove(); } catch (InvalidOperationException) { /* not while the mouse is released */ }
        };

        var badge = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 16, 0) };
        _badgeTint.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        _glyph.Style = (Style)FindResource("Icon");
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _glyph.Text = glyph;
        badge.Children.Add(_badgeTint);
        badge.Children.Add(_glyph);
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);

        var close = new Button
        {
            Style = (Style)FindResource("BaseButton"), Padding = new Thickness(8), MinHeight = 32, MinWidth = 32,
            VerticalAlignment = VerticalAlignment.Top, ToolTip = "Close (Esc)", Focusable = false,
            Content = new TextBlock { Text = "", FontSize = 12, Style = (Style)FindResource("Icon") },
        };
        AutomationProperties.SetName(close, "Close");
        close.SetResourceReference(ForegroundProperty, "Brush.TextMuted");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        titles.Children.Add(new TextBlock
        {
            Text = title, FontSize = 20, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis,
            FontFamily = (FontFamily)FindResource("DisplayFont"),
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            titles.Children.Add(sub);
        }
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ----- Footer -----
        var footer = new Border
        {
            Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, Radius - 1, Radius - 1), Child = Footer,
        };
        footer.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(_body);
        _card.Child = root;
        Content = _card;

        Loaded += (_, _) => { MainWindow.Instance?.SetDimmed(true); Animate(); };
        Closed += (_, _) => MainWindow.Instance?.SetDimmed(false);
    }

    /// <summary>Puts content in the body. Scrolls when it's taller than the screen allows.</summary>
    protected void SetBody(UIElement content, bool scroll = true)
    {
        // Header + footer + shadow take about 250px; keep the dialog on small laptop screens too.
        double room = Math.Max(240, SystemParameters.WorkArea.Height - 250);
        _body.Content = scroll
            ? new ScrollViewer { Content = content, MaxHeight = Math.Min(620, room), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }
            : content;
    }

    /// <summary>Red badge for warnings, errors and deletes.</summary>
    protected void UseDangerTone(string glyph = "")
    {
        _glyph.Text = glyph;
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        _badgeTint.SetResourceReference(Border.BackgroundProperty, "Brush.Danger");
        _badgeTint.Opacity = 0.14;
    }

    /// <summary>A rounded red banner for validation messages. Add it to the body; call <see cref="ShowError"/> to fill it.</summary>
    protected static Border MakeErrorBar(out TextBlock text)
    {
        text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        var bar = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 14),
            BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
        };
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
        var tint = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.10, Margin = new Thickness(-12, -9, -12, -9) };
        tint.SetResourceReference(Border.BackgroundProperty, "Brush.Danger");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = "", FontSize = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(StyleProperty, "Icon");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        row.Children.Add(icon);
        row.Children.Add(text);
        var grid = new Grid();
        grid.Children.Add(tint);
        grid.Children.Add(row);
        bar.Child = grid;
        return bar;
    }

    protected static void ShowError(Border bar, TextBlock text, string message)
    {
        text.Text = message;
        bar.Visibility = Visibility.Visible;
    }

    protected Button MakeButton(string text, string styleKey, string? glyph = null)
    {
        var button = new Button { Style = (Style)FindResource(styleKey), MinWidth = 96 };
        if (glyph is null) button.Content = text;
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
        }
        return button;
    }

    private void Animate()
    {
        if (SelfTest.IsRunning) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(190);
        _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = ease });
        var scale = (ScaleTransform)_card.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Picks a badge icon from words in the title.</summary>
    protected static string GlyphFor(string title)
    {
        string t = title.ToLowerInvariant();
        (string Word, string Glyph)[] map =
        [
            ("incident", ""), ("accident", ""), ("action", ""), ("behavioral", ""), ("observation", ""),
            ("training", ""), ("requirement", ""), ("claim", ""), ("document", ""), ("month", ""),
            ("permit", ""), ("loto", ""), ("procedure", ""), ("product", ""), ("assessment", ""),
            ("change", ""), ("checklist", ""), ("inspection", ""), ("driver", ""), ("vehicle", ""),
            ("unit", ""), ("dvir", ""), ("repair", ""), ("service", ""), ("log", ""),
        ];
        foreach (var (word, glyph) in map) if (t.Contains(word)) return glyph;
        return t.StartsWith("edit") ? "" : "";
    }
}
