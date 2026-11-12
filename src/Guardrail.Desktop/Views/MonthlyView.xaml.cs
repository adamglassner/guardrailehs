using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class MonthlyView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public MonthlyView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Monthly.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Monthly.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Monthly.Items;
        string q = SearchBox.Text.Trim();
        var since = MonthlyEntry.FirstOfMonth(DateTime.Today).AddMonths(-11);
        var last12 = all.Where(m => m.Month >= since).ToList();
        KpiHours.Text = last12.Sum(m => m.HoursWorked).ToString("N0");
        // Average headcount per month across locations: sum employees per month, then average the months.
        var perMonth = last12.GroupBy(m => m.Month).Select(g => g.Sum(m => m.Employees)).ToList();
        KpiEmployees.Text = perMonth.Count == 0 ? "—" : perMonth.Average().ToString("N0");
        KpiMiles.Text = last12.Sum(m => m.MilesDriven).ToString("N0");
        KpiMonths.Text = all.Select(m => m.Month).Distinct().Count().ToString();

        int year = DateTime.Today.Year;
        var shown = all
            .Where(m => _filter switch
            {
                "ThisYear" => m.Month.Year == year,
                "LastYear" => m.Month.Year == year - 1,
                _ => true,
            })
            .Where(m => q.Length == 0 || Matches(m, q))
            .OrderByDescending(m => m.Month)
            .ThenBy(m => m.Location)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No months entered yet";
            EmptyText.Text = "Click \"Enter month\" and add hours worked for each month. OSHA incident rates are based on these numbers.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} months · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(MonthlyEntry r, string q) =>
        new[] { r.MonthText, r.Location, r.EnteredBy, r.Notes }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditMonthly(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MonthlyEntry r }) Editors.EditMonthly(r);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-monthly-data",
            ["Month", "Location", "Hours worked", "Employees", "Miles driven", "Entered by", "Notes"],
            (Rows.ItemsSource as IEnumerable<MonthlyEntry> ?? Enumerable.Empty<MonthlyEntry>()).Select(r => new[]
            {
                r.Month.ToString("yyyy-MM"), r.Location, r.HoursWorked.ToString("0.##"), r.Employees.ToString("0.##"), r.MilesDriven.ToString("0.##"), r.EnteredBy, r.Notes,
            }));
}
