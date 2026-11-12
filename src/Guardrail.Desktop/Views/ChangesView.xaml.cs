using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class ChangesView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public ChangesView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Changes.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Changes.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Changes.Items;
        string q = SearchBox.Text.Trim();
        KpiOpen.Text = all.Count(c => c.IsOpen).ToString();
        KpiAwaiting.Text = all.Count(c => c.Stage is Status.Requested or Status.UnderReview).ToString();
        KpiTempExpired.Text = all.Count(c => c.TemporaryExpired).ToString();
        var since = DateTime.Today.AddDays(-90);
        KpiImplemented.Text = all.Count(c => c.Stage is Status.Implemented or Status.Closed && c.StageChangedOn is { } d && d >= since).ToString();

        var shown = all
            .Where(c => _filter switch
            {
                "Open" => c.IsOpen,
                "Awaiting" => c.Stage is Status.Requested or Status.UnderReview,
                "Temporary" => c.IsTemporary,
                "Closed" => !c.IsOpen,
                _ => true,
            })
            .Where(c => q.Length == 0 || Matches(c, q))
            .OrderByDescending(c => c.TemporaryExpired)
            .ThenByDescending(c => c.IsOpen)
            .ThenBy(c => c.TargetDate ?? DateTime.MaxValue)
            .ThenByDescending(c => c.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No changes requested";
            EmptyText.Text = "Click \"Request change\" before changing a process, equipment, material or procedure.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} changes · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(ChangeRequest r, string q) =>
        new[] { r.Number, r.Title, r.Type, r.Description, r.Reason, r.Location, r.Requester, r.Approver, r.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditChange(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChangeRequest r }) Editors.EditChange(r);
    }

    private void Advance_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.AdvanceChange((ChangeRequest)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-moc",
            ["MOC #", "Change", "Type", "Description", "Reason", "Location", "Requested by", "Approver", "Duration", "Temporary until", "Target date", "Risk assessment done", "Training needed", "Stage"],
            (Rows.ItemsSource as IEnumerable<ChangeRequest> ?? Enumerable.Empty<ChangeRequest>()).Select(r => new[]
            {
                r.Number, r.Title, r.Type, r.Description, r.Reason, r.Location, r.Requester, r.Approver, r.Duration, r.TemporaryUntil?.ToString("yyyy-MM-dd") ?? "", r.TargetDate?.ToString("yyyy-MM-dd") ?? "", r.RiskReviewed, r.TrainingNeeded, r.DisplayStatus,
            }));
}
