using System.Windows;
using System.Windows.Controls;
using Guardrail.Services;

namespace Guardrail.Views;

public sealed record ReportGroup(string Category, List<ReportDef> Reports);

/// <summary>Report library: pick a report, choose the year where it applies, preview it, export it to CSV.</summary>
public partial class ReportsView : UserControl
{
    private const int MaxPreviewRows = 300;
    private ReportDef? _report;
    private int _year = DateTime.Today.Year;
    private ReportTable? _table;

    /// <param name="category">Show only one category (Fleet Hub uses "Fleet"); null shows everything.</param>
    public ReportsView(string? category = null)
    {
        InitializeComponent();

        if (category != null)
        {
            // Inside Fleet Hub: no big page header, the section already has one.
            TitleRow.Visibility = Visibility.Collapsed;
        }

        var reports = ReportCatalog.All.Where(r => category == null || r.Category == category).ToList();
        ReportGroups.ItemsSource = reports.GroupBy(r => r.Category).Select(g => new ReportGroup(g.Key, g.ToList())).ToList();

        foreach (int year in new[] { DateTime.Today.Year, DateTime.Today.Year - 1, DateTime.Today.Year - 2 })
        {
            var chip = new RadioButton
            {
                Content = year.ToString(),
                Tag = year,
                GroupName = "ReportYear",
                Style = (Style)FindResource("ChipRadio"),
                IsChecked = year == _year,
            };
            chip.Checked += (_, _) => { _year = year; Run(); };
            YearChips.Children.Add(chip);
        }

        // Select the first report once the list has been generated.
        Loaded += (_, _) =>
        {
            if (_report != null) return;
            var first = FindRadio(ReportGroups);
            if (first != null) first.IsChecked = true;
            else if (reports.Count > 0) { _report = reports[0]; Run(); }
        };
    }

    private static RadioButton? FindRadio(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is RadioButton rb) return rb;
            if (FindRadio(child) is { } found) return found;
        }
        return null;
    }

    private void Report_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReportDef report })
        {
            _report = report;
            Run();
        }
    }

    private void Run()
    {
        if (_report is null) return;
        ReportTitle.Text = _report.Title;
        ReportDescription.Text = _report.Description;
        YearChips.Visibility = _report.UsesYear ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            _table = _report.Build(_year);
        }
        catch (Exception ex)
        {
            _table = null;
            SummaryText.Text = $"Couldn't build this report: {ex.Message}";
            Preview.Children.Clear();
            return;
        }
        SummaryText.Text = _table.Summary ?? "";
        DrawPreview(_table);
    }

    private void DrawPreview(ReportTable table)
    {
        Preview.Children.Clear();
        Preview.RowDefinitions.Clear();
        Preview.ColumnDefinitions.Clear();
        foreach (var _ in table.Headers) Preview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 70 });

        Preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Border();
        header.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        Grid.SetColumnSpan(header, Math.Max(1, table.Headers.Length));
        Preview.Children.Add(header);
        for (int c = 0; c < table.Headers.Length; c++)
        {
            var text = new TextBlock
            {
                Text = table.Headers[c].ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(14, 10, 10, 10),
                MaxWidth = 220,
                TextWrapping = TextWrapping.Wrap,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetColumn(text, c);
            Preview.Children.Add(text);
        }

        var rows = table.Rows.Take(MaxPreviewRows).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            Preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetRow(line, r + 1);
            Grid.SetColumnSpan(line, Math.Max(1, table.Headers.Length));
            Preview.Children.Add(line);

            for (int c = 0; c < rows[r].Length && c < table.Headers.Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = rows[r][c],
                    Margin = new Thickness(14, 8, 10, 8),
                    MaxWidth = 320,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                Preview.Children.Add(cell);
            }
        }

        PreviewNote.Text = table.Rows.Count == 0 ? "Nothing to show for this report yet."
            : table.Rows.Count > MaxPreviewRows ? $"Showing the first {MaxPreviewRows} of {table.Rows.Count} rows. Export to see them all."
            : $"{table.Rows.Count} row{(table.Rows.Count == 1 ? "" : "s")}.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null || _table is null) return;
        var name = "guardrail-" + _report.Key + (_report.UsesYear ? $"-{_year}" : "");
        CsvExporter.Export(name, _table.Headers, _table.Rows);
    }
}
