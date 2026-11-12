using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class AuditsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public AuditsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        if (DataStore.Templates.Items.Count == 0)
            foreach (var t in InspectionTemplate.Starters()) DataStore.Templates.Add(t);

        DataStore.Inspections.Changed += OnStoreChanged;
        DataStore.Templates.Changed += OnStoreChanged;
        Unloaded += (_, _) =>
        {
            DataStore.Inspections.Changed -= OnStoreChanged;
            DataStore.Templates.Changed -= OnStoreChanged;
        };
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Inspections.Items;
        string q = SearchBox.Text.Trim();
        var since = DateTime.Today.AddDays(-30);
        var recentDone = all.Where(a => a.IsComplete && a.Date >= since).ToList();
        KpiDone.Text = recentDone.Count.ToString();
        KpiInProgress.Text = all.Count(a => !a.IsComplete).ToString();
        KpiFindings.Text = all.Where(a => a.IsComplete && !a.HasLinkedActions).Sum(a => a.Failed).ToString();
        var scores = recentDone.Where(a => a.Score.HasValue).Select(a => a.Score!.Value).ToList();
        KpiScore.Text = scores.Count == 0 ? "—" : $"{scores.Average() * 100:0}%";

        var shown = all
            .Where(a => _filter switch
            {
                "InProgress" => !a.IsComplete,
                "Findings" => a.IsComplete && a.Failed > 0,
                "Completed" => a.IsComplete,
                _ => true,
            })
            .Where(a => q.Length == 0 || Matches(a, q))
            .OrderBy(a => a.IsComplete)
            .ThenByDescending(a => a.Date)
            .ThenByDescending(a => a.Seq)
            .ToList();

        TemplateRows.ItemsSource = null;
        TemplateRows.ItemsSource = DataStore.Templates.Items.OrderBy(t => t.Name).ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No inspections yet";
            EmptyText.Text = "Click \"Start inspection\", pick a checklist and answer each item.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} inspections · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Inspection r, string q) =>
        new[] { r.Number, r.TemplateName, r.Location, r.Inspector, r.Notes, r.DisplayStatus, r.LinkedActions }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.StartInspection();

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Inspection r }) Editors.FillInspection(r);
    }

    private void CreateActions_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.CreateActionsFrom((Inspection)ctx);
    }

    private void NewTemplate_Click(object sender, RoutedEventArgs e) => Editors.EditTemplate(null);

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InspectionTemplate t }) Editors.EditTemplate(t);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-inspections",
            ["Inspection #", "Date", "Checklist", "Location", "Inspector", "Score", "Passed", "Failed", "Not answered", "Status", "Failed items", "Actions"],
            (Rows.ItemsSource as IEnumerable<Inspection> ?? Enumerable.Empty<Inspection>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.TemplateName, r.Location, r.Inspector, r.ScoreText, r.Passed.ToString(), r.Failed.ToString(), r.Unanswered.ToString(), r.DisplayStatus, string.Join("; ", r.Items.Where(i => i.Result == ChecklistItem.Fail).Select(i => i.Note.Length > 0 ? $"{i.Text} ({i.Note})" : i.Text)), r.LinkedActions,
            }));
}
