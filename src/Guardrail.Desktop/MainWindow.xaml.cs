using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Guardrail.Data;
using Guardrail.Models;
using Guardrail.Services;
using Guardrail.Views;

namespace Guardrail;

public sealed record NavGroup(string Title, List<ModuleInfo> Items);

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.2) };
    private readonly DispatcherTimer _dataTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Rectangle? _dim;
    private int _dimCount;

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        NavGroups.ItemsSource = ModuleCatalog.NavGroups
            .Select(g => new NavGroup(g.Title, g.Keys.Select(k => ModuleCatalog.Toolbar.First(m => m.Key == k)).ToList()))
            .ToList();

        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };
        _dataTimer.Tick += (_, _) => UpdateDataStatus();

        App.ThemeChanged += OnThemeChanged;
        SetUpLock();
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(this, App.IsDark);
        UpdateThemeButton();
        UpdateDataStatus();

        Loaded += async (_, _) =>
        {
            _dataTimer.Start();
            if (!SelfTest.IsRunning) CrashReviewService.Start();
            if (!SelfTest.IsRunning) ServerSync.Start();
            if (App.StartPage == "settings") ShowSettings();
            else if (App.StartPage?.Split('/') is [var page, var tab])
            {
                // e.g. --page references/placards opens a tab inside a page
                GoTo(page);
                if (PageHost.Content is ReferencesView refs) refs.Select(tab);
            }
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
        _dataTimer.Stop();
        base.OnClosed(e);
    }

    // ----- Navigation -----

    private bool _suppressShow;

    private void Module_Checked(object sender, RoutedEventArgs e)
    {
        if (!_suppressShow && sender is FrameworkElement { DataContext: ModuleInfo module })
            Show(module);
    }

    /// <summary>Highlights a module in the sidebar and shows a specific page for it (e.g. Actions pre-filtered to "Overdue").</summary>
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

    /// <summary>Selects a module by key (also works for Fleet Hub section keys).</summary>
    public void GoTo(string key)
    {
        var module = ModuleCatalog.Toolbar.FirstOrDefault(m => m.Key == key);
        if (module is null)
        {
            // A Fleet Hub section: open Fleet Hub at that section.
            OpenModule("fleet", new FleetView(ModuleCatalog.FleetSections.Any(s => s.Key == key) ? key : "fleet-overview"));
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
            "regulatory" => new ComplianceView(),
            "claims" => new ClaimsView(),
            "library" => new LibraryView(),
            "monthly" => new MonthlyView(),
            "bbs" => new BbsView(),
            "permits" => new PermitsView(),
            "loto" => new LotoView(),
            "sds" => new SdsView(),
            "incidents" => new IncidentsView(),
            "audits" => new AuditsView(),
            "risk" => new RiskView(),
            "moc" => new ChangesView(),
            "reports" => new ReportsView(),
            "fleet" => new FleetView(),
            "ai" => new AiReviewView(),
            "references" => new ReferencesView(),
            _ => new ComingSoonView(module),
        });
    }

    private void SetPage(object page)
    {
        PageHost.Content = page;
        PageScroller.ScrollToTop();
        if (SelfTest.IsRunning) return;

        // A short fade-and-rise so moving between pages feels smooth.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private static void ClearSelection()
    {
        foreach (var m in ModuleCatalog.Toolbar) m.IsSelected = false;
    }

    // ----- Sidebar buttons -----

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    public void ShowSettings()
    {
        ClearSelection();
        SetPage(new SettingsView());
    }

    /// <summary>Settings → Locations.</summary>
    public void ShowLocations()
    {
        ClearSelection();
        SetPage(new LocationsView());
    }

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        App.SetThemePreference(App.IsDark ? AppTheme.Light : AppTheme.Dark);

    // ----- Theme -----

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
        SystemTheme.ApplyTitleBar(this, App.IsDark);
    }

    private void UpdateThemeButton()
    {
        ThemeGlyph.Text = App.IsDark ? "" : "";
        ThemeLabel.Text = App.IsDark ? "Light mode" : "Dark mode";
        ThemeButton.ToolTip = App.IsDark ? "Switch to light mode" : "Switch to dark mode";
    }

    // ----- Data status -----

    private static readonly FieldInfo[] StoreFields = typeof(DataStore)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(JsonStore<>))
        .ToArray();

    private void UpdateDataStatus()
    {
        int records = 0;
        foreach (var field in StoreFields)
            if (field.GetValue(null) is { } store && store.GetType().GetProperty("Items")?.GetValue(store) is System.Collections.ICollection items)
                records += items.Count;

        DataDetail.Text = records == 0
            ? "No records yet. Everything you add stays on this computer."
            : $"{records:N0} record{(records == 1 ? "" : "s")} · back up from Settings";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        StatusText.Text = $"Guardrail EHS {version?.ToString(3)} · {records:N0} records saved on this PC · syncing to a Guardrail Server is coming soon";
    }

    // ----- PIN / password lock -----

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTime _lastActivity = DateTime.Now;
    private Grid? _lockOverlay;
    private LockPanel? _lockPanel;
    private Button? _lockButton;

    private void SetUpLock()
    {
        // "Lock" in the sidebar, just above the theme toggle (only when protection is on).
        if (ThemeButton.Parent is Panel bottom)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "\uE72E", Style = (Style)FindResource("Icon"), FontSize = 15, Width = 28 });
            row.Children.Add(new TextBlock { Text = "Lock", FontSize = 13.5 });
            _lockButton = new Button { Style = (Style)FindResource("SidebarButton"), Content = row, ToolTip = "Lock Guardrail now (Ctrl+L)" };
            _lockButton.Click += (_, _) => LockNow();
            bottom.Children.Insert(bottom.Children.IndexOf(ThemeButton), _lockButton);
        }
        UpdateLockButton();

        System.Windows.Input.InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is System.Windows.Input.KeyboardEventArgs or System.Windows.Input.MouseButtonEventArgs or System.Windows.Input.MouseWheelEventArgs)
                _lastActivity = DateTime.Now;
        };
        _idleTimer.Tick += (_, _) =>
        {
            if (!DataLock.IsEnabled || SelfTest.IsRunning || IsLocked) return;
            int minutes = DataLock.Info?.AutoLockMinutes ?? 0;
            if (minutes > 0 && DateTime.Now - _lastActivity >= TimeSpan.FromMinutes(minutes)) LockNow();
        };
        _idleTimer.Start();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.L && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && DataLock.IsEnabled)
            {
                e.Handled = true;
                LockNow();
            }
        };
    }

    /// <summary>Shows or hides the sidebar Lock button (after protection is turned on or off).</summary>
    public void UpdateLockButton()
    {
        if (_lockButton != null) _lockButton.Visibility = DataLock.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsLocked => _lockOverlay?.Visibility == Visibility.Visible;

    /// <summary>
    /// Covers the window until the PIN / password is entered. The records on disk stay encrypted either way;
    /// work already running in the background (like Crash Review AI) carries on.
    /// </summary>
    public void LockNow()
    {
        if (!DataLock.IsEnabled || IsLocked) return;
        foreach (Window w in OwnedWindows) w.Close(); // open dialogs would show data on top of the lock
        if (_lockOverlay == null)
        {
            _lockPanel = new LockPanel();
            _lockOverlay = new Grid();
            _lockOverlay.SetResourceReference(Panel.BackgroundProperty, "Brush.Bg");
            _lockOverlay.Children.Add(_lockPanel);
            Grid.SetColumnSpan(_lockOverlay, 2);
            Panel.SetZIndex(_lockOverlay, 1000);
            RootGrid.Children.Add(_lockOverlay);
            _lockPanel.Unlocked += recovered =>
            {
                _lockOverlay.Visibility = Visibility.Collapsed;
                _lastActivity = DateTime.Now;
                if (recovered)
                {
                    MessageDialog.Inform("Choose a new PIN or password",
                        "You unlocked Guardrail with your recovery key. Next, choose a new PIN or password. Your recovery key keeps working.");
                    new SetLockDialog(change: true).ShowDialog();
                }
            };
        }
        _lockOverlay.Visibility = Visibility.Visible;
        _lockPanel!.Reset();
        FileVault.CleanUp();
    }

    // ----- Dialog dimming -----

    /// <summary>Dims the window while a dialog is open (dialogs can stack, so this counts).</summary>
    public void SetDimmed(bool on)
    {
        _dimCount = Math.Max(0, _dimCount + (on ? 1 : -1));
        if (_dim == null)
        {
            _dim = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(5, 10, 18)), Opacity = 0, IsHitTestVisible = false };
            Grid.SetColumnSpan(_dim, 2);
            RootGrid.Children.Add(_dim);
        }
        double target = _dimCount > 0 ? (App.IsDark ? 0.55 : 0.4) : 0;
        if (SelfTest.IsRunning) { _dim.BeginAnimation(OpacityProperty, null); _dim.Opacity = target; return; }
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(170)));
    }

    // ----- Toast -----

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        UpdateDataStatus();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Toast.Opacity < 0.05) Toast.Visibility = Visibility.Collapsed; };
        Toast.BeginAnimation(OpacityProperty, fade);
    }
}
