using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>The unlock card: PIN / password, or the recovery key if it's been forgotten.</summary>
public sealed class LockPanel : UserControl
{
    private readonly TextBlock _prompt = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 18) };
    private readonly PasswordBox _secret = new() { FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 44 };
    private readonly TextBox _recovery = new() { FontSize = 16, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 44, CharacterCasing = CharacterCasing.Upper, Visibility = Visibility.Collapsed };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _unlock;
    private readonly Button _switch;
    private bool _recoveryMode;
    private int _failures;
    private DateTime _waitUntil = DateTime.MinValue;

    /// <summary>Raised after a successful unlock. True when the recovery key was used (so a new PIN should be set).</summary>
    public event Action<bool>? Unlocked;

    public LockPanel()
    {
        var card = new Border
        {
            Width = 420, CornerRadius = new CornerRadius(20), Padding = new Thickness(36, 34, 36, 28), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Direction = 270, Opacity = 0.22 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var stack = new StackPanel();

        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/guardrail.png")), Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        stack.Children.Add(logo);
        var title = new TextBlock
        {
            Text = "Guardrail is locked", FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
        };
        stack.Children.Add(title);
        _prompt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(_prompt);
        stack.Children.Add(_secret);
        stack.Children.Add(_recovery);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        stack.Children.Add(_error);

        _unlock = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new Thickness(0, 18, 0, 0), MinHeight = 42, IsDefault = true };
        var unlockRow = new StackPanel { Orientation = Orientation.Horizontal };
        unlockRow.Children.Add(new TextBlock { Text = "", Style = (Style)Application.Current.FindResource("Icon"), Margin = new Thickness(0, 0, 8, 0) });
        unlockRow.Children.Add(new TextBlock { Text = "Unlock" });
        _unlock.Content = unlockRow;
        _unlock.Click += async (_, _) => await TryUnlock();
        stack.Children.Add(_unlock);

        _switch = new Button { Style = (Style)Application.Current.FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        _switch.Click += (_, _) => SetMode(!_recoveryMode);
        stack.Children.Add(_switch);

        var note = new TextBlock
        {
            Text = "Your records are encrypted on this PC.", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(note);

        card.Child = stack;
        Content = card;
        _secret.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await TryUnlock(); } };
        _recovery.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await TryUnlock(); } };
        SetMode(false);
    }

    /// <summary>Clears what was typed and puts the cursor in the box.</summary>
    public void Reset()
    {
        _secret.Clear();
        _recovery.Clear();
        _error.Visibility = Visibility.Collapsed;
        SetMode(false);
        Dispatcher.BeginInvoke(() => _secret.Focus(), DispatcherPriority.Input);
    }

    private void SetMode(bool recovery)
    {
        _recoveryMode = recovery;
        _secret.Visibility = recovery ? Visibility.Collapsed : Visibility.Visible;
        _recovery.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        _prompt.Text = recovery
            ? "Enter the recovery key you saved when you turned on protection (like ABCDE-FGHJK-…)."
            : DataLock.IsPin ? "Enter your PIN to open Guardrail." : "Enter your password to open Guardrail.";
        _switch.Content = recovery ? (DataLock.IsPin ? "Use my PIN instead" : "Use my password instead") : "Forgot it? Use your recovery key";
        _error.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(() => { if (recovery) _recovery.Focus(); else _secret.Focus(); }, DispatcherPriority.Input);
    }

    private async Task TryUnlock()
    {
        if (DateTime.Now < _waitUntil)
        {
            ShowError($"Too many tries. Wait {(int)Math.Ceiling((_waitUntil - DateTime.Now).TotalSeconds)} seconds.");
            return;
        }
        bool recovery = _recoveryMode;
        string secret = recovery ? _recovery.Text : _secret.Password;
        if (secret.Length == 0) return;
        _unlock.IsEnabled = false;
        Cursor = Cursors.Wait;
        bool ok;
        try { ok = await Task.Run(() => recovery ? DataLock.UnlockWithRecoveryKey(secret) : DataLock.Unlock(secret)); }
        finally { _unlock.IsEnabled = true; Cursor = null; }

        if (ok)
        {
            _failures = 0;
            _secret.Clear();
            _recovery.Clear();
            Unlocked?.Invoke(recovery);
            return;
        }
        _failures++;
        if (_failures >= 5) _waitUntil = DateTime.Now.AddSeconds(Math.Min(300, 15 * (_failures - 4)));
        ShowError((recovery ? "That recovery key doesn't match." : "That's not right. Try again.") + (_failures >= 5 ? " Please wait a moment before trying again." : ""));
        _secret.Clear();
        if (!recovery) _secret.Focus();
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}

/// <summary>Shown before anything loads when protection is on.</summary>
public sealed class UnlockWindow : Window
{
    public UnlockWindow()
    {
        Title = "Guardrail EHS";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/guardrail.ico"));
        Width = 560;
        Height = 620;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13.5;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Brush.Bg");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        var panel = new LockPanel();
        panel.Unlocked += recovered =>
        {
            if (recovered)
            {
                MessageDialog.Inform("Choose a new PIN or password",
                    "You opened Guardrail with your recovery key. Next, choose a new PIN or password. Your recovery key keeps working.");
                new SetLockDialog(change: true) { Owner = this }.ShowDialog();
            }
            DialogResult = true;
        };
        Content = panel;
        Loaded += (_, _) => panel.Reset();
    }
}
