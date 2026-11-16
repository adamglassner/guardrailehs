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
        UpdateServer();
        Loaded += (_, _) => ServerSync.Changed += UpdateServer;
        Unloaded += (_, _) => ServerSync.Changed -= UpdateServer;
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
            : "Encrypt your records and documents on this PC and ask for a PIN or password to open Guardrail. Good for shared office or shop PCs. Off by default. Works alongside Guardrail Server: the server never sees your PIN or password.";

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

    // ----- Guardrail Server -----

    private void UpdateServer()
    {
        var c = ServerSync.Connection;
        ServerDetails.Children.Clear();
        ServerButtons.Children.Clear();
        Button Make(string glyph, string text, string style, Func<Task> click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = text });
            var b = new Button { Style = (Style)FindResource(style), Content = row, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += async (_, _) => { b.IsEnabled = false; try { await click(); } finally { b.IsEnabled = true; } };
            ServerButtons.Children.Add(b);
            return b;
        }

        if (c == null)
        {
            ServerPill.Tag = "Denied";
            ServerPillText.Text = "LOCAL ONLY";
            ServerText.Text = "Guardrail is saving everything on this PC. To share records with your team, connect to a Guardrail Server: your own PC, NAS or in-house server, a Docker container, or a cluster. Records stay on this PC too, so Guardrail keeps working offline.";
            ServerDetails.Visibility = Visibility.Collapsed;
            Make("\uE968", "Connect…", "PrimaryButton", () => { if (new ConnectServerDialog().ShowDialog() == true) Toaster.Show("Connected to Guardrail Server. Syncing…"); return Task.CompletedTask; });
            Make("\uE896", "Get Guardrail Server", "GhostButton", () => { Open("https://github.com/adamglassner/guardrailehs/tree/main/server#install"); return Task.CompletedTask; });
            return;
        }

        bool problem = ServerSync.LastError != null;
        ServerPill.Tag = problem ? (ServerSync.Offline ? "Pending review" : "Overdue") : "Current";
        ServerPillText.Text = ServerSync.IsSyncing ? "SYNCING" : problem ? (ServerSync.Offline ? "OFFLINE" : "PROBLEM") : "CONNECTED";
        ServerText.Text = problem
            ? ServerSync.LastError + (ServerSync.Offline ? " Changes are saved on this PC and sent when the server is back." : "")
            : $"Signed in to {c.ServerName} as {c.DisplayName}. Changes are sent within seconds and everyone else's arrive every minute.";
        ServerDetails.Visibility = Visibility.Visible;
        void Detail(string label, string value)
        {
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var l = new TextBlock { Text = label, Width = 150, Style = (Style)FindResource("Muted") };
            DockPanel.SetDock(l, Dock.Left);
            d.Children.Add(l);
            d.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            ServerDetails.Children.Add(d);
        }
        Detail("Server", c.Url);
        Detail("Organization", c.OrgName);
        Detail("Account", $"{c.Username} · {Guardrail.Sync.Protocol.Roles.Label(c.Role)}");
        Detail("Last sync", c.LastSync is DateTime t ? t.ToString("MMM d, h:mm tt") : "Not yet");
        int pending = ServerSync.Pending;
        Detail("Waiting to send", pending == 0 ? "Nothing" : $"{pending:N0} change{(pending == 1 ? "" : "s")}");

        Make("\uE895", "Sync now", "PrimaryButton", async () =>
        {
            string? err = await ServerSync.SyncNowAsync(quiet: false);
            Toaster.Show(err ?? "Synced with Guardrail Server.");
        });
        Make("\uE774", "Open web panel", "GhostButton", () => { Open(c.Url); return Task.CompletedTask; });
        if (ServerSync.LastError?.Contains("signed out") == true)
            Make("\uE968", "Connect again…", "GhostButton", () => { new ConnectServerDialog().ShowDialog(); return Task.CompletedTask; });
        var off = Make("\uE8D8", "Disconnect…", "GhostButton", async () =>
        {
            if (!MessageDialog.Confirm("Disconnect from Guardrail Server?",
                    "Guardrail goes back to saving only on this PC. Everything already here stays. You can connect again any time." +
                    (ServerSync.Pending > 0 ? $"\n\n{ServerSync.Pending:N0} change(s) haven't been sent yet and won't be." : ""),
                    "Disconnect", "Cancel", danger: true)) return;
            await ServerSync.DisconnectAsync();
            Toaster.Show("Disconnected. Guardrail is saving on this PC only.");
        });
        off.SetResourceReference(ForegroundProperty, "Brush.Danger");
    }

    private void UsersRoles_Click(object sender, RoutedEventArgs e)
    {
        if (ServerSync.Connection is { } c)
        {
            Open(c.Url.TrimEnd('/') + "/#users");
            return;
        }
        MessageDialog.Inform("Users and roles come with Guardrail Server",
            "Accounts, roles (admin, manager, member, viewer) and invites are managed in Guardrail Server's web panel. Connect to a server under Guardrail Server above, or set one up first.");
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Toaster.Show("Couldn't open the browser: " + ex.Message); }
    }
}
