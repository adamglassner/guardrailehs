using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class BbsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public BbsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Bbs.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Bbs.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Bbs.Items;
        string q = SearchBox.Text.Trim();
        var since = DateTime.Today.AddDays(-30);
        var recent = all.Where(b => b.Date >= since).ToList();
        int safe = recent.Sum(b => b.SafeCount), risk = recent.Sum(b => b.AtRiskCount);
        KpiCount.Text = recent.Count.ToString();
        KpiSafe.Text = safe + risk == 0 ? "—" : $"{100.0 * safe / (safe + risk):0}%";
        KpiAtRisk.Text = risk.ToString();
        KpiFeedback.Text = recent.Count == 0 ? "—" : $"{100.0 * recent.Count(b => b.FeedbackGiven == "Yes") / recent.Count:0}%";

        var shown = all
            .Where(b => _filter switch
            {
                "AtRisk" => b.AtRiskCount > 0,
                "Safe" => b.AtRiskCount == 0,
                _ => true,
            })
            .Where(b => q.Length == 0 || Matches(b, q))
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No behavioral observations yet";
            EmptyText.Text = "Click \"New observation\" after watching a task and talking with the worker.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} observations · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(BbsObservation r, string q) =>
        new[] { r.Number, r.Observer, r.Area, r.Task, r.FocusArea, r.Comments, r.DisplayStatus, r.LinkedAction }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditBbs(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BbsObservation r }) Editors.EditBbs(r);
    }

    private void CreateAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.CreateActionFrom((BbsObservation)ctx);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-behavioral-observations",
            ["BBS #", "Date", "Observer", "Area", "Task", "Focus area", "Safe", "At-risk", "% safe", "Feedback given", "Comments", "Linked action"],
            (Rows.ItemsSource as IEnumerable<BbsObservation> ?? Enumerable.Empty<BbsObservation>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.Observer, r.Area, r.Task, r.FocusArea, r.SafeCount.ToString(), r.AtRiskCount.ToString(), r.PercentSafeText, r.FeedbackGiven, r.Comments, r.LinkedAction,
            }));
}
