using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class RiskView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public RiskView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Risks.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Risks.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Risks.Items;
        string q = SearchBox.Text.Trim();
        static bool HighOrWorse(string level) => level is Status.High or Status.Extreme;
        KpiCount.Text = all.Count.ToString();
        KpiHighBefore.Text = all.Count(r => HighOrWorse(r.Level)).ToString();
        KpiHighAfter.Text = all.Count(r => HighOrWorse(r.ResidualLevel)).ToString();
        KpiReview.Text = all.Count(r => r.ReviewIsDue).ToString();
        DrawMatrix(all);

        var shown = all
            .Where(r => _filter switch
            {
                "High" => HighOrWorse(r.ResidualLevel),
                "Review" => r.ReviewIsDue,
                _ => true,
            })
            .Where(r => q.Length == 0 || Matches(r, q))
            .OrderByDescending(r => r.ResidualScore)
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Task)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No risk assessments yet";
            EmptyText.Text = "Click \"New assessment\" to rate a task's hazards before and after controls.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} assessments · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(RiskAssessment r, string q) =>
        new[] { r.Number, r.Task, r.Hazard, r.Location, r.Assessor, r.Controls, r.Level, r.ResidualLevel }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditRisk(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RiskAssessment r }) Editors.EditRisk(r);
    }

    /// <summary>5 × 5 grid: severity across, likelihood up, each square showing how many assessments land there after controls.</summary>
    private void DrawMatrix(IReadOnlyList<RiskAssessment> all)
    {
        Matrix.Children.Clear();
        Matrix.RowDefinitions.Clear();
        Matrix.ColumnDefinitions.Clear();
        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int c = 0; c < 5; c++) Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        for (int r = 0; r < 5; r++) Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int likelihood = 5; likelihood >= 1; likelihood--)
        {
            int row = 5 - likelihood;
            var label = new TextBlock { Text = $"L{likelihood}", FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetRow(label, row);
            Matrix.Children.Add(label);

            for (int severity = 1; severity <= 5; severity++)
            {
                int count = all.Count(a => a.ResidualLikelihood == likelihood && a.ResidualSeverity == severity);
                string level = RiskAssessment.LevelFor(likelihood * severity);
                var cell = new Border
                {
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Opacity = count == 0 ? 0.35 : 1,
                    ToolTip = $"Likelihood {likelihood} × severity {severity} = {likelihood * severity} ({level}): {count} assessment{(count == 1 ? "" : "s")}",
                    Child = new TextBlock
                    {
                        Text = count == 0 ? "" : count.ToString(),
                        Foreground = System.Windows.Media.Brushes.White,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                cell.SetResourceReference(Border.BackgroundProperty, level switch
                {
                    Status.Low => "Brush.Success",
                    Status.Medium => "Brush.Accent",
                    Status.High => "Brush.Warm",
                    _ => "Brush.Danger",
                });
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, severity);
                Matrix.Children.Add(cell);
            }
        }
        for (int severity = 1; severity <= 5; severity++)
        {
            var label = new TextBlock { Text = $"S{severity}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetRow(label, 5);
            Grid.SetColumn(label, severity);
            Matrix.Children.Add(label);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-risk-assessments",
            ["Risk #", "Date", "Task", "Hazard", "Location", "Assessor", "Likelihood", "Severity", "Score", "Level", "Controls", "Likelihood with controls", "Severity with controls", "Score with controls", "Level with controls", "Next review"],
            (Rows.ItemsSource as IEnumerable<RiskAssessment> ?? Enumerable.Empty<RiskAssessment>()).Select(r => new[]
            {
                r.Number, r.Date.ToString("yyyy-MM-dd"), r.Task, r.Hazard, r.Location, r.Assessor, r.Likelihood.ToString(), r.Severity.ToString(), r.Score.ToString(), r.Level, r.Controls, r.ResidualLikelihood.ToString(), r.ResidualSeverity.ToString(), r.ResidualScore.ToString(), r.ResidualLevel, r.ReviewDue?.ToString("yyyy-MM-dd") ?? "",
            }));
}
