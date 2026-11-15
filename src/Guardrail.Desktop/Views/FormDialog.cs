using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public enum FieldKind { Text, MultiLine, Date, Choice, Number, File }

public sealed class FormField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public bool Required { get; init; }
    public string[] Options { get; init; } = [];
    /// <summary>Text fields: values to offer in a dropdown (typing anything else is still allowed).
    /// Fields with the key "location" get the locations from Settings automatically.</summary>
    public string[]? Suggestions { get; init; }
    public string? Hint { get; init; }
    /// <summary>Initial value: string for Text/MultiLine/Choice/File, DateTime? for Date, decimal? for Number.</summary>
    public object? Value { get; init; }
}

/// <summary>
/// The add/edit dialog every working module uses: a floating card built from a list of fields,
/// so new modules only need to describe their fields.
/// </summary>
public sealed class FormDialog : DialogFrame
{
    private readonly List<FormField> _fields;
    private readonly Dictionary<string, Func<object?>> _readers = [];
    private readonly Border _errorBar;
    private readonly TextBlock _error;
    private readonly Dictionary<string, object?> _values = [];
    private readonly HashSet<string> _numberFields = [];
    private UIElement? _firstInput;

    public bool DeleteRequested { get; private set; }
    public IReadOnlyDictionary<string, object?> Values => _values;

    public FormDialog(string title, string subtitle, IEnumerable<FormField> fields, bool allowDelete, string saveText = "Save")
        : base(title, subtitle, GlyphFor(title))
    {
        _fields = fields.ToList();
        Loaded += (_, _) => _firstInput?.Focus();

        // Footer: Delete (two clicks) on the left, Cancel + Save on the right.
        if (allowDelete)
        {
            var delete = MakeButton("Delete", "GhostButton", "\uE74D");
            delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
            delete.Click += (_, _) =>
            {
                var label = ((StackPanel)delete.Content).Children.OfType<TextBlock>().Last();
                if (label.Text == "Delete")
                {
                    label.Text = "Click again to delete";
                    return;
                }
                DeleteRequested = true;
                DialogResult = true;
            };
            DockPanel.SetDock(delete, Dock.Left);
            Footer.Children.Add(delete);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Cancel", "GhostButton");
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        var save = MakeButton(saveText, "PrimaryButton");
        save.IsDefault = true;
        save.MinWidth = 120;
        save.Click += (_, _) => TrySave();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Footer.Children.Add(buttons);

        // Body: error banner, then the fields.
        var form = new StackPanel { Margin = new Thickness(28, 4, 28, 12) };
        _errorBar = MakeErrorBar(out _error);
        form.Children.Add(_errorBar);
        foreach (var field in _fields) form.Children.Add(BuildField(field));
        SetBody(form);
    }

    private FrameworkElement BuildField(FormField field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var label = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
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
                    Height = 100,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                _readers[field.Key] = () => box.Text.Trim();
                input = box;
                break;
            }
            case FieldKind.Date:
            {
                var picker = new DatePicker { SelectedDate = field.Value as DateTime?, HorizontalAlignment = HorizontalAlignment.Left, Width = 270 };
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
            case FieldKind.Number:
            {
                var box = new TextBox
                {
                    Text = field.Value is decimal d ? d.ToString("0.##", CultureInfo.CurrentCulture) : "",
                    Width = 200,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                _readers[field.Key] = () => box.Text.Trim();
                _numberFields.Add(field.Key);
                input = box;
                break;
            }
            case FieldKind.File:
            {
                // Value = path of the current file (or null). Reader returns the chosen path.
                string? chosen = field.Value as string;
                var name = new TextBlock
                {
                    Text = string.IsNullOrEmpty(chosen) ? "No file chosen" : System.IO.Path.GetFileName(chosen),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                var pick = MakeButton("Choose file…", "GhostButton", "\uE8E5");
                pick.Click += (_, _) =>
                {
                    var ofd = new Microsoft.Win32.OpenFileDialog { Title = $"Choose a file for {field.Label}", Filter = "All files (*.*)|*.*" };
                    if (ofd.ShowDialog(this) == true)
                    {
                        chosen = ofd.FileName;
                        name.Text = System.IO.Path.GetFileName(chosen);
                    }
                };
                var row = new DockPanel();
                DockPanel.SetDock(pick, Dock.Left);
                row.Children.Add(pick);
                row.Children.Add(name);
                _readers[field.Key] = () => chosen ?? "";
                input = row;
                break;
            }
            default:
            {
                string value = field.Value as string ?? "";
                var suggestions = field.Suggestions ?? (field.Key == "location" ? LocationNames.ActiveNames() : null);
                if (suggestions is { Length: > 0 })
                {
                    var items = suggestions.ToList();
                    if (value.Length > 0 && !items.Contains(value, StringComparer.OrdinalIgnoreCase)) items.Insert(0, value);
                    var combo = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, ItemsSource = items, Text = value };
                    _readers[field.Key] = () => combo.Text.Trim();
                    input = combo;
                }
                else
                {
                    var box = new TextBox { Text = value };
                    _readers[field.Key] = () => box.Text.Trim();
                    input = box;
                }
                break;
            }
        }
        _firstInput ??= input is WrapPanel or DockPanel ? null : input;
        panel.Children.Add(input);

        if (!string.IsNullOrEmpty(field.Hint))
        {
            var hint = new TextBlock { Text = field.Hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
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
            if (_numberFields.Contains(field.Key) && value is string raw)
            {
                // Accept "2,500", "$2,500.50" or "2500".
                var cleaned = raw.Replace(CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol, "").Trim();
                if (cleaned.Length == 0) value = null;
                else if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out var number) && number >= 0) value = number;
                else
                {
                    ShowError(_errorBar, _error, $"{field.Label} must be a number, like 2500 or 2,500.50.");
                    return;
                }
            }
            _values[field.Key] = value;
            bool empty = value is null || (value is string s && s.Length == 0);
            if (field.Required && empty) missing.Add(field.Label);
        }

        if (missing.Count > 0)
        {
            ShowError(_errorBar, _error, "Please fill in: " + string.Join(", ", missing) + ".");
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
    public decimal? Number(string key) => _values.TryGetValue(key, out var v) ? v as decimal? : null;
}
