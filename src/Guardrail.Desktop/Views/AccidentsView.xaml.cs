using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class AccidentsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public AccidentsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.FleetAccidents.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.FleetAccidents.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.FleetAccidents.Items;
        string q = SearchBox.Text.Trim();
        int year = DateTime.Today.Year;
        var thisYear = all.Where(a => a.Date.Year == year).ToList();
        KpiYear.Text = thisYear.Count.ToString();
        KpiRecordable.Text = thisYear.Count(a => a.IsDotRecordable).ToString();
        KpiPreventable.Text = thisYear.Count(a => a.Preventability == FleetStatus.Preventable).ToString();
        KpiPending.Text = all.Count(a => a.Preventability == FleetStatus.PendingReview).ToString();

        var shown = all
            .Where(a => _filter switch
            {
                "Year" => a.Date.Year == year,
                "Recordable" => a.IsDotRecordable,
                "Pending" => a.Preventability == FleetStatus.PendingReview,
                "Preventable" => a.Preventability == FleetStatus.Preventable,
                _ => true,
            })
            .Where(a => q.Length == 0 || Matches(a, q))
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No accidents logged";
            EmptyText.Text = "Click \"Log accident\" for every vehicle accident. The register is required by 49 CFR 390.15.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} accidents · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(FleetAccident r, string q) =>
        new[] { r.Number, r.Driver, r.UnitNumber, r.Location, r.CityState, r.Description, r.PoliceReport, r.Preventability, r.RecordableText, r.Notes }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditFleetAccident(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FleetAccident r }) Editors.EditFleetAccident(r);
    }

    private void Video_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.OpenStoredFile(((FleetAccident)ctx).StoredFile, "this accident");
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-accident-register",
            ["Accident #", "Date", "Time", "Driver", "Unit", "Location", "City and state", "Description", "Injuries", "Fatalities", "Tow-away", "Hazmat released", "Citation", "Police report", "DOT recordable", "Preventability", "Notes"],
            (Rows.ItemsSource as IEnumerable<FleetAccident> ?? Enumerable.Empty<FleetAccident>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.Time, r.Driver, r.UnitNumber, r.Location, r.CityState, r.Description, r.Injuries.ToString(), r.Fatalities.ToString(), r.TowAway, r.Hazmat, r.Citation, r.PoliceReport, r.IsDotRecordable ? "Yes" : "No", r.Preventability, r.Notes,
            }));
}
