using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class ActionsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public ActionsView(string filter = "All")
    {
        InitializeComponent();
        foreach (var chip in FilterChips.Children.OfType<RadioButton>())
            chip.IsChecked = (string)chip.Tag == filter;
        _filter = filter;
        _ready = true;

        DataStore.Actions.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Actions.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Actions.Items;
        var today = DateTime.Today;

        KpiOpen.Text = all.Count(a => !a.IsClosed).ToString();
        KpiOverdue.Text = all.Count(a => a.IsOverdue).ToString();
        KpiWeek.Text = all.Count(a => !a.IsClosed && a.DueDate is { } d && d.Date >= today && d.Date <= today.AddDays(7)).ToString();
        KpiClosed.Text = all.Count(a => a.IsClosed && a.ClosedAt is { } c && c >= today.AddDays(-30)).ToString();

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(a => _filter switch
            {
                "Open" => !a.IsClosed,
                "Overdue" => a.IsOverdue,
                "Closed" => a.IsClosed,
                _ => true,
            })
            .Where(a => q.Length == 0 || Matches(a, q))
            .OrderBy(a => a.IsClosed)
            .ThenBy(a => a.IsClosed ? DateTime.MaxValue - (a.ClosedAt ?? a.UpdatedAt) : TimeSpan.Zero)
            .ThenBy(a => a.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(a => a.Seq)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No actions yet";
            EmptyText.Text = "Click \"New action\" to add the first one, or create one from an observation.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} actions · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(ActionItem a, string q) =>
        new[] { a.Number, a.Title, a.Description, a.Owner, a.Location, a.Source, a.Priority, a.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditAction(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActionItem a }) Editors.EditAction(a);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-actions",
            ["Action #", "Title", "Details", "Owner", "Location", "Due", "Priority", "Status", "Source", "Created", "Closed"],
            (Rows.ItemsSource as IEnumerable<ActionItem> ?? Enumerable.Empty<ActionItem>()).Select(a => new[]
            {
                a.Number, a.Title, a.Description, a.Owner, a.Location,
                a.DueDate?.ToString("yyyy-MM-dd") ?? "", a.Priority, a.DisplayStatus, a.Source,
                a.CreatedAt.ToString("yyyy-MM-dd"), a.ClosedAt?.ToString("yyyy-MM-dd") ?? "",
            }));
}
