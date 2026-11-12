using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class DvirView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public DvirView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Dvirs.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Dvirs.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Dvirs.Items;
        string q = SearchBox.Text.Trim();
        var today = DateTime.Today;
        KpiWeek.Text = all.Count(r => r.Date >= today.AddDays(-7)).ToString();
        KpiReported.Text = all.Count(r => r.DefectsFound && r.Date >= today.AddDays(-30)).ToString();
        KpiOpen.Text = all.Count(r => r.DefectsOpen).ToString();
        KpiRepaired.Text = all.Count(r => r.RepairedOn is { } d && d >= today.AddDays(-30)).ToString();

        var shown = all
            .Where(r => _filter switch
            {
                "Open" => r.DefectsOpen,
                "Repaired" => r.DefectsFound && !r.DefectsOpen,
                "None" => !r.DefectsFound,
                _ => true,
            })
            .Where(r => q.Length == 0 || Matches(r, q))
            .OrderByDescending(r => r.DefectsOpen)
            .ThenByDescending(r => r.Date)
            .ThenByDescending(r => r.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No DVIRs yet";
            EmptyText.Text = "Click \"New DVIR\" after a pre-trip or post-trip inspection.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} DVIRs · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Dvir r, string q) =>
        new[] { r.Number, r.Driver, r.UnitNumber, r.TripType, r.Defects, r.RepairedBy, r.RepairNotes, r.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditDvir(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Dvir r }) Editors.EditDvir(r);
    }

    private void Certify_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.CertifyDvirRepair((Dvir)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-dvirs",
            ["DVIR #", "Date", "Trip", "Driver", "Unit", "Odometer", "Defects", "Safe to operate", "Status", "Repaired on", "Repaired by", "Repair notes"],
            (Rows.ItemsSource as IEnumerable<Dvir> ?? Enumerable.Empty<Dvir>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.TripType, r.Driver, r.UnitNumber, r.Odometer.ToString("0"), r.DefectsText, r.SafeToOperate, r.DisplayStatus, r.RepairedOn?.ToString("yyyy-MM-dd") ?? "", r.RepairedBy, r.RepairNotes,
            }));
}
