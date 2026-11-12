using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class VehiclesView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public VehiclesView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Vehicles.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Vehicles.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Vehicles.Items;
        string q = SearchBox.Text.Trim();
        KpiInService.Text = all.Count(v => v.ServiceStatus == FleetStatus.InService).ToString();
        KpiOut.Text = all.Count(v => v.ServiceStatus == FleetStatus.OutOfService).ToString();
        KpiSoon.Text = all.Count(v => v.DisplayStatus == Status.DueSoon).ToString();
        KpiOverdue.Text = all.Count(v => v.DisplayStatus is FleetStatus.RegistrationExpired or FleetStatus.InspectionOverdue or Status.NotInspected).ToString();

        var shown = all
            .Where(v => _filter switch
            {
                "InService" => v.ServiceStatus == FleetStatus.InService,
                "Attention" => v.ServiceStatus == FleetStatus.InService && v.DisplayStatus != FleetStatus.InService,
                "Out" => v.ServiceStatus == FleetStatus.OutOfService,
                "Sold" => v.ServiceStatus == FleetStatus.Sold,
                _ => true,
            })
            .Where(v => q.Length == 0 || Matches(v, q))
            .OrderBy(v => v.ServiceStatus == FleetStatus.Sold)
            .ThenBy(v => v.UnitNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No vehicles yet";
            EmptyText.Text = "Click \"Add vehicle\" for each truck, trailer, van or piece of equipment.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} vehicles · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Vehicle r, string q) =>
        new[] { r.Number, r.UnitNumber, r.Type, r.Year, r.Make, r.Model, r.Vin, r.Plate, r.AssignedDriver, r.Location, r.DisplayStatus, r.Notes }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditVehicle(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Vehicle r }) Editors.EditVehicle(r);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-vehicles",
            ["Unit", "Type", "Year", "Make", "Model", "VIN", "Plate", "Plate state", "Assigned driver", "Location", "Odometer", "Registration expires", "Last annual inspection", "Annual due", "Status", "Notes"],
            (Rows.ItemsSource as IEnumerable<Vehicle> ?? Enumerable.Empty<Vehicle>()).Select(r => new[]
            {
                r.UnitNumber, r.Type, r.Year, r.Make, r.Model, r.Vin, r.Plate, r.PlateState, r.AssignedDriver, r.Location, r.Odometer.ToString("0"), r.RegistrationExpires?.ToString("yyyy-MM-dd") ?? "", r.LastAnnualInspection?.ToString("yyyy-MM-dd") ?? "", r.AnnualDue?.ToString("yyyy-MM-dd") ?? "", r.DisplayStatus, r.Notes,
            }));
}
