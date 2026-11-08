using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class ObservationsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public ObservationsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Observations.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Observations.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Observations.Items;
        var since = DateTime.Today.AddDays(-30);

        Kpi30.Text = all.Count(o => o.Date >= since).ToString();
        KpiOpen.Text = all.Count(o => o.Status == Status.Open).ToString();
        KpiGood.Text = all.Count(o => o.IsGoodCatch && o.Date >= since).ToString();
        KpiActions.Text = all.Count(o => o.LinkedAction.Length > 0).ToString();

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(o => _filter switch
            {
                "Open" => o.Status == Status.Open,
                "Hazards" => !o.IsGoodCatch,
                "Good" => o.IsGoodCatch,
                "Closed" => o.Status == Status.Closed,
                _ => true,
            })
            .Where(o => q.Length == 0 || Matches(o, q))
            .OrderByDescending(o => o.Date)
            .ThenByDescending(o => o.Seq)
            .ToList();

        // Rebinding (rather than editing in place) keeps the rows simple: records don't need change notification.
        Rows.ItemsSource = null;
        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No observations yet";
            EmptyText.Text = "Spotted a hazard or a good catch? Click \"New observation\".";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} observations · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Observation o, string q) =>
        new[] { o.Number, o.Type, o.Description, o.Location, o.ReportedBy, o.Status, o.LinkedAction }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditObservation(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Observation o }) Editors.EditObservation(o);
    }

    private void CreateAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the row editor
        if (sender is FrameworkElement { DataContext: Observation o }) Editors.CreateActionFrom(o);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-observations",
            ["Obs #", "Date", "Type", "Description", "Location", "Reported by", "Status", "Linked action"],
            (Rows.ItemsSource as IEnumerable<Observation> ?? Enumerable.Empty<Observation>()).Select(o => new[]
            {
                o.Number, o.Date.ToString("yyyy-MM-dd"), o.Type, o.Description, o.Location, o.ReportedBy, o.Status, o.LinkedAction,
            }));
}
