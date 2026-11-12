using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Live mock DOT audit built from the fleet records.</summary>
public partial class DotAuditView : UserControl
{
    private readonly List<AuditCheck> _checks;

    public DotAuditView()
    {
        InitializeComponent();

        _checks = FleetInsights.DotAudit();
        var readiness = FleetInsights.Readiness(_checks);
        KpiReadiness.Text = readiness is { } r ? $"{r}%" : "—";
        KpiGaps.Text = _checks.Sum(c => c.Gaps.Count).ToString();
        KpiDrivers.Text = DataStore.Drivers.Items.Count(d => d.IsActive).ToString();
        KpiVehicles.Text = DataStore.Vehicles.Items.Count(v => v.ServiceStatus == FleetStatus.InService).ToString();
        Rows.ItemsSource = _checks;
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AuditCheck check }) MainWindow.Instance?.GoTo(check.SectionKey);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-dot-audit",
            ["Requirement", "Regulation", "Records checked", "Status", "Record", "Problem"],
            _checks.SelectMany(c => c.Gaps.Count == 0
                ? new[] { new[] { c.Requirement, c.Rule, c.Checked.ToString(), c.Status, "", "" } }
                : c.Gaps.Select(g => new[] { c.Requirement, c.Rule, c.Checked.ToString(), c.Status, g.Record, g.Problem })));
}
