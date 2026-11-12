using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class IncidentsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public IncidentsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Incidents.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Incidents.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Incidents.Items;
        string q = SearchBox.Text.Trim();
        int year = DateTime.Today.Year;
        KpiOpen.Text = all.Count(i => i.Status != Status.Closed).ToString();
        KpiRecordable.Text = all.Count(i => i.IsRecordable && i.Date.Year == year).ToString();
        KpiNearMiss.Text = all.Count(i => i.Type == Incident.NearMiss && i.Date.Year == year).ToString();
        var lastRecordable = all.Where(i => i.IsRecordable).Select(i => (DateTime?)i.Date).Max();
        KpiDaysSince.Text = lastRecordable is { } last ? Math.Max(0, (DateTime.Today - last.Date).Days).ToString() : "—";

        var shown = all
            .Where(i => _filter switch
            {
                "Open" => i.Status != Status.Closed,
                "Recordable" => i.IsRecordable,
                "Injury" => i.IsInjuryIllness,
                "Vehicle" => i.IsVehicle,
                "NearMiss" => i.Type == Incident.NearMiss,
                _ => true,
            })
            .Where(i => q.Length == 0 || Matches(i, q))
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No incidents reported";
            EmptyText.Text = "Click \"Report incident\" when something happens, including near misses.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} incidents · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Incident r, string q) =>
        new[] { r.Number, r.Type, r.Location, r.Description, r.PersonInvolved, r.JobTitle, r.ReportedBy, r.Classification, r.BodyPart, r.RootCause, r.Status, r.LinkedActions }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditIncident(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Incident r }) Editors.EditIncident(r);
    }

    private void CreateAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.CreateActionFrom((Incident)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-incidents",
            ["Incident #", "Date", "Time", "Type", "Location", "Description", "Person involved", "Job title", "Reported by", "OSHA classification", "Recordable", "Case type", "Body part", "Days away", "Days restricted", "DOT recordable", "Root cause", "Status", "Actions"],
            (Rows.ItemsSource as IEnumerable<Incident> ?? Enumerable.Empty<Incident>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.Time, r.Type, r.Location, r.Description, r.PersonInvolved, r.JobTitle, r.ReportedBy, r.Classification, r.IsRecordable ? "Yes" : "No", r.IsInjuryIllness ? r.CaseType : "", r.BodyPart, r.DaysAway.ToString(), r.DaysRestricted.ToString(), r.DotRecordable, r.RootCause, r.Status, r.LinkedActions,
            }));
}
