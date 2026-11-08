using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guardrail.Data;
using Guardrail.Services;
using Path = System.Windows.Shapes.Path;

namespace Guardrail.Views;

public sealed record IncidentTypeRow(string Glyph, string Label, int Count);
public sealed record CountCard(int Count, string Label, string Filter);
public sealed record ChartSlice(string Label, int Value, Brush Brush);
public sealed record RateTile(string Value, string Label, string Help);
public sealed record LocationBar(string Location, int Value, Brush Brush);

/// <summary>
/// Dashboard preview. Every number here is SAMPLE data so the layout can be judged;
/// real figures will come from Guardrail Server once it exists.
/// </summary>
public partial class DashboardView : UserControl
{
    private static Brush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush DaysAway = Hex("#C8352B");
    private static readonly Brush FirstAid = Hex("#78B82A");
    private static readonly Brush Restricted = Hex("#E07B26");
    private static readonly Brush Fatality = Hex("#3A3A3A");
    private static readonly Brush Medical = Hex("#C9BE2B");

    public DashboardView()
    {
        InitializeComponent();

        IncidentTypes.ItemsSource = new[]
        {
            new IncidentTypeRow("", "INJURY / ILLNESS", 4),
            new IncidentTypeRow("", "ENVIRONMENTAL", 0),
            new IncidentTypeRow("", "PROPERTY DAMAGE OR LOSS", 3),
            new IncidentTypeRow("", "MOTORIZED VEHICLES", 2),
            new IncidentTypeRow("", "OPERATIONAL LIABILITIES", 1),
            new IncidentTypeRow("", "NEAR MISS", 7),
        };

        // Live counts from the Actions module.
        var actions = DataStore.Actions.Items;
        ActionCards.ItemsSource = new[]
        {
            new CountCard(actions.Count(a => !a.IsClosed), "Actions open", "Open"),
            new CountCard(actions.Count(a => a.IsClosed), "Actions closed", "Closed"),
            new CountCard(actions.Count(a => a.IsOverdue), "Actions overdue", "Overdue"),
        };

        var slices = new[]
        {
            new ChartSlice("Days away from work", 6, DaysAway),
            new ChartSlice("First aid", 3, FirstAid),
            new ChartSlice("Restricted work / job transfer", 2, Restricted),
            new ChartSlice("Fatality", 0, Fatality),
            new ChartSlice("Medical treatment beyond first aid", 1, Medical),
        };
        DonutLegend.ItemsSource = slices;
        DrawDonut(slices);

        DrawBars(new[]
        {
            new LocationBar("Main Plant", 1, DaysAway),
            new LocationBar("North Yard", 0, DaysAway),
            new LocationBar("Depot 3", 1, FirstAid),
            new LocationBar("East Terminal", 1, DaysAway),
            new LocationBar("Warehouse B", 0, DaysAway),
            new LocationBar("Riverside", 1, Restricted),
            new LocationBar("Site 12", 0, DaysAway),
            new LocationBar("HQ", 0, DaysAway),
            new LocationBar("South Shop", 1, Medical),
        });

        Rates.ItemsSource = new[]
        {
            new RateTile("4.12", "Recordable incident rate (TRIR)", "Recordable cases × 200,000 ÷ hours worked"),
            new RateTile("2.06", "DART rate", "Days away, restricted or transferred cases × 200,000 ÷ hours worked"),
            new RateTile("1.03", "Lost time incident rate", "Lost-time cases × 200,000 ÷ hours worked"),
            new RateTile("38.00", "Lost time days rate", "Days lost × 200,000 ÷ hours worked"),
            new RateTile("21.50", "Restricted days rate", "Restricted days × 200,000 ÷ hours worked"),
            new RateTile("0.85", "Accident frequency rate", "Vehicle accidents × 1,000,000 ÷ miles driven"),
        };
    }

    // ---------- Charts (simple hand-drawn shapes; a real chart library can replace these later) ----------

    private void DrawDonut(IReadOnlyList<ChartSlice> slices)
    {
        double size = Donut.Width, outer = size / 2, inner = outer * 0.55, cx = outer, cy = outer;
        double total = slices.Sum(s => s.Value);
        if (total <= 0) return;

        double angle = 0;
        foreach (var slice in slices.Where(s => s.Value > 0))
        {
            double sweep = slice.Value / total * 360.0;
            if (sweep >= 360) sweep = 359.99;
            Donut.Children.Add(new Path
            {
                Fill = slice.Brush,
                Data = Ring(cx, cy, outer, inner, angle, sweep),
                ToolTip = $"{slice.Label}: {slice.Value}",
            });
            angle += sweep;
        }
    }

    private static Geometry Ring(double cx, double cy, double r1, double r2, double start, double sweep)
    {
        static Point At(double cx, double cy, double r, double degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
        }

        bool large = sweep > 180;
        var figure = new PathFigure { StartPoint = At(cx, cy, r1, start), IsClosed = true };
        figure.Segments.Add(new ArcSegment(At(cx, cy, r1, start + sweep), new Size(r1, r1), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(cx, cy, r2, start + sweep), true));
        figure.Segments.Add(new ArcSegment(At(cx, cy, r2, start), new Size(r2, r2), 0, large, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private void DrawBars(IReadOnlyList<LocationBar> bars)
    {
        const double plotHeight = 190;
        double max = Math.Max(1, bars.Max(b => b.Value)) * 1.25;

        BarChart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plotHeight) });
        BarChart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Horizontal gridlines
        var lines = new Grid();
        for (int i = 0; i < 4; i++)
        {
            lines.RowDefinitions.Add(new RowDefinition());
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetRow(line, i);
            lines.Children.Add(line);
        }
        var baseline = new Border { BorderThickness = new Thickness(0, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };
        baseline.SetResourceReference(Border.BorderBrushProperty, "Brush.TextMuted");
        lines.Children.Add(baseline);
        Grid.SetColumnSpan(lines, bars.Count);
        BarChart.Children.Add(lines);

        for (int i = 0; i < bars.Count; i++)
        {
            BarChart.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = bars[i];

            var rect = new Border
            {
                Width = 12,
                Height = Math.Max(0, bar.Value / max * plotHeight),
                Background = bar.Brush,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = $"{bar.Location}: {bar.Value}",
            };
            Grid.SetColumn(rect, i);
            BarChart.Children.Add(rect);

            var label = new TextBlock
            {
                Text = bar.Location,
                FontSize = 10.5,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                LayoutTransform = new RotateTransform(-40),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetRow(label, 1);
            Grid.SetColumn(label, i);
            BarChart.Children.Add(label);
        }
    }

    // ---------- Coming soon ----------

    private void Soon_Click(object sender, RoutedEventArgs e) => Toaster.Soon(sender);

    private void ActionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountCard card })
            MainWindow.Instance?.OpenModule("actions", new ActionsView(card.Filter));
    }

    private void Filter_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Toaster.Show("Corporate / region / district / location filters are coming soon.");
    }
}
