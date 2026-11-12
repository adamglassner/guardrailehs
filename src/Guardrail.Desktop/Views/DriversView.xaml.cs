using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class DriversView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public DriversView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Drivers.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Drivers.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Drivers.Items;
        string q = SearchBox.Text.Trim();
        var active = all.Where(d => d.IsActive).ToList();
        KpiActive.Text = active.Count.ToString();
        KpiQualified.Text = active.Count(d => d.DisplayStatus == FleetStatus.Qualified).ToString();
        KpiSoon.Text = active.Count(d => d.ComingUp.Count > 0).ToString();
        KpiProblems.Text = active.Count(d => d.DisplayStatus is Status.Expired or FleetStatus.Incomplete).ToString();

        var shown = all
            .Where(d => _filter switch
            {
                "Active" => d.IsActive,
                "Attention" => d.IsActive && d.DisplayStatus != FleetStatus.Qualified,
                "Applicants" => !d.IsActive,
                _ => true,
            })
            .Where(d => q.Length == 0 || Matches(d, q))
            .OrderBy(d => d.DisplayStatus switch { Status.Expired => 0, FleetStatus.Incomplete => 1, Status.ExpiringSoon => 2, FleetStatus.Qualified => 3, _ => 4 })
            .ThenBy(d => d.Name)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No drivers yet";
            EmptyText.Text = "Click \"Add driver\" to start a driver qualification file.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} drivers · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Driver r, string q) =>
        new[] { r.Number, r.Name, r.EmployeeId, r.Location, r.LicenseNumber, r.LicenseState, r.LicenseClass, r.Endorsements, r.ApplicationStatus, r.DisplayStatus, r.Notes }
            .Any(f => (f ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag }) return;
        _filter = tag;
        Refresh();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) Refresh();
    }

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditDriver(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Driver r }) Editors.EditDriver(r);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.OpenStoredFile(((Driver)ctx).StoredFile, "this driver");
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-drivers",
            ["Driver #", "Name", "Employee ID", "Location", "Application status", "License class", "License number", "License state", "Endorsements", "License expires", "Medical card expires", "Last MVR review", "MVR due", "Road test", "Hire date", "Status", "Problems"],
            (Rows.ItemsSource as IEnumerable<Driver> ?? Enumerable.Empty<Driver>()).Select(r => new[]
            {
                r.Number, r.Name, r.EmployeeId, r.Location, r.ApplicationStatus, r.LicenseClass, r.LicenseNumber, r.LicenseState, r.Endorsements, r.LicenseExpires?.ToString("yyyy-MM-dd") ?? "", r.MedicalCardExpires?.ToString("yyyy-MM-dd") ?? "", r.LastMvrReview?.ToString("yyyy-MM-dd") ?? "", r.MvrDue?.ToString("yyyy-MM-dd") ?? "", r.RoadTest, r.HireDate?.ToString("yyyy-MM-dd") ?? "", r.DisplayStatus, string.Join("; ", r.Problems),
            }));
}
