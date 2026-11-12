using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class MaintenanceView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public MaintenanceView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Maintenance.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Maintenance.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Maintenance.Items;
        string q = SearchBox.Text.Trim();
        var today = DateTime.Today;
        KpiSoon.Text = all.Count(w => w.DisplayStatus == Status.DueSoon).ToString();
        KpiOverdue.Text = all.Count(w => w.DisplayStatus == Status.Overdue).ToString();
        KpiDone.Text = all.Count(w => w.CompletedOn is { } d && d >= today.AddDays(-30)).ToString();
        KpiSpent.Text = all.Where(w => w.CompletedOn is { } d && d.Year == today.Year).Sum(w => w.Cost).ToString("C0");

        var shown = all
            .Where(w => _filter switch
            {
                "Open" => !w.IsComplete,
                "Overdue" => w.DisplayStatus == Status.Overdue,
                "Completed" => w.IsComplete,
                _ => true,
            })
            .Where(w => q.Length == 0 || Matches(w, q))
            .OrderBy(w => w.IsComplete)
            .ThenBy(w => w.IsComplete ? DateTime.MaxValue - (w.CompletedOn ?? w.UpdatedAt) : TimeSpan.Zero)
            .ThenBy(w => w.DueDate)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No maintenance scheduled";
            EmptyText.Text = "Click \"Schedule service\" to plan PM, repairs or the annual DOT inspection.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} work orders · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(MaintenanceOrder r, string q) =>
        new[] { r.Number, r.UnitNumber, r.Service, r.Description, r.Vendor, r.Notes, r.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditMaintenance(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MaintenanceOrder r }) Editors.EditMaintenance(r);
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.CompleteMaintenance((MaintenanceOrder)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-maintenance",
            ["WO #", "Unit", "Service", "Details", "Due", "Due odometer", "Repeats", "Shop", "Cost", "Completed", "Status", "Notes"],
            (Rows.ItemsSource as IEnumerable<MaintenanceOrder> ?? Enumerable.Empty<MaintenanceOrder>()).Select(r => new[]
            {
                r.Number, r.UnitNumber, r.Service, r.Description, r.DueDate.ToString("yyyy-MM-dd"), r.DueOdometer == 0 ? "" : r.DueOdometer.ToString("0"), r.Repeat, r.Vendor, r.Cost.ToString("0.00"), r.CompletedOn?.ToString("yyyy-MM-dd") ?? "", r.DisplayStatus, r.Notes,
            }));
}
