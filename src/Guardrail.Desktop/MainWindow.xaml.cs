using System.Windows;
using System.Windows.Threading;
using Guardrail.Models;
using Guardrail.Services;
using Guardrail.Views;

namespace Guardrail;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        ModuleBar.ItemsSource = ModuleCatalog.Toolbar;

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        App.ThemeChanged += OnThemeChanged;
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        UpdateThemeButton();

        Loaded += async (_, _) =>
        {
            if (App.StartPage == "settings") ShowSettings();
            else GoTo(App.StartPage ?? ModuleCatalog.Dashboard.Key);

            if (SelfTest.IsRunning)
            {
                int failed = await SelfTest.RunAsync(this);
                Application.Current.Shutdown(failed);
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    // ----- Navigation -----

    private bool _suppressShow;

    private void Module_Checked(object sender, RoutedEventArgs e)
    {
        if (!_suppressShow && sender is FrameworkElement { DataContext: ModuleInfo module })
            Show(module);
    }

    /// <summary>Highlights a toolbar module and shows a specific page for it (e.g. Actions pre-filtered to "Overdue").</summary>
    public void OpenModule(string key, object page)
    {
        var module = ModuleCatalog.Toolbar.FirstOrDefault(m => m.Key == key);
        if (module is not null)
        {
            _suppressShow = true;
            try { module.IsSelected = true; }
            finally { _suppressShow = false; }
        }
        SetPage(page);
    }

    /// <summary>Selects a toolbar module by key (also works for Fleet Hub section keys).</summary>
    public void GoTo(string key)
    {
        var module = ModuleCatalog.Toolbar.FirstOrDefault(m => m.Key == key);
        if (module is null)
        {
            // A Fleet Hub section: open Fleet Hub at that section.
            var fleet = ModuleCatalog.Toolbar.First(m => m.Key == "fleet");
            ClearToolbarSelection();
            fleet.IsSelected = true;
            SetPage(new FleetView(key));
            return;
        }

        if (module.IsSelected) Show(module);
        else module.IsSelected = true; // fires Module_Checked → Show
    }

    private void Show(ModuleInfo module)
    {
        SetPage(module.Key switch
        {
            "dashboard" => new DashboardView(),
            "actions" => new ActionsView(),
            "observations" => new ObservationsView(),
            "training" => new TrainingView(),
            "fleet" => new FleetView(),
            "dvir" => new FleetView("fleet-dvir"),
            "ai" => new AiReviewView(),
            _ => new ComingSoonView(module),
        });
    }

    private void SetPage(object page)
    {
        PageHost.Content = page;
        PageScroller.ScrollToTop();
    }

    private static void ClearToolbarSelection()
    {
        foreach (var m in ModuleCatalog.Toolbar) m.IsSelected = false;
    }

    // ----- Header buttons -----

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    public void ShowSettings()
    {
        ClearToolbarSelection();
        SetPage(new SettingsView());
    }

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        App.SetThemePreference(App.IsDark ? AppTheme.Light : AppTheme.Dark);

    private void Profile_Click(object sender, RoutedEventArgs e) =>
        Toaster.Show("User accounts and sign-in are coming soon.");

    // ----- Theme -----

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
        SystemTheme.ApplyTitleBar(this, App.IsDark);
    }

    private void UpdateThemeButton()
    {
        // Sun when dark (click for light), moon when light (click for dark).
        ThemeGlyph.Text = App.IsDark ? "" : "";
        ThemeButton.ToolTip = App.IsDark ? "Switch to light mode" : "Switch to dark mode";
    }

    // ----- Toast -----

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
