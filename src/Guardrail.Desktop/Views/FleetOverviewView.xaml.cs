using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Fleet Hub overview: KPIs and every overdue or upcoming item across the fleet records.</summary>
public partial class FleetOverviewView : UserControl
{
    public FleetOverviewView()
    {
        InitializeComponent();

        var alerts = FleetInsights.Alerts();
        KpiDrivers.Text = DataStore.Drivers.Items.Count(d => d.IsActive).ToString();
        KpiVehicles.Text = DataStore.Vehicles.Items.Count(v => v.ServiceStatus == FleetStatus.InService).ToString();
        KpiOverdue.Text = alerts.Count(a => a.Severity == Status.Overdue).ToString();
        KpiSoon.Text = alerts.Count(a => a.Severity == Status.DueSoon).ToString();

        Rows.ItemsSource = alerts;
        EmptyState.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool anyRecords = DataStore.Drivers.Items.Count + DataStore.Vehicles.Items.Count + DataStore.Dvirs.Items.Count
                          + DataStore.Maintenance.Items.Count + DataStore.Hos.Items.Count + DataStore.FleetAccidents.Items.Count > 0;
        EmptyTitle.Text = anyRecords ? "Everything is up to date" : "Your fleet is empty";
        EmptyText.Text = anyRecords ? "Nothing overdue and nothing due in the next 30 days."
                                    : "Start with Driver Qualification and Vehicle Management on the left.";
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FleetAlert alert }) MainWindow.Instance?.GoTo(alert.SectionKey);
    }
}
