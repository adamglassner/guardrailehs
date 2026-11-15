using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Settings → Locations: the corporate → region → district → location list every Location field offers.</summary>
public sealed class LocationsView : UserControl
{
    private static readonly double[] Columns = [0, 150, 190, 150, 140, 90, 80, 100]; // 0 = star
    private readonly StackPanel _rows = new();
    private readonly Border _importBar = new();
    private readonly TextBlock _importText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = new() { FontSize = 11.5, Margin = new Thickness(2, 8, 0, 0) };
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 44) };

    public LocationsView()
    {
        var page = new StackPanel();

        // ----- Header -----
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var back = IconButton("", "Settings", "GhostButton");
        back.Margin = new Thickness(0, 0, 8, 0);
        back.Click += (_, _) => MainWindow.Instance?.ShowSettings();
        var add = IconButton("", "Add location", "PrimaryButton");
        add.Click += (_, _) => Editors.EditLocation(null);
        buttons.Children.Add(back);
        buttons.Children.Add(add);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);

        var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 14, 0) };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var glyph = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        badge.Child = glyph;
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Locations", Style = Res<Style>("PageTitle") });
        titles.Children.Add(new TextBlock
        {
            Style = Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "Your plants, offices, terminals and job sites, grouped by region and district. Every Location field in Guardrail offers this list, and the Dashboard can filter by it.",
        });
        header.Children.Add(titles);
        page.Children.Add(header);

        // ----- "Names already in use" import bar -----
        _importBar.CornerRadius = new CornerRadius(12);
        _importBar.Padding = new Thickness(16, 12, 16, 12);
        _importBar.Margin = new Thickness(0, 0, 0, 14);
        _importBar.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var importRow = new DockPanel();
        var importButton = IconButton("", "Add them", "PrimaryButton");
        importButton.Margin = new Thickness(12, 0, 0, 0);
        importButton.Click += (_, _) => ImportNamesInUse();
        DockPanel.SetDock(importButton, Dock.Right);
        importRow.Children.Add(importButton);
        importRow.Children.Add(_importText);
        _importBar.Child = importRow;
        page.Children.Add(_importBar);

        // ----- Table -----
        var card = new Border { Style = Res<Style>("Card"), Padding = new Thickness(0), ClipToBounds = true };
        var table = new StackPanel();
        var head = new Border { Padding = new Thickness(16, 10, 16, 10), CornerRadius = new CornerRadius(14, 14, 0, 0) };
        head.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        head.Child = Row(["LOCATION", "TYPE", "REGION › DISTRICT", "CITY", "MANAGER", "PEOPLE", "RECORDS", "STATUS"], header: true);
        table.Children.Add(head);
        table.Children.Add(_rows);

        var emptyIcon = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center };
        emptyIcon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _empty.Children.Add(emptyIcon);
        _empty.Children.Add(new TextBlock { Text = "No locations yet", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        _empty.Children.Add(new TextBlock { Text = "Add your sites so every form can pick from the same list.", Style = Res<Style>("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
        table.Children.Add(_empty);
        card.Child = table;
        page.Children.Add(card);
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        page.Children.Add(_count);
        Content = page;

        DataStore.Locations.Changed += OnChanged;
        Unloaded += (_, _) => DataStore.Locations.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _rows.Children.Clear();
        var all = DataStore.Locations.Items
            .OrderBy(l => !l.IsActive).ThenBy(l => l.Region).ThenBy(l => l.District).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var l in all)
        {
            var button = new Button { Style = Res<Style>("TableRow"), ToolTip = "Click to edit", DataContext = l };
            button.Content = Row([l.Name + (l.Code.Length > 0 ? $"  ·  {l.Code}" : ""), l.Type, l.Hierarchy.Length > 0 ? l.Hierarchy : "—",
                l.CityState.Length > 0 ? l.CityState : "—", l.Manager.Length > 0 ? l.Manager : "—", l.EmployeesText, l.UsageCount.ToString(), l.DisplayStatus]);
            button.Click += (_, _) => Editors.EditLocation(l);
            _rows.Children.Add(button);
        }
        _empty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int active = all.Count(l => l.IsActive);
        _count.Text = all.Count == 0 ? "" : $"{all.Count} location{(all.Count == 1 ? "" : "s")} · {active} active · click a row to edit or rename";

        var unlisted = LocationNames.UnlistedNamesInUse();
        _importBar.Visibility = unlisted.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _importText.Text = unlisted.Length == 0 ? "" :
            $"Your records already mention {unlisted.Length} location{(unlisted.Length == 1 ? "" : "s")} that aren't in this list: " +
            string.Join(", ", unlisted.Take(6)) + (unlisted.Length > 6 ? $" and {unlisted.Length - 6} more" : "") + ".";
    }

    private void ImportNamesInUse()
    {
        var names = LocationNames.UnlistedNamesInUse();
        foreach (var n in names) DataStore.Locations.Add(new Location { Name = n });
        Toaster.Show($"{names.Length} location{(names.Length == 1 ? "" : "s")} added. Click one to fill in its region, district and details.");
    }

    private Grid Row(string[] cells, bool header = false)
    {
        var grid = new Grid();
        for (int i = 0; i < Columns.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Columns[i] == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(Columns[i]) });
        for (int i = 0; i < cells.Length && i < Columns.Length; i++)
        {
            FrameworkElement cell;
            if (!header && i == Columns.Length - 1)
            {
                var pill = new Border { Style = Res<Style>("StatusPill"), Tag = cells[i] == Location.Active ? "Current" : "Denied" };
                pill.Child = new TextBlock { Text = cells[i] };
                cell = pill;
            }
            else
            {
                var t = new TextBlock { Text = cells[i], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                if (header) t.Style = Res<Style>("TableHeader");
                else if (i == 0) { t.FontWeight = FontWeights.SemiBold; t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent"); }
                cell = t;
            }
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private Button IconButton(string glyph, string text, string style)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, Style = Res<Style>("Icon"), Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBlock { Text = text });
        return new Button { Style = Res<Style>(style), Content = row };
    }

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);
}
