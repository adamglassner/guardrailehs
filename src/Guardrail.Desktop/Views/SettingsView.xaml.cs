using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class SettingsView : UserControl
{
    private readonly bool _ready;

    public SettingsView()
    {
        InitializeComponent();
        // Always the version this exe was built as (set from the .csproj by build.ps1 / CI).
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Guardrail EHS {version?.ToString(3) ?? ""} (preview)";

        (App.Settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;

        DataFolderBox.Text = DataStore.Folder;
        UpdateProtection();
        int locations = DataStore.Locations.Items.Count, active = DataStore.Locations.Items.Count(l => l.IsActive);
        LocationsSummary.Text = locations == 0 ? "None set up yet." : $"{locations} location{(locations == 1 ? "" : "s")}, {active} active.";
        _ready = true;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataStore.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toaster.Show($"Couldn't open the folder: {ex.Message}");
        }
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is RadioButton { Tag: string mode })
            App.SetThemePreference(mode);
    }

    // ----- PIN / password protection -----

    private void UpdateProtection()
    {
        bool on = DataLock.IsEnabled;
        DataText.Text = on
            ? "Your records and the files attached to them are saved on this PC, encrypted with your PIN or password. Dashcam and CCTV videos are kept as they are."
            : "Your records, and the files attached to Library documents, LOTO procedures, Safety Data Sheets and more, are saved on this PC. Back them up to keep them safe.";
        ProtectionPill.Tag = on ? "Current" : "Denied";
        ProtectionPillText.Text = on ? "ON" : "OFF";
        ProtectionText.Text = on
            ? $"Guardrail asks for your {(DataLock.IsPin ? "PIN" : "password")} when it opens and after it's been left alone. Lock it any time with Ctrl+L or Lock in the sidebar."
            : "Encrypt your records and documents on this PC and ask for a PIN or password to open Guardrail. Good for shared office or shop PCs. Off by default; when Guardrail Server arrives, signing in to it will work alongside this.";

        ProtectionButtons.Children.Clear();
        Button Make(string glyph, string text, string style, RoutedEventHandler click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = text });
            var b = new Button { Style = (Style)FindResource(style), Content = row, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += click;
            ProtectionButtons.Children.Add(b);
            return b;
        }
        if (!on)
        {
            Make("\uE72E", "Turn on PIN or password…", "PrimaryButton", TurnOn_Click);
            AutoLockRow.Visibility = Visibility.Collapsed;
            return;
        }
        Make("\uE8AC", "Change PIN or password…", "GhostButton", Change_Click);
        var off = Make("\uE785", "Turn off…", "GhostButton", TurnOff_Click);
        off.SetResourceReference(ForegroundProperty, "Brush.Danger");

        AutoLockRow.Visibility = Visibility.Visible;
        AutoLockChips.Children.Clear();
        int current = DataLock.Info?.AutoLockMinutes ?? 10;
        foreach (var (minutes, label) in SetLockDialog.AutoLockChoices)
        {
            var chip = new RadioButton { Content = label, GroupName = "AutoLock", IsChecked = minutes == current, Style = (Style)FindResource("ChipRadio") };
            chip.Checked += (_, _) => { if (_ready) DataLock.SetAutoLock(minutes); };
            AutoLockChips.Children.Add(chip);
        }
    }

    private void TurnOn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SetLockDialog(change: false);
        if (dialog.ShowDialog() != true || dialog.RecoveryKey is null) return;
        new RecoveryKeyDialog(dialog.RecoveryKey).ShowDialog();
        MainWindow.Instance?.UpdateLockButton();
        UpdateProtection();
        Toaster.Show("Protection is on. Your records are encrypted on this PC.");
    }

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        if (new VerifySecretDialog("Change PIN or password", "First, enter the one you use now.", "Continue").ShowDialog() != true) return;
        if (new SetLockDialog(change: true).ShowDialog() == true)
        {
            UpdateProtection();
            Toaster.Show("Changed. Use the new one next time Guardrail asks.");
        }
    }

    private async void TurnOff_Click(object sender, RoutedEventArgs e)
    {
        if (new VerifySecretDialog("Turn off protection", "Your records will be decrypted and saved as ordinary files on this PC. Enter your PIN or password to confirm.", "Turn off").ShowDialog() != true)
            return;
        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            await Task.Run(DataLock.Disable);
            Toaster.Show("Protection is off. Your records are no longer encrypted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageDialog.Inform("Couldn't turn it off", ex.Message, danger: true);
        }
        finally { Cursor = null; }
        MainWindow.Instance?.UpdateLockButton();
        UpdateProtection();
    }

    // ----- Backup / restore -----

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Back up Guardrail", FileName = $"Guardrail backup {DateTime.Now:yyyy-MM-dd}.zip", Filter = "Guardrail backup (*.zip)|*.zip",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            await Task.Run(() => Backup.Create(dlg.FileName));
            Toaster.Show(DataLock.IsEnabled
                ? "Backup saved. It's encrypted: you'll need your PIN, password or recovery key to restore it."
                : "Backup saved. Turn on PIN / password protection if you want backups encrypted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Inform("Couldn't back up", ex.Message, danger: true);
        }
        finally { Cursor = null; }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Restore a Guardrail backup", Filter = "Guardrail backup (*.zip)|*.zip" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!MessageDialog.Confirm("Restore this backup?",
                $"Everything in Guardrail will be replaced with {Path.GetFileName(dlg.FileName)}. Your current data is kept in a \"data-before-restore\" folder next to it, just in case. Guardrail restarts afterwards.",
                "Restore and restart", danger: true))
            return;
        try
        {
            Backup.Restore(dlg.FileName);
            Backup.Restart();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageDialog.Inform("Couldn't restore", ex.Message, danger: true);
        }
    }

    private void Locations_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.ShowLocations();

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);
}
