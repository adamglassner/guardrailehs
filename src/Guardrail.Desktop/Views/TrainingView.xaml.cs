using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class TrainingView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public TrainingView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Training.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Training.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Training.Items;

        KpiPeople.Text = all.Select(t => t.Employee.Trim().ToLowerInvariant()).Distinct().Count().ToString();
        KpiCurrent.Text = all.Count(t => t.DisplayStatus is Status.Current or Status.NoExpiry).ToString();
        KpiSoon.Text = all.Count(t => t.DisplayStatus == Status.ExpiringSoon).ToString();
        KpiExpired.Text = all.Count(t => t.DisplayStatus == Status.Expired).ToString();

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(t => _filter switch
            {
                "Current" => t.DisplayStatus is Status.Current or Status.NoExpiry,
                "Expiring soon" => t.DisplayStatus == Status.ExpiringSoon,
                "Expired" => t.DisplayStatus == Status.Expired,
                _ => true,
            })
            .Where(t => q.Length == 0 || Matches(t, q))
            // Most urgent first: expired, then expiring soon, then everything else by expiry date.
            .OrderBy(t => t.DisplayStatus switch { Status.Expired => 0, Status.ExpiringSoon => 1, _ => 2 })
            .ThenBy(t => t.ExpiresOn ?? DateTime.MaxValue)
            .ThenBy(t => t.Employee)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No training records yet";
            EmptyText.Text = "Click \"Record training\" to log a completed course.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} records · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(TrainingRecord t, string q) =>
        new[] { t.Number, t.Employee, t.Course, t.Trainer, t.Notes, t.DisplayStatus }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditTraining(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrainingRecord t }) Editors.EditTraining(t);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-training",
            ["Record #", "Employee", "Course", "Completed", "Expires", "Trainer", "Status", "Notes"],
            (Rows.ItemsSource as IEnumerable<TrainingRecord> ?? Enumerable.Empty<TrainingRecord>()).Select(t => new[]
            {
                t.Number, t.Employee, t.Course, t.CompletedOn.ToString("yyyy-MM-dd"),
                t.ExpiresOn?.ToString("yyyy-MM-dd") ?? "", t.Trainer, t.DisplayStatus, t.Notes,
            }));
}
