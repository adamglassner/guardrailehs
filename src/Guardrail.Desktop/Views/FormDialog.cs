using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guardrail.Services;

namespace Guardrail.Views;

public enum FieldKind { Text, MultiLine, Date, Choice }

public sealed class FormField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public bool Required { get; init; }
    public string[] Options { get; init; } = [];
    public string? Hint { get; init; }
    /// <summary>Initial value: string for Text/MultiLine/Choice, DateTime? for Date.</summary>
    public object? Value { get; init; }
}

/// <summary>
/// A themed add/edit dialog built from a list of fields. Used by every module that works today,
/// so new modules only need to describe their fields.
/// </summary>
public sealed class FormDialog : Window
{
    private readonly List<FormField> _fields;
    private readonly Dictionary<string, Func<object?>> _readers = [];
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, object?> _values = [];
    private UIElement? _firstInput;

    public bool DeleteRequested { get; private set; }
    public IReadOnlyDictionary<string, object?> Values => _values;

    public FormDialog(string title, string subtitle, IEnumerable<FormField> fields, bool allowDelete, string saveText = "Save")
    {
        _fields = fields.ToList();

        Title = title;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/guardrail.ico"));
        Width = 580;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Brush.Bg");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        Loaded += (_, _) => _firstInput?.Focus();

        var root = new DockPanel();

        // Header
        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 10) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        var sub = new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        header.Children.Add(sub);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Footer
        var footer = new Border { Padding = new Thickness(24, 12, 24, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var footerDock = new DockPanel();
        if (allowDelete)
        {
            var delete = new Button { Content = "Delete", Style = (Style)FindResource("GhostButton") };
            delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
            delete.Click += (_, _) =>
            {
                if (delete.Content as string == "Delete")
                {
                    delete.Content = "Click again to delete";
                    return;
                }
                DeleteRequested = true;
                DialogResult = true;
            };
            DockPanel.SetDock(delete, Dock.Left);
            footerDock.Children.Add(delete);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = saveText, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) => TrySave();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footerDock.Children.Add(buttons);
        footer.Child = footerDock;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // Fields
        var form = new StackPanel { Margin = new Thickness(24, 6, 24, 14) };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        form.Children.Add(_error);
        foreach (var field in _fields) form.Children.Add(BuildField(field));
        root.Children.Add(new ScrollViewer { Content = form, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Content = root;
    }

    private FrameworkElement BuildField(FormField field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var label = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
        label.Inlines.Add(field.Label);
        if (field.Required)
        {
            var star = new System.Windows.Documents.Run(" *");
            star.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Danger");
            label.Inlines.Add(star);
        }
        panel.Children.Add(label);

        UIElement input;
        switch (field.Kind)
        {
            case FieldKind.MultiLine:
            {
                var box = new TextBox
                {
                    Text = field.Value as string ?? "",
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 96,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                _readers[field.Key] = () => box.Text.Trim();
                input = box;
                break;
            }
            case FieldKind.Date:
            {
                var picker = new DatePicker { SelectedDate = field.Value as DateTime?, HorizontalAlignment = HorizontalAlignment.Left, Width = 200 };
                _readers[field.Key] = () => picker.SelectedDate;
                input = picker;
                break;
            }
            case FieldKind.Choice:
            {
                var wrap = new WrapPanel();
                string group = field.Key + Guid.NewGuid().ToString("N");
                string selected = field.Value as string ?? field.Options.FirstOrDefault() ?? "";
                foreach (var option in field.Options)
                {
                    wrap.Children.Add(new RadioButton
                    {
                        Content = option,
                        GroupName = group,
                        IsChecked = option == selected,
                        Style = (Style)FindResource("ChipRadio"),
                    });
                }
                _readers[field.Key] = () => wrap.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "";
                input = wrap;
                break;
            }
            default:
            {
                var box = new TextBox { Text = field.Value as string ?? "" };
                _readers[field.Key] = () => box.Text.Trim();
                input = box;
                break;
            }
        }
        _firstInput ??= input is WrapPanel ? null : input;
        panel.Children.Add(input);

        if (!string.IsNullOrEmpty(field.Hint))
        {
            var hint = new TextBlock { Text = field.Hint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            panel.Children.Add(hint);
        }
        return panel;
    }

    private void TrySave()
    {
        _values.Clear();
        var missing = new List<string>();
        foreach (var field in _fields)
        {
            var value = _readers[field.Key]();
            _values[field.Key] = value;
            bool empty = value is null || (value is string s && s.Length == 0);
            if (field.Required && empty) missing.Add(field.Label);
        }

        if (missing.Count > 0)
        {
            _error.Text = "Please fill in: " + string.Join(", ", missing) + ".";
            _error.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Ctrl+Enter saves even from inside a multi-line box.
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            TrySave();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    public string Text(string key) => _values.TryGetValue(key, out var v) ? v as string ?? "" : "";
    public DateTime? Date(string key) => _values.TryGetValue(key, out var v) ? v as DateTime? : null;
}
