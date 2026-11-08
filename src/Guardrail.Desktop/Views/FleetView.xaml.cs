using System.Windows;
using System.Windows.Controls;
using Guardrail.Models;

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
        SectionHost.Content = new ComingSoonView(section);
    }
}
