using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class PermitsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public PermitsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Permits.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Permits.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Permits.Items;
        string q = SearchBox.Text.Trim();
        var today = DateTime.Today;
        KpiActive.Text = all.Count(w => w.IsActive && !w.IsExpired).ToString();
        KpiToday.Text = all.Count(w => w.IsActive && w.ValidFrom.Date <= today && w.ValidUntil.Date >= today).ToString();
        KpiExpired.Text = all.Count(w => w.IsExpired).ToString();
        KpiMonth.Text = all.Count(w => w.CreatedAt >= today.AddDays(-30)).ToString();

        var shown = all
            .Where(w => _filter switch
            {
                "Active" => w.IsActive && !w.IsExpired,
                "Expired" => w.IsExpired,
                "Closed" => !w.IsActive,
                _ => true,
            })
            .Where(w => q.Length == 0 || Matches(w, q))
            .OrderByDescending(w => w.IsExpired)
            .ThenByDescending(w => w.IsActive)
            .ThenByDescending(w => w.ValidFrom)
            .ThenByDescending(w => w.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No work permits yet";
            EmptyText.Text = "Click \"Issue permit\" before hot work, confined space entry or other high-risk jobs.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} permits · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(WorkPermit r, string q) =>
        new[] { r.Number, r.Type, r.Work, r.Location, r.IssuedTo, r.IssuedBy, r.Precautions, r.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditPermit(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkPermit r }) Editors.EditPermit(r);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.ClosePermit((WorkPermit)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-work-permits",
            ["Permit #", "Type", "Work", "Location", "Issued to", "Issued by", "Valid from", "Valid until", "Status", "Closed on", "Precautions"],
            (Rows.ItemsSource as IEnumerable<WorkPermit> ?? Enumerable.Empty<WorkPermit>()).Select(r => new[]
            {
                r.Number, r.Type, r.Work, r.Location, r.IssuedTo, r.IssuedBy, r.ValidFrom.ToString("yyyy-MM-dd"), r.ValidUntil.ToString("yyyy-MM-dd"), r.DisplayStatus, r.ClosedOn?.ToString("yyyy-MM-dd") ?? "", r.Precautions,
            }));
}
