using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class HosView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public HosView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Hos.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Hos.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Hos.Items;
        string q = SearchBox.Text.Trim();
        var recent = all.Where(h => h.Date >= DateTime.Today.AddDays(-30)).ToList();
        KpiReviewed.Text = recent.Count.ToString();
        KpiViolations.Text = recent.Count(h => h.HasViolation).ToString();
        KpiDrivers.Text = recent.Where(h => h.HasViolation).Select(h => h.Driver.Trim().ToLowerInvariant()).Distinct().Count().ToString();
        KpiRate.Text = recent.Count == 0 ? "—" : $"{100.0 * recent.Count(h => !h.HasViolation) / recent.Count:0}%";

        var shown = all
            .Where(h => _filter switch
            {
                "Violations" => h.HasViolation,
                "Compliant" => !h.HasViolation,
                _ => true,
            })
            .Where(h => q.Length == 0 || Matches(h, q))
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No logs reviewed yet";
            EmptyText.Text = "Click \"Review a log\" to record a driver day from the ELD.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} logs · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(HosLog r, string q) =>
        new[] { r.Number, r.Driver, r.Violation, r.ReviewedBy, r.Notes, r.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditHos(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HosLog r }) Editors.EditHos(r);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-hours-of-service",
            ["HOS #", "Date", "Driver", "Driving hours", "On-duty hours", "Violation", "Reviewed by", "Status", "Notes"],
            (Rows.ItemsSource as IEnumerable<HosLog> ?? Enumerable.Empty<HosLog>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.Driver, r.DrivingText, r.OnDutyText, r.Violation, r.ReviewedBy, r.DisplayStatus, r.Notes,
            }));
}
