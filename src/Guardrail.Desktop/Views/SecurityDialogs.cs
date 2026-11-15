using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>A small form on the shared dialog frame: labelled fields, an error banner, Cancel + a primary button.</summary>
public abstract class FormFrame : DialogFrame
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(28, 0, 28, 16) };
    private readonly Border _errorBar;
    private readonly TextBlock _error;
    private readonly Button _cancel;
    protected readonly Button SaveButton;

    protected FormFrame(string title, string subtitle, string glyph, string saveText, double width = 520) : base(title, subtitle, glyph, width)
    {
        _errorBar = MakeErrorBar(out _error);
        Body.Children.Add(_errorBar);
        SetBody(Body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _cancel = MakeButton("Cancel", "GhostButton");
        _cancel.IsCancel = true;
        _cancel.Margin = new Thickness(0, 0, 10, 0);
        SaveButton = MakeButton(saveText, "PrimaryButton");
        SaveButton.IsDefault = true;
        SaveButton.MinWidth = 120;
        SaveButton.Click += async (_, _) =>
        {
            SaveButton.IsEnabled = false;
            Cursor = Cursors.Wait;
            string? problem;
            try { problem = await TrySaveAsync(); }
            finally { Cursor = null; SaveButton.IsEnabled = true; }
            if (problem is null) DialogResult = true;
            else ShowError(_errorBar, _error, problem);
        };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(SaveButton);
        Footer.Children.Add(buttons);
    }

    protected void HideCancel() => _cancel.Visibility = Visibility.Collapsed;

    /// <summary>Returns null when done, or what's wrong.</summary>
    protected abstract Task<string?> TrySaveAsync();

    protected T AddField<T>(string label, T input, string? hint = null) where T : FrameworkElement
    {
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        wrap.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        wrap.Children.Add(input);
        if (hint != null)
        {
            var h = new TextBlock { Text = hint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            wrap.Children.Add(h);
        }
        Body.Children.Add(wrap);
        return input;
    }

    protected WrapPanel Chips(IEnumerable<(string Value, string Label)> options, string selected)
    {
        var wrap = new WrapPanel();
        string group = "g" + Guid.NewGuid().ToString("N");
        foreach (var (value, label) in options)
            wrap.Children.Add(new RadioButton { Content = label, Tag = value, GroupName = group, IsChecked = value == selected, Style = (Style)FindResource("ChipRadio") });
        return wrap;
    }

    protected static string? Selected(WrapPanel chips) => chips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
}

/// <summary>Turn the lock on, or change the PIN / password.</summary>
public sealed class SetLockDialog : FormFrame
{
    private readonly bool _change;
    private readonly WrapPanel _kind;
    private readonly PasswordBox _first = new(), _second = new();
    private readonly WrapPanel? _autoLock;

    public static readonly (int Minutes, string Label)[] AutoLockChoices =
        [(5, "5 minutes"), (10, "10 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 hour"), (0, "Never")];

    /// <summary>The recovery key to show once (only when protection was just turned on).</summary>
    public string? RecoveryKey { get; private set; }

    public SetLockDialog(bool change)
        : base(change ? "Change PIN or password" : "Protect Guardrail",
               change ? "Choose a new PIN or password. Your recovery key keeps working."
                      : "Your records and attached documents will be encrypted on this PC. You'll need the PIN or password to open Guardrail.",
               "", change ? "Change" : "Turn on protection")
    {
        _change = change;
        _kind = AddField("Use a", Chips([("PIN", "PIN"), ("Password", "Password")], DataLock.Info?.Kind ?? "PIN"),
            "A PIN (4–12 digits) is quick to type on a shared shop PC. A password (8+ characters) is stronger.");
        AddField(change ? "New PIN or password" : "PIN or password", _first);
        AddField("Type it again", _second);
        if (!change)
            _autoLock = AddField("Lock the window after this long without use", Chips(AutoLockChoices.Select(c => (c.Minutes.ToString(), c.Label)), "10"),
                "Dashcam and CCTV videos aren't encrypted (they're large and Crash Review reads them directly).");
        Loaded += (_, _) => _first.Focus();
    }

    protected override async Task<string?> TrySaveAsync()
    {
        string kind = Selected(_kind) ?? "PIN";
        string secret = _first.Password;
        if (DataLock.Validate(secret, kind) is string problem) return problem;
        if (secret != _second.Password) return "The two entries don't match.";
        int minutes = int.Parse(_autoLock is null ? "10" : Selected(_autoLock) ?? "10");
        try
        {
            if (_change) await Task.Run(() => DataLock.ChangeSecret(secret, kind));
            else RecoveryKey = await Task.Run(() => DataLock.Enable(secret, kind, minutes));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"Couldn't {(_change ? "change it" : "turn on protection")}: {ex.Message}";
        }
    }
}

/// <summary>Shows the recovery key once, with copy / save / print. Can't be closed until you confirm you've saved it.</summary>
public sealed class RecoveryKeyDialog : FormFrame
{
    private readonly CheckBox _saved = new() { Content = "I've saved my recovery key somewhere safe" };

    /// <summary>Lets the self-test close it without ticking the box.</summary>
    public bool ForceClose { get; set; }

    public RecoveryKeyDialog(string key)
        : base("Save your recovery key", "If you ever forget your PIN or password, this key is the only way to open your data. Nobody can reset it for you.",
               "", "Done", 560)
    {
        HideCancel();
        var box = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 12) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        box.Child = new TextBox
        {
            Text = key, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        Body.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var copy = MakeButton("Copy", "GhostButton", "");
        copy.Margin = new Thickness(0, 0, 8, 0);
        copy.Click += (_, _) => { Clipboard.SetText(key); Toaster.Show("Recovery key copied. Paste it somewhere safe, like a password manager."); };
        var save = MakeButton("Save to a file…", "GhostButton", "");
        save.Margin = new Thickness(0, 0, 8, 0);
        save.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Guardrail recovery key.txt", Filter = "Text file (*.txt)|*.txt" };
            if (dlg.ShowDialog(this) == true)
                File.WriteAllText(dlg.FileName, $"Guardrail EHS recovery key\r\n\r\n{key}\r\n\r\nKeep this somewhere safe and away from this computer (printed, or in a password manager).\r\n");
        };
        var print = MakeButton("Print", "GhostButton", "");
        print.Click += (_, _) =>
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            var panel = new StackPanel { Margin = new Thickness(60) };
            panel.Children.Add(new TextBlock { Text = "Guardrail EHS recovery key", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
            panel.Children.Add(new TextBlock { Text = key, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 24 });
            panel.Children.Add(new TextBlock { Text = $"Created {DateTime.Now:MMMM d, yyyy}. Keep this page somewhere safe.", Margin = new Thickness(0, 16, 0, 0) });
            panel.Measure(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight));
            panel.Arrange(new Rect(new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight)));
            pd.PrintVisual(panel, "Guardrail recovery key");
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(print);
        Body.Children.Add(buttons);
        Body.Children.Add(_saved);
        Closing += (_, e) => { if (DialogResult != true && !ForceClose) e.Cancel = _saved.IsChecked != true; };
    }

    protected override Task<string?> TrySaveAsync() =>
        Task.FromResult(_saved.IsChecked == true ? null : "Save, copy or print the key, then tick the box to confirm.");
}

/// <summary>Asks for the current PIN / password (before turning protection off or changing it).</summary>
public sealed class VerifySecretDialog : FormFrame
{
    private readonly PasswordBox _box = new();

    public VerifySecretDialog(string title, string message, string saveText) : base(title, message, "", saveText, 480)
    {
        AddField(DataLock.IsPin ? "Current PIN" : "Current password", _box);
        Loaded += (_, _) => _box.Focus();
    }

    protected override async Task<string?> TrySaveAsync()
    {
        string secret = _box.Password;
        return await Task.Run(() => DataLock.Unlock(secret)) ? null : "That's not right.";
    }
}
