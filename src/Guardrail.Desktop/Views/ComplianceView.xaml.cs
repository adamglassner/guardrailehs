using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class ComplianceView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public ComplianceView(string filter = "All")
    {
        InitializeComponent();
        foreach (var chip in FilterChips.Children.OfType<RadioButton>())
            chip.IsChecked = (string)chip.Tag == filter;
        _filter = filter;
        _ready = true;

        DataStore.Compliance.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Compliance.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Compliance.Items;

        KpiActive.Text = all.Count(c => !c.IsCompleted).ToString();
        KpiSoon.Text = all.Count(c => c.DisplayStatus == Status.DueSoon).ToString();
        KpiOverdue.Text = all.Count(c => c.DisplayStatus == Status.Overdue).ToString();
        KpiPermits.Text = all.Count(c => !c.IsCompleted && c.Type is "Permit" or "License").ToString();

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(c => _filter switch
            {
                "Due soon" => c.DisplayStatus == Status.DueSoon,
                "Overdue" => c.DisplayStatus == Status.Overdue,
                "Completed" => c.IsCompleted,
                _ => true,
            })
            .Where(c => q.Length == 0 || Matches(c, q))
            // Open items by due date (overdue first), completed ones last.
            .OrderBy(c => c.IsCompleted)
            .ThenBy(c => c.IsCompleted ? DateTime.MaxValue - (c.LastCompleted ?? c.UpdatedAt) : TimeSpan.Zero)
            .ThenBy(c => c.DueDate)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No requirements yet";
            EmptyText.Text = "Add your permits, licenses, reports and inspections to get reminders before they're due.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} requirements · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(ComplianceItem c, string q) =>
        new[] { c.Number, c.Title, c.Agency, c.Type, c.Reference, c.Location, c.Owner, c.Notes, c.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditCompliance(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComplianceItem c }) Editors.EditCompliance(c);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: ComplianceItem c }) Editors.MarkComplianceDone(c);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-compliance",
            ["Req #", "Requirement", "Agency", "Type", "Reference", "Location", "Owner", "Next due", "Repeats", "Last done", "Status", "Notes"],
            (Rows.ItemsSource as IEnumerable<ComplianceItem> ?? Enumerable.Empty<ComplianceItem>()).Select(c => new[]
            {
                c.Number, c.Title, c.Agency, c.Type, c.Reference, c.Location, c.Owner,
                c.IsCompleted ? "" : c.DueDate.ToString("yyyy-MM-dd"), c.Frequency,
                c.LastCompleted?.ToString("yyyy-MM-dd") ?? "", c.DisplayStatus, c.Notes,
            }));
}
