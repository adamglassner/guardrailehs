using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class ClaimsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public ClaimsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Claims.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Claims.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Claims.Items;
        var open = all.Where(c => c.IsOpen).ToList();

        KpiOpen.Text = open.Count.ToString();
        KpiIncurred.Text = open.Sum(c => c.Incurred).ToString("C0");
        KpiClosedYear.Text = all.Count(c => !c.IsOpen && c.ClosedOn is { } d && d.Year == DateTime.Today.Year).ToString();
        KpiAvgDays.Text = open.Count == 0 ? "—" : Math.Round(open.Average(c => c.DaysOpen)).ToString("0");

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(c => _filter switch
            {
                "Open" => c.IsOpen,
                "Closed" => !c.IsOpen,
                "Workers' comp" or "Auto" => c.Type == _filter,
                _ => true,
            })
            .Where(c => q.Length == 0 || Matches(c, q))
            .OrderByDescending(c => c.IsOpen)
            .ThenByDescending(c => c.DateOfLoss)
            .ThenByDescending(c => c.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No claims yet";
            EmptyText.Text = "Click \"Add claim\" to start tracking a workers' comp, auto, liability or property claim.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} claims · incurred = paid + reserve on open claims · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Claim c, string q) =>
        new[] { c.Number, c.Type, c.Claimant, c.CarrierClaimNumber, c.Carrier, c.Adjuster, c.Location, c.RelatedIncident, c.Status, c.Notes }
            .Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase));

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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditClaim(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Claim c }) Editors.EditClaim(c);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-claims",
            ["Claim #", "Type", "Claimant", "Date of loss", "Location", "Related incident", "Carrier", "Carrier claim #", "Adjuster",
             "Reserve", "Paid", "Incurred", "Status", "Opened", "Closed", "Notes"],
            (Rows.ItemsSource as IEnumerable<Claim> ?? Enumerable.Empty<Claim>()).Select(c => new[]
            {
                c.Number, c.Type, c.Claimant, c.DateOfLoss.ToString("yyyy-MM-dd"), c.Location, c.RelatedIncident, c.Carrier,
                c.CarrierClaimNumber, c.Adjuster, c.Reserve.ToString("0.00"), c.Paid.ToString("0.00"), c.Incurred.ToString("0.00"),
                c.Status, c.CreatedAt.ToString("yyyy-MM-dd"), c.ClosedOn?.ToString("yyyy-MM-dd") ?? "", c.Notes,
            }));
}
