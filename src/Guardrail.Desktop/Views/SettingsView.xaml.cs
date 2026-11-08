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

        (App.Settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;

        DataFolderBox.Text = DataStore.Folder;
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

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);
}
