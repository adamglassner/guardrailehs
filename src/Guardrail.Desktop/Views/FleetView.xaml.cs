using System.Windows;
using System.Windows.Controls;
using Guardrail.Models;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Fleet Hub: side navigation over the Fleet sections, each shown as a "coming soon" page.</summary>
public partial class FleetView : UserControl
{
    public FleetView(string sectionKey = "fleet-overview")
    {
        InitializeComponent();

        var sections = ModuleCatalog.FleetSections;
        var start = sections.FirstOrDefault(s => s.Key == sectionKey) ?? sections[0];
        foreach (var s in sections) s.IsSelected = ReferenceEquals(s, start);

        SectionList.ItemsSource = sections;
        ShowSection(start);
    }

    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModuleInfo section })
            ShowSection(section);
    }

    private void ShowSection(ModuleInfo section)
    {
        if (SectionHost.Content is ComingSoonView { DataContext: ModuleInfo current } && ReferenceEquals(current, section))
            return;
        SectionHost.Content = section.Key switch
        {
            "fleet-overview" => new FleetOverviewView(),
            "fleet-dq" => new DriversView(),
            "fleet-accidents" => new AccidentsView(),
            "fleet-vehicles" => new VehiclesView(),
            "fleet-dvir" => new DvirView(),
            "fleet-maintenance" => new MaintenanceView(),
            "fleet-hos" => new HosView(),
            "fleet-dot" => new DotAuditView(),
            "fleet-reports" => new ReportsView(ReportCatalog.Fleet),
            _ => (object)new ComingSoonView(section),
        };
    }
}
