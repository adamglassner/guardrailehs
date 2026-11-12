using System.Windows;
using System.Windows.Controls;
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
/// Live dashboard: everything comes from Incidents, Actions and Monthly Data,
/// filtered by period and location.
/// </summary>
public partial class DashboardView : UserControl
{
    private static Brush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush DaysAwayBrush = Hex("#C8352B");
    private static readonly Brush FirstAidBrush = Hex("#78B82A");
    private static readonly Brush RestrictedBrush = Hex("#E07B26");
    private static readonly Brush FatalityBrush = Hex("#3A3A3A");
    private static readonly Brush MedicalBrush = Hex("#C9BE2B");

    private string _period = "ThisYear";
    private string? _location;
    private bool _ready;

    public DashboardView()
    {
        InitializeComponent();
        ((RadioButton)PeriodChips.Children.OfType<RadioButton>().First()).IsChecked = true;
        BuildLocationChips();
        _ready = true;
        Refresh();
    }

    // ---------- filters ----------

    private void BuildLocationChips()
    {
        foreach (var old in LocationChips.Children.OfType<RadioButton>().ToList()) LocationChips.Children.Remove(old);

        var locations = DataStore.Incidents.Items.Select(i => i.Location)
            .Concat(DataStore.Monthly.Items.Select(m => m.Location))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(l => l)
            .Take(12)
            .ToList();

        void Add(string text, string? tag)
        {
            var chip = new RadioButton
            {
                Content = text,
                Tag = tag ?? "",
                GroupName = "DashLocation",
                Style = (Style)FindResource("ChipRadio"),
                IsChecked = string.Equals(tag, _location, StringComparison.OrdinalIgnoreCase),
            };
            chip.Checked += Location_Checked;
            LocationChips.Children.Add(chip);
        }
        Add("All locations", null);
        foreach (var l in locations) Add(l, l);
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _period = tag;
        if (_ready) Refresh();
    }

    private void Location_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _location = tag.Length == 0 ? null : tag;
        if (_ready) Refresh();
    }

    private (DateTime From, DateTime To, string Label) Period()
    {
        var today = DateTime.Today;
        return _period switch
        {
            "Last12" => (MonthlyEntry.FirstOfMonth(today).AddMonths(-11), today, "Last 12 months"),
            "LastYear" => (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31), $"{today.Year - 1}"),
            _ => (new DateTime(today.Year, 1, 1), today, $"{today.Year} year to date"),
        };
    }

    // ---------- data ----------

    private void Refresh()
    {
        var (from, to, label) = Period();
        var m = SafetyMetrics.Compute(from, to, _location);
        PeriodText.Text = $"{label}{(_location is null ? "" : " · " + _location)}";

        var allHere = DataStore.Incidents.Items.Where(i => SafetyMetrics.SameLocation(i.Location, _location)).ToList();

        // Days since the last recordable injury / last vehicle incident (all time, this location).
        var lastInjury = allHere.Where(i => i.IsRecordable).Select(i => (DateTime?)i.Date).Max();
        InjuryFreeDays.Text = lastInjury is { } li ? Math.Max(0, (DateTime.Today - li.Date).Days).ToString() : "—";
        LastInjuryText.Text = lastInjury is { } li2 ? $"Last recordable injury: {li2:MMM d, yyyy}" : "No recordable injuries on record";
        var lastVehicle = allHere.Where(i => i.IsVehicle).Select(i => (DateTime?)i.Date)
            .Concat(_location is null ? DataStore.FleetAccidents.Items.Select(a => (DateTime?)a.Date) : Enumerable.Empty<DateTime?>())
            .Max();
        VehicleFreeDays.Text = lastVehicle is { } lv ? Math.Max(0, (DateTime.Today - lv.Date).Days).ToString() : "—";
        LastVehicleText.Text = lastVehicle is { } lv2 ? $"Last vehicle incident: {lv2:MMM d, yyyy}" : "No vehicle incidents on record";

        // Incident counts by type for the period.
        int Count(string type) => m.Incidents.Count(i => i.Type == type);
        IncidentTypes.ItemsSource = new[]
        {
            new IncidentTypeRow("", "INJURY / ILLNESS", Count(Incident.InjuryIllness)),
            new IncidentTypeRow("", "ENVIRONMENTAL", Count("Environmental")),
            new IncidentTypeRow("", "PROPERTY DAMAGE", Count("Property damage")),
            new IncidentTypeRow("", "MOTOR VEHICLE", Count(Incident.MotorVehicle)),
            new IncidentTypeRow("", "OPERATIONAL LIABILITY", Count("Operational liability")),
            new IncidentTypeRow("", "NEAR MISS", Count(Incident.NearMiss)),
        };

        // Actions (all, not period-filtered: open work is open work).
        var actions = DataStore.Actions.Items;
        ActionCards.ItemsSource = new[]
        {
            new CountCard(actions.Count(a => !a.IsClosed), "Actions open", "Open"),
            new CountCard(actions.Count(a => a.IsClosed), "Actions closed", "Closed"),
            new CountCard(actions.Count(a => a.IsOverdue), "Actions overdue", "Overdue"),
        };

        // OSHA classification of injury / illness cases.
        var injuries = m.Incidents.Where(i => i.IsInjuryIllness).ToList();
        int Classified(string c) => injuries.Count(i => i.Classification == c);
        var slices = new[]
        {
            new ChartSlice("Days away from work", Classified(Incident.DaysAwayCase), DaysAwayBrush),
            new ChartSlice("Restricted work / job transfer", Classified(Incident.RestrictedTransfer), RestrictedBrush),
            new ChartSlice("Medical treatment beyond first aid", Classified(Incident.MedicalTreatment), MedicalBrush),
            new ChartSlice("First aid", Classified(Incident.FirstAid), FirstAidBrush),
            new ChartSlice("Fatality", Classified(Incident.Fatality), FatalityBrush),
        };
        DonutLegend.ItemsSource = slices;
        DrawDonut(slices);

        // Recordables by location.
        var bars = m.Incidents.Where(i => i.IsRecordable)
            .GroupBy(i => i.Location.Trim().Length == 0 ? "(no location)" : i.Location.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new LocationBar(g.Key, g.Count(), DaysAwayBrush))
            .OrderByDescending(b => b.Value)
            .Take(12)
            .ToList();
        DrawBars(bars);

        Rates.ItemsSource = new[]
        {
            new RateTile(SafetyMetrics.Format(m.Trir), "Recordable incident rate (TRIR)", $"{m.Recordables} recordable cases × 200,000 ÷ {m.Hours:N0} hours"),
            new RateTile(SafetyMetrics.Format(m.Dart), "DART rate", $"{m.DartCases} days away / restricted / transfer cases × 200,000 ÷ hours"),
            new RateTile(SafetyMetrics.Format(m.Ltir), "Lost time incident rate", $"{m.LostTimeCases} days-away cases × 200,000 ÷ hours"),
            new RateTile(SafetyMetrics.Format(m.LostDaysRate), "Lost time days rate", $"{m.DaysAway} days away × 200,000 ÷ hours"),
            new RateTile(SafetyMetrics.Format(m.RestrictedDaysRate), "Restricted days rate", $"{m.DaysRestricted} restricted days × 200,000 ÷ hours"),
            new RateTile(SafetyMetrics.Format(m.AccidentFrequencyRate), "Accident frequency rate", $"{m.VehicleAccidents} vehicle accidents × 1,000,000 ÷ {m.Miles:N0} miles"),
        };
        RatesNote.Text = m.Hours == 0
            ? "Rates need hours worked: enter them in Monthly Data. The accident frequency rate also needs miles driven."
            : $"Based on {m.Hours:N0} hours worked" + (m.Miles > 0 ? $" and {m.Miles:N0} miles driven" : "") + " from Monthly Data. Hover a tile for the math.";
    }

    // ---------- charts ----------

    private void DrawDonut(IReadOnlyList<ChartSlice> slices)
    {
        Donut.Children.Clear();
        double size = Donut.Width, outer = size / 2, inner = outer * 0.55, cx = outer, cy = outer;
        double total = slices.Sum(s => s.Value);
        DonutEmpty.Visibility = total > 0 ? Visibility.Collapsed : Visibility.Visible;
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
        BarChart.Children.Clear();
        BarChart.RowDefinitions.Clear();
        BarChart.ColumnDefinitions.Clear();
        BarEmpty.Visibility = bars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (bars.Count == 0) return;

        const double plotHeight = 190;
        double max = Math.Max(1, bars.Max(b => b.Value)) * 1.25;

        BarChart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plotHeight) });
        BarChart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
                Width = 14,
                Height = Math.Max(0, bar.Value / max * plotHeight),
                Background = bar.Brush,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = $"{bar.Location}: {bar.Value} recordable",
            };
            Grid.SetColumn(rect, i);
            BarChart.Children.Add(rect);

            var count = new TextBlock
            {
                Text = bar.Value.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, rect.Height + 2),
            };
            Grid.SetColumn(count, i);
            BarChart.Children.Add(count);

            var label = new TextBlock
            {
                Text = bar.Location,
                FontSize = 10.5,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                LayoutTransform = new RotateTransform(-40),
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetRow(label, 1);
            Grid.SetColumn(label, i);
            BarChart.Children.Add(label);
        }
    }

    // ---------- navigation ----------

    private void IncidentType_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Instance?.OpenModule("incidents", new IncidentsView());

    private void ActionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountCard card })
            MainWindow.Instance?.OpenModule("actions", new ActionsView(card.Filter));
    }
}
