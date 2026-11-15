using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>
/// Safety References: an offline Emergency Response Guidebook lookup, DOT hazmat placards, GHS pictograms,
/// NFPA 704, workplace safety basics, DOT/FMCSA and OSHA quick rules, and emergency phone numbers.
/// Everything is built in code and works with no internet connection.
/// </summary>
public sealed class ReferencesView : UserControl
{
    public const string ErgUrl = "https://www.phmsa.dot.gov/hazmat/erg/emergency-response-guidebook-erg";

    public static readonly (string Key, string Title, string Glyph)[] Tabs =
    [
        ("erg", "ERG lookup", ""),
        ("placards", "Hazmat placards", ""),
        ("ghs", "GHS & NFPA 704", ""),
        ("workplace", "Workplace safety", ""),
        ("fleet", "DOT & fleet rules", ""),
        ("osha", "OSHA & rates", ""),
        ("numbers", "Emergency numbers", ""),
    ];

    private static string _lastTab = "erg";
    private static string _lastQuery = "";

    private readonly ContentControl _host = new();
    private readonly Dictionary<string, RadioButton> _chips = [];

    // ERG tab
    private readonly TextBox _search = new() { FontSize = 15, Padding = new Thickness(38, 9, 12, 9) };
    private readonly TextBlock _searchHint = new() { Text = "UN/NA number or material name — e.g. 1203, propane, sulfuric acid", IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 0, 0), FontSize = 14 };
    private readonly StackPanel _results = new();
    private readonly TextBlock _resultCount = new() { FontSize = 11.5, Margin = new Thickness(2, 8, 0, 0) };
    private readonly ContentControl _detail = new();
    private FrameworkElement? _ergPage;

    public ReferencesView()
    {
        var page = new StackPanel();

        // ----- Header -----
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 14, 0) };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        var glyph = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        badge.Child = glyph;
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Safety References", Style = Res<Style>("PageTitle") });
        titles.Children.Add(new TextBlock
        {
            Style = Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0),
            Text = "The Emergency Response Guidebook, hazmat placards, GHS and NFPA labels, and the rules and phone numbers you reach for most — all built in, so they work offline.",
        });
        header.Children.Add(titles);
        page.Children.Add(header);

        // ----- Tabs -----
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (key, title, g) in Tabs)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = g, Style = Res<Style>("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            var chip = new RadioButton { Style = Res<Style>("ChipRadio"), Content = row, GroupName = "RefTabs", Tag = key };
            chip.Checked += (_, _) => ShowTab(key);
            _chips[key] = chip;
            chips.Children.Add(chip);
        }
        page.Children.Add(chips);
        page.Children.Add(_host);
        Content = page;

        _search.TextChanged += (_, _) => { _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RunSearch(); };
        _searchHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _resultCount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        Select(_chips.ContainsKey(_lastTab) ? _lastTab : "erg");
    }

    /// <summary>Opens a tab ("erg", "placards", …).</summary>
    public void Select(string key)
    {
        if (_chips.TryGetValue(key, out var chip))
        {
            if (chip.IsChecked == true) ShowTab(key);
            else chip.IsChecked = true;
        }
    }

    /// <summary>Opens the ERG tab with a search already typed in.</summary>
    public void Lookup(string query)
    {
        Select("erg");
        _search.Text = query;
    }

    private void ShowTab(string key)
    {
        _lastTab = key;
        _host.Content = key switch
        {
            "erg" => _ergPage ??= BuildErg(),
            "placards" => BuildPlacards(),
            "ghs" => BuildGhs(),
            "workplace" => BuildWorkplace(),
            "fleet" => BuildFleet(),
            "osha" => BuildOsha(),
            "topics" => BuildOshaTopics(),
            _ => BuildNumbers(),
        };
    }

    // =====================================================================
    // ERG lookup
    // =====================================================================

    private FrameworkElement BuildErg()
    {
        var page = new StackPanel();

        // Search card
        var card = new Border { Style = Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 14) };
        var stack = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var official = SmallButton("", "Official ERG page");
        official.Click += (_, _) => OpenUrl(ErgUrl);
        DockPanel.SetDock(official, Dock.Right);
        top.Children.Add(official);
        var t = new StackPanel();
        t.Children.Add(new TextBlock { Text = "Emergency Response Guidebook lookup", Style = Res<Style>("CardTitle") });
        t.Children.Add(new TextBlock { Text = $"{ErgData.Edition} · {ErgData.Materials.Count:N0} materials · {ErgData.Guides.Count(g => !g.Value.IsBlank)} orange guides · green-table isolation distances", Style = Res<Style>("Muted"), Margin = new Thickness(0, 3, 0, 0) });
        top.Children.Add(t);
        stack.Children.Add(top);

        var box = new Grid();
        box.Children.Add(_search);
        var icon = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 15, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        box.Children.Add(icon);
        box.Children.Add(_searchHint);
        stack.Children.Add(box);

        var quick = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        quick.Children.Add(new TextBlock { Text = "Try:", Style = Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        foreach (var (q, label) in new[] { ("1203", "1203 Gasoline"), ("1993", "1993 Diesel"), ("1075", "1075 Propane"), ("1005", "1005 Ammonia"), ("1017", "1017 Chlorine"), ("3480", "3480 Lithium ion"), ("2794", "2794 Batteries, wet") })
        {
            var b = new Button { Style = Res<Style>("SmallButton"), Content = label, Margin = new Thickness(0, 0, 6, 8) };
            b.Click += (_, _) => _search.Text = q;
            quick.Children.Add(b);
        }
        stack.Children.Add(quick);
        stack.Children.Add(_results);
        stack.Children.Add(_resultCount);
        card.Child = stack;
        page.Children.Add(card);
        page.Children.Add(_detail);

        _search.Text = _lastQuery;
        RunSearch();
        Dispatcher.BeginInvoke(() => _search.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return page;
    }

    private void RunSearch()
    {
        _lastQuery = _search.Text;
        _results.Children.Clear();
        var found = ErgData.Search(_search.Text, 400);
        if (_search.Text.Trim().Length == 0)
        {
            _resultCount.Text = "";
            _detail.Content = ErgHowTo();
            return;
        }
        if (found.Count == 0)
        {
            _resultCount.Text = "Nothing in the ERG matches that. Check the number on the placard, orange panel or shipping paper — or, if the material can't be identified, use GUIDE 111.";
            _detail.Content = GuideCard(null, ErgData.Guide("111")!);
            return;
        }

        var head = new Border { Padding = new Thickness(14, 8, 14, 8), CornerRadius = new CornerRadius(10, 10, 0, 0), Margin = new Thickness(0, 14, 0, 0) };
        head.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        head.Child = ResultRow("ID NO.", "GUIDE", "NAME OF MATERIAL", null, header: true);
        _results.Children.Add(head);

        var list = new StackPanel();
        foreach (var m in found.Take(60))
        {
            var row = new Button { Style = Res<Style>("TableRow"), Padding = new Thickness(14, 9, 14, 9), Content = ResultRow(m.IdText, m.Guide, m.Name, m) };
            row.Click += (_, _) => ShowMaterial(m);
            list.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _results.Children.Add(scroll);
        _resultCount.Text = found.Count > 60
            ? $"Showing 60 of {found.Count:N0} matches — type more of the name to narrow it down."
            : $"{found.Count} match{(found.Count == 1 ? "" : "es")}. Click one to open its guide.";

        // Open the guide straight away when every match points to the same guide (e.g. a number search).
        var guides = found.Select(m => m.GuideNumber).Distinct().ToList();
        if (guides.Count == 1) ShowMaterial(found[0]);
        else _detail.Content = null;
    }

    private Grid ResultRow(string id, string guide, string name, ErgMaterial? m, bool header = false)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cells = new[] { id, guide, name };
        for (int i = 0; i < 3; i++)
        {
            var tb = new TextBlock { Text = cells[i], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (header) tb.Style = Res<Style>("TableHeader");
            else if (i < 2) { tb.FontWeight = FontWeights.SemiBold; tb.FontFamily = new FontFamily("Consolas, Segoe UI"); }
            if (!header && i == 1) tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        if (m is not null)
        {
            var flags = new StackPanel { Orientation = Orientation.Horizontal };
            if (ErgData.IsHighlighted(m.Id)) flags.Children.Add(Badge("Isolation distances", Ergs.Green));
            if (ErgData.WaterReactiveFor(m.Id).Count > 0) flags.Children.Add(Badge("Water-reactive", Ergs.Blue));
            if (m.MayPolymerize) flags.Children.Add(Badge("P · may polymerize", Ergs.Orange));
            Grid.SetColumn(flags, 3);
            g.Children.Add(flags);
        }
        return g;
    }

    private void ShowMaterial(ErgMaterial m)
    {
        var guide = ErgData.Guide(m.Guide);
        if (guide is null) return;
        _detail.Content = GuideCard(m, guide);
    }

    /// <summary>A "how to use" card shown before anything is typed.</summary>
    private FrameworkElement ErgHowTo()
    {
        var grid = TwoColumns(
            Section("How to use the ERG", Numbered([
                "RESIST RUSHING IN. Approach from upwind, uphill or upstream, keep people away and stay clear of spills, vapors, fumes and smoke.",
                "Identify the material by the 4-digit ID number on the placard, orange panel or shipping paper, or by its name.",
                "Look it up here to get its 3-digit orange GUIDE. If the entry is highlighted (isolation distances), follow those distances when there's no fire.",
                "Can't identify it? Use the placard to pick a guide (see Hazmat placards) or use GUIDE 111.",
                "Call 911, then the emergency response number on the shipping paper (or CHEMTREC 1-800-424-9300).",
            ])),
            Section("General first aid (ERG)", Bullets(ErgData.GeneralFirstAid)));
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(SourceNote("From the 2024 Emergency Response Guidebook (U.S. DOT/PHMSA, Transport Canada and Mexico's SCT). It's for the first 30 minutes of a transportation incident — always follow your training, your emergency plan and the shipping papers."));
        return stack;
    }

    private FrameworkElement GuideCard(ErgMaterial? m, ErgGuide guide)
    {
        var stack = new StackPanel();

        // Title band in ERG orange
        var band = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 16, 20, 16), Background = new SolidColorBrush(Ergs.Orange), Margin = new Thickness(0, 0, 0, 14) };
        var bandRow = new DockPanel();
        var num = new Border { CornerRadius = new CornerRadius(12), Background = Brushes.Black, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var numStack = new StackPanel();
        numStack.Children.Add(new TextBlock { Text = "GUIDE", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        numStack.Children.Add(new TextBlock { Text = guide.Number, Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center });
        num.Child = numStack;
        DockPanel.SetDock(num, Dock.Left);
        bandRow.Children.Add(num);
        var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (m is not null)
            bt.Children.Add(new TextBlock { Text = (m.Id.Length > 0 ? $"UN/NA {m.Id} · " : "") + m.Name, Foreground = Brushes.Black, FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        bt.Children.Add(new TextBlock { Text = guide.Title.Length > 0 ? guide.Title : "Guide " + guide.Number, Foreground = Brushes.Black, FontSize = m is null ? 18 : 13.5, FontWeight = m is null ? FontWeights.Bold : FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Opacity = m is null ? 1 : 0.85 });
        if (m?.MayPolymerize == true)
            bt.Children.Add(new TextBlock { Text = "(P) — may polymerize violently when heated or contaminated.", Foreground = Brushes.Black, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        bandRow.Children.Add(bt);
        band.Child = bandRow;
        stack.Children.Add(band);

        // Green table (Table 1) and Table 2
        if (m is not null && m.Id.Length > 0)
        {
            var dist = ErgData.DistancesFor(m.Id);
            if (dist.Count > 0) stack.Children.Add(DistancesCard(dist));
            var water = ErgData.WaterReactiveFor(m.Id);
            if (water.Count > 0)
            {
                stack.Children.Add(Callout(Ergs.Blue, "", "Water-reactive — gives off toxic gas when spilled in water (Table 2)",
                    $"Gas produced: {string.Join(", ", water.Select(w => w.Gases).Distinct())}. Use the \"when spilled in water\" distances above."));
            }
        }

        if (guide.IsBlank)
        {
            stack.Children.Add(Section($"Guide {guide.Number}", Para("This guide number is intentionally left blank in the 2024 edition — no materials refer to it.")));
            return stack;
        }

        // Guide text: hazards & public safety on the left, emergency response on the right, like the book.
        int split = guide.Lines.FindIndex(l => l.Kind == 'H' && l.Text == "EMERGENCY RESPONSE");
        var left = split > 0 ? guide.Lines.Take(split).ToList() : guide.Lines;
        var right = split > 0 ? guide.Lines.Skip(split).ToList() : new List<(char Kind, string Text)>();
        stack.Children.Add(TwoColumns(GuideColumn(left), right.Count > 0 ? GuideColumn(right) : new Border()));

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var all = SmallButton("", $"All materials using guide {guide.Number}");
        all.Click += (_, _) => ShowGuideMaterials(guide);
        actions.Children.Add(all);
        if (guide.Number != "111")
        {
            var unknown = SmallButton("", "Guide 111 — unknown material");
            unknown.Margin = new Thickness(8, 0, 0, 0);
            unknown.Click += (_, _) => _detail.Content = GuideCard(null, ErgData.Guide("111")!);
            actions.Children.Add(unknown);
        }
        stack.Children.Add(actions);
        stack.Children.Add(SourceNote($"{ErgData.Edition}, Guide {guide.Number}. For initial response only — follow your training and call the number on the shipping paper for material-specific advice."));
        return stack;
    }

    private void ShowGuideMaterials(ErgGuide guide)
    {
        var list = ErgData.Materials.Where(x => x.GuideNumber == guide.Number).ToList();
        var stack = new StackPanel();
        foreach (var m in list.Take(300))
        {
            var row = new Button { Style = Res<Style>("TableRow"), Padding = new Thickness(14, 7, 14, 7), Content = ResultRow(m.IdText, m.Guide, m.Name, m) };
            row.Click += (_, _) => ShowMaterial(m);
            stack.Children.Add(row);
        }
        var back = SmallButton("", $"Back to guide {guide.Number}");
        back.Margin = new Thickness(0, 0, 0, 10);
        back.Click += (_, _) => _detail.Content = GuideCard(null, guide);
        var outer = new StackPanel();
        outer.Children.Add(back);
        outer.Children.Add(Section($"{list.Count} materials use guide {guide.Number}" + (list.Count > 300 ? " (first 300 shown)" : ""), stack));
        _detail.Content = outer;
    }

    private FrameworkElement DistancesCard(IReadOnlyList<ErgDistances> rows)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1.5) };
        card.Background = new SolidColorBrush(Color.FromArgb(0x22, Ergs.Green.R, Ergs.Green.G, Ergs.Green.B));
        card.BorderBrush = new SolidColorBrush(Ergs.Green);
        var stack = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var ic = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 16, Foreground = new SolidColorBrush(Ergs.Green), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(ic);
        title.Children.Add(new TextBlock { Text = "Initial isolation and protective action distances (Table 1)", FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock { Text = "Use these when there is NO FIRE. If there's a fire, use the evacuation distances in the guide. ISOLATE in all directions, then PROTECT people downwind.", Style = Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 10) });

        // Rows with the same distances are shown once, with all their names.
        foreach (var group in rows.GroupBy(r => string.Join("|", r.Small.Concat(r.Large))))
        {
            var first = group.First();
            stack.Children.Add(new TextBlock { Text = string.Join(" · ", group.Select(r => r.Name).Distinct()), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6), TextWrapping = TextWrapping.Wrap });
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var small = SpillBox("SMALL SPILL", "small package or small leak from a large package", first.Small);
            var large = first.LargeUsesTable3
                ? SpillNote("LARGE SPILL", "Use Table 3 in the ERG — distances depend on the container type and wind speed.")
                : SpillBox("LARGE SPILL", "large package or many small packages", first.Large);
            Grid.SetColumn(large, 2);
            g.Children.Add(small);
            g.Children.Add(large);
            stack.Children.Add(g);
        }
        card.Child = stack;
        return card;
    }

    private FrameworkElement SpillBox(string title, string sub, string[] d)
    {
        var b = new Border { Style = Res<Style>("Card"), Padding = new Thickness(14, 10, 14, 12) };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Ergs.Green) });
        s.Children.Add(new TextBlock { Text = sub, Style = Res<Style>("Muted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
        s.Children.Add(Metric("Isolate in all directions", d.ElementAtOrDefault(0) ?? "—"));
        s.Children.Add(Metric("Protect downwind — day", d.ElementAtOrDefault(1) ?? "—"));
        s.Children.Add(Metric("Protect downwind — night", d.ElementAtOrDefault(2) ?? "—"));
        b.Child = s;
        return b;
    }

    private FrameworkElement SpillNote(string title, string text)
    {
        var b = new Border { Style = Res<Style>("Card"), Padding = new Thickness(14, 10, 14, 12) };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Ergs.Green), Margin = new Thickness(0, 0, 0, 8) });
        s.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var open = SmallButton("", "Official ERG (PDF)");
        open.Margin = new Thickness(0, 10, 0, 0);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (_, _) => OpenUrl(ErgUrl);
        s.Children.Add(open);
        b.Child = s;
        return b;
    }

    private static FrameworkElement Metric(string label, string value)
    {
        var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var v = new TextBlock { Text = value, FontWeight = FontWeights.Bold, FontSize = 13.5 };
        DockPanel.SetDock(v, Dock.Right);
        d.Children.Add(v);
        d.Children.Add(new TextBlock { Text = label, Style = Res<Style>("Muted") });
        return d;
    }

    private FrameworkElement GuideColumn(List<(char Kind, string Text)> lines)
    {
        var card = new Border { Style = Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var s = new StackPanel();
        foreach (var (kind, text) in lines)
        {
            switch (kind)
            {
                case 'H':
                    var h = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, s.Children.Count == 0 ? 0 : 14, 0, 6), Background = new SolidColorBrush(Color.FromArgb(0x30, Ergs.Orange.R, Ergs.Orange.G, Ergs.Orange.B)) };
                    h.Child = new TextBlock { Text = text, FontWeight = FontWeights.Black, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0x5E, 0x00)) };
                    s.Children.Add(h);
                    break;
                case 'S':
                    s.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 10, 0, 3) });
                    break;
                case 's':
                    s.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 6, 0, 2) });
                    break;
                case 'B':
                    s.Children.Add(BulletLine(text));
                    break;
                default:
                    var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                    if (text.StartsWith("CAUTION")) { tb.FontWeight = FontWeights.SemiBold; tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger"); }
                    s.Children.Add(tb);
                    break;
            }
        }
        card.Child = s;
        return card;
    }

    // =====================================================================
    // Hazmat placards
    // =====================================================================

    public static readonly PlacardSpec[] Placards =
    [
        new("Dangerous", "Mixed load", "", HazardSigns.White, HazardSigns.White, HazardSymbol.None, HazardSigns.Black, "DANGEROUS", "111", "Two or more Table 2 categories in non-bulk packages may use one DANGEROUS placard.", PlacardFill.Dangerous),
        new("Explosives 1.1, 1.2, 1.3", "Class 1", "1", HazardSigns.Orange, HazardSigns.Orange, HazardSymbol.Burst, HazardSigns.Black, "EXPLOSIVES", "112", "Table 1 — placard any amount."),
        new("Explosives 1.4", "Class 1", "1", HazardSigns.Orange, HazardSigns.Orange, HazardSymbol.None, HazardSigns.Black, "1.4 EXPLOSIVES", "114", "Table 2."),
        new("Blasting agents 1.5", "Class 1", "1", HazardSigns.Orange, HazardSigns.Orange, HazardSymbol.None, HazardSigns.Black, "1.5 BLASTING AGENTS", "112", "Table 2."),
        new("Explosives 1.6", "Class 1", "1", HazardSigns.Orange, HazardSigns.Orange, HazardSymbol.None, HazardSigns.Black, "1.6 EXPLOSIVES", "114", "Table 2."),
        new("Flammable gas", "Division 2.1", "2", HazardSigns.Red, HazardSigns.Red, HazardSymbol.Flame, HazardSigns.White, "FLAMMABLE GAS", "118", "Table 2."),
        new("Non-flammable gas", "Division 2.2", "2", HazardSigns.Green, HazardSigns.Green, HazardSymbol.Cylinder, HazardSigns.White, "NON-FLAMMABLE GAS", "120", "Table 2."),
        new("Oxygen", "Division 2.2 (oxidizer)", "2", HazardSigns.Yellow, HazardSigns.Yellow, HazardSymbol.FlameOverCircle, HazardSigns.Black, "OXYGEN", "122", "Compressed or refrigerated oxygen."),
        new("Poison gas / inhalation hazard", "Division 2.3", "2", HazardSigns.White, HazardSigns.White, HazardSymbol.Skull, HazardSigns.Black, "POISON GAS", "123", "Table 1 — placard any amount."),
        new("Flammable liquid", "Class 3", "3", HazardSigns.Red, HazardSigns.Red, HazardSymbol.Flame, HazardSigns.White, "FLAMMABLE", "127", "Table 2. Gasoline and fuel oil placards use GUIDE 128."),
        new("Combustible liquid", "Class 3 (U.S.)", "3", HazardSigns.Red, HazardSigns.Red, HazardSymbol.Flame, HazardSigns.White, "COMBUSTIBLE", "127", "Table 2 (bulk only in the U.S.)."),
        new("Flammable solid", "Division 4.1", "4", HazardSigns.White, HazardSigns.White, HazardSymbol.Flame, HazardSigns.Black, "FLAMMABLE SOLID", "134", "Table 2.", PlacardFill.RedStripes),
        new("Spontaneously combustible", "Division 4.2", "4", HazardSigns.White, HazardSigns.Red, HazardSymbol.Flame, HazardSigns.Black, "SPONTANEOUSLY COMBUSTIBLE", "136", "Table 2.", PlacardFill.TopBottom),
        new("Dangerous when wet", "Division 4.3", "4", HazardSigns.Blue, HazardSigns.Blue, HazardSymbol.Flame, HazardSigns.White, "DANGEROUS WHEN WET", "139", "Table 1 — placard any amount."),
        new("Oxidizer", "Division 5.1", "5.1", HazardSigns.Yellow, HazardSigns.Yellow, HazardSymbol.FlameOverCircle, HazardSigns.Black, "OXIDIZER", "143", "Table 2."),
        new("Organic peroxide", "Division 5.2", "5.2", HazardSigns.Red, HazardSigns.Yellow, HazardSymbol.Flame, HazardSigns.Black, "ORGANIC PEROXIDE", "148", "Table 1 if Type B, temperature controlled; otherwise Table 2.", PlacardFill.TopBottom),
        new("Poison / toxic", "Division 6.1", "6", HazardSigns.White, HazardSigns.White, HazardSymbol.Skull, HazardSigns.Black, "POISON", "153", "Table 2. Poison-by-inhalation materials use the POISON INHALATION HAZARD placard (Table 1)."),
        new("Infectious substance", "Division 6.2", "6", HazardSigns.White, HazardSigns.White, HazardSymbol.Biohazard, HazardSigns.Black, "INFECTIOUS SUBSTANCE", "158", "Label only — no placard is required."),
        new("Radioactive", "Class 7", "7", HazardSigns.Yellow, HazardSigns.White, HazardSymbol.Trefoil, HazardSigns.Black, "RADIOACTIVE", "163", "Table 1 for Radioactive Yellow III packages.", PlacardFill.TopBottom),
        new("Corrosive", "Class 8", "8", HazardSigns.White, HazardSigns.Black, HazardSymbol.Corrosion, HazardSigns.Black, "CORROSIVE", "153", "Table 2.", PlacardFill.TopBottom),
        new("Miscellaneous", "Class 9", "9", HazardSigns.White, HazardSigns.White, HazardSymbol.None, HazardSigns.Black, "", "171", "Table 2 — not required for domestic transport.", PlacardFill.Class9),
        new("Lithium batteries", "Class 9 label", "9", HazardSigns.White, HazardSigns.White, HazardSymbol.Battery, HazardSigns.Black, "", "147", "Lithium ion (UN3480, UN3481): GUIDE 147. Lithium metal (UN3090, UN3091): GUIDE 138.", PlacardFill.Class9),
    ];

    private FrameworkElement BuildPlacards()
    {
        var page = new StackPanel();
        page.Children.Add(Callout(Ergs.Orange, "", "Use a placard to pick a guide only when you can't get the ID number or name",
            "Read placards from upwind and a safe distance (binoculars help). Once the 4-digit ID number or shipping name is known, look it up in the ERG tab and use that guide instead."));

        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var p in Placards)
        {
            var card = new Border { Style = Res<Style>("Card"), Width = 214, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14) };
            var s = new StackPanel();
            s.Children.Add(HazardSigns.Placard(p, 118));
            s.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Bold, FontSize = 13.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
            var meta = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
            var guideButton = new Button { Style = Res<Style>("LinkButton"), Content = $"Guide {p.Guide}", ToolTip = "Open this guide", FontWeight = FontWeights.SemiBold };
            string guideNo = p.Guide;
            guideButton.Click += (_, _) => OpenGuide(guideNo);
            DockPanel.SetDock(guideButton, Dock.Right);
            meta.Children.Add(guideButton);
            meta.Children.Add(new TextBlock { Text = p.ClassText, Style = Res<Style>("Muted") });
            s.Children.Add(meta);
            s.Children.Add(new TextBlock { Text = p.Note, Style = Res<Style>("Muted"), FontSize = 11.5 });
            card.Child = s;
            wrap.Children.Add(card);
        }
        page.Children.Add(wrap);

        page.Children.Add(TwoColumns(
            Section("When placards are required (49 CFR 172.504)", Stack(
                Label("TABLE 1 — PLACARD ANY AMOUNT"),
                Bullets(["Explosives 1.1, 1.2, 1.3", "Poison gas (2.3)", "Dangerous when wet (4.3)", "Organic peroxide, Type B, temperature controlled (5.2)", "Poison inhalation hazard (6.1)", "Radioactive Yellow III label (7)"]),
                Label("TABLE 2 — 1,001 LB OR MORE"),
                Para("Every other class (1.4–1.6, 2.1, 2.2, 3, combustible, 4.1, 4.2, 5.1, 5.2, 6.1, 8, 9). Placards aren't required on a highway or rail vehicle carrying less than 454 kg (1,001 lb) aggregate gross weight of Table 2 materials in non-bulk packages."),
                Label("DANGEROUS PLACARD"),
                Para("A load with two or more Table 2 categories in non-bulk packages may show one DANGEROUS placard instead — except a category with 1,000 kg (2,205 lb) or more loaded at one facility, which needs its own placard."),
                Label("OTHER RULES"),
                Bullets(["Placard all four sides of the vehicle.", "Bulk packages are placarded regardless of quantity, and show the 4-digit ID number on the placard or an orange panel.", "6.2 infectious substances have no placard."]))),
            Section("Hazard classes", Stack(
                KeyRows([
                    ("Class 1", "Explosives (divisions 1.1–1.6)"),
                    ("Class 2", "Gases — 2.1 flammable, 2.2 non-flammable non-toxic, 2.3 toxic"),
                    ("Class 3", "Flammable liquids (and combustible liquids in the U.S.)"),
                    ("Class 4", "4.1 flammable solids · 4.2 spontaneously combustible · 4.3 dangerous when wet"),
                    ("Class 5", "5.1 oxidizers · 5.2 organic peroxides"),
                    ("Class 6", "6.1 toxic (poison) · 6.2 infectious substances"),
                    ("Class 7", "Radioactive materials"),
                    ("Class 8", "Corrosives"),
                    ("Class 9", "Miscellaneous (lithium batteries, elevated-temperature materials, …)"),
                ]),
                Label("EXPLOSIVE DIVISIONS"),
                KeyRows([
                    ("1.1", "Mass explosion hazard"),
                    ("1.2", "Projection hazard, no mass explosion"),
                    ("1.3", "Fire hazard with minor blast or projection hazard"),
                    ("1.4", "No significant hazard"),
                    ("1.5", "Very insensitive, with a mass explosion hazard"),
                    ("1.6", "Extremely insensitive articles, no mass explosion"),
                ])))));
        page.Children.Add(SourceNote("Placards are simplified drawings for recognition — not to scale and not for printing. Starting guides from the ERG 2024 placard table; placarding rules from 49 CFR 172.504."));
        return page;
    }

    /// <summary>Opens an orange guide on the ERG tab.</summary>
    public void OpenGuide(string number)
    {
        Select("erg");
        _search.Text = "";
        var g = ErgData.Guide(number);
        if (g is not null) _detail.Content = GuideCard(null, g);
    }

    // =====================================================================
    // GHS & NFPA 704
    // =====================================================================

    public static readonly (HazardSymbol Symbol, string Name, string Hazards)[] GhsPictograms =
    [
        (HazardSymbol.HealthHazard, "Health hazard", "Carcinogen, mutagen, reproductive toxicity, respiratory sensitizer, target organ toxicity, aspiration toxicity"),
        (HazardSymbol.Flame, "Flame", "Flammables, pyrophorics, self-heating, emits flammable gas, self-reactives, organic peroxides"),
        (HazardSymbol.Exclamation, "Exclamation mark", "Irritant (skin and eye), skin sensitizer, acute toxicity (harmful), narcotic effects, respiratory tract irritant"),
        (HazardSymbol.Cylinder, "Gas cylinder", "Gases under pressure"),
        (HazardSymbol.Corrosion, "Corrosion", "Skin corrosion or burns, serious eye damage, corrosive to metals"),
        (HazardSymbol.Burst, "Exploding bomb", "Explosives, self-reactives, organic peroxides"),
        (HazardSymbol.FlameOverCircle, "Flame over circle", "Oxidizers"),
        (HazardSymbol.Environment, "Environment", "Aquatic toxicity (not required by OSHA, but often on labels)"),
        (HazardSymbol.Skull, "Skull and crossbones", "Acute toxicity — fatal or toxic"),
    ];

    private FrameworkElement BuildGhs()
    {
        var page = new StackPanel();
        var wrap = new WrapPanel();
        foreach (var (sym, name, hazards) in GhsPictograms)
        {
            var card = new Border { Style = Res<Style>("Card"), Width = 214, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14) };
            var s = new StackPanel();
            s.Children.Add(HazardSigns.Pictogram(sym, 96));
            s.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 13.5, Margin = new Thickness(0, 10, 0, 3) });
            s.Children.Add(new TextBlock { Text = hazards, Style = Res<Style>("Muted"), FontSize = 11.5 });
            card.Child = s;
            wrap.Children.Add(card);
        }
        page.Children.Add(Section("GHS pictograms (OSHA HazCom)", wrap, padBottom: 8));

        page.Children.Add(TwoColumns(
            Section("Reading a GHS label", Stack(
                KeyRows([
                    ("Product identifier", "Name or number, matching the SDS"),
                    ("Signal word", "DANGER (more severe) or WARNING (less severe)"),
                    ("Hazard statements", "What the hazard is, e.g. \"Highly flammable liquid and vapor\""),
                    ("Pictograms", "Red-bordered diamonds like the ones above"),
                    ("Precautionary statements", "How to prevent and respond to exposure, store and dispose"),
                    ("Supplier", "Name, address and phone number of the manufacturer or importer"),
                ]))),
            Section("NFPA 704 diamond", Stack(
                Row(HazardSigns.Nfpa704(3, 2, 1, "W", 120), Stack(
                    ColorKey(HazardSigns.Blue, "Health (left, blue)"),
                    ColorKey(HazardSigns.Red, "Flammability (top, red)"),
                    ColorKey(HazardSigns.Yellow, "Instability (right, yellow)"),
                    ColorKey(Colors.LightGray, "Special (bottom, white) — W reacts with water, OX oxidizer, SA simple asphyxiant"),
                    Para("0 = minimal hazard, 4 = severe."))),
                Label("RATINGS"),
                KeyRows([
                    ("Health 4", "Can be lethal on short exposure"),
                    ("Health 3", "Short exposure can cause serious or permanent injury"),
                    ("Health 2", "Can cause temporary incapacitation or residual injury"),
                    ("Health 1", "Can cause irritation or minor residual injury"),
                    ("Fire 4", "Burns readily; vaporizes at normal temperature (flash point below 73°F, boiling point below 100°F)"),
                    ("Fire 3", "Ignites at almost any ambient temperature (flash point below 100°F)"),
                    ("Fire 2", "Must be moderately heated to ignite (flash point 100–200°F)"),
                    ("Fire 1", "Must be preheated to ignite (flash point above 200°F)"),
                    ("Instability 4", "May detonate at normal temperature and pressure"),
                    ("Instability 3", "May detonate with a strong ignition source or when heated under confinement"),
                    ("Instability 2", "Violent chemical change at elevated temperature or pressure"),
                    ("Instability 1", "Normally stable; can become unstable when heated"),
                ])))));
        page.Children.Add(SourceNote("Pictograms are simplified drawings for recognition. GHS hazard groupings from OSHA's HazCom Standard (29 CFR 1910.1200); ratings summarized from NFPA 704."));
        return page;
    }

    // =====================================================================
    // Workplace safety
    // =====================================================================

    private FrameworkElement BuildWorkplace()
    {
        var page = new StackPanel();

        // Hierarchy of controls as a widening stack
        var hier = new StackPanel();
        var levels = new[]
        {
            ("Elimination", "Physically remove the hazard", Color.FromRgb(0x1E, 0x7D, 0x4F)),
            ("Substitution", "Replace the hazard with something safer", Color.FromRgb(0x4C, 0x9A, 0x3D)),
            ("Engineering controls", "Isolate people from the hazard (guards, ventilation)", Color.FromRgb(0xC9, 0xA2, 0x27)),
            ("Administrative controls", "Change the way people work (procedures, training, signs)", Color.FromRgb(0xE0, 0x7A, 0x1F)),
            ("PPE", "Protect the worker with personal protective equipment", Color.FromRgb(0xC8, 0x3A, 0x2E)),
        };
        for (int i = 0; i < levels.Length; i++)
        {
            var (name, desc, c) = levels[i];
            var bar = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(c), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(i * 14, 0, i * 14, 4) };
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
            t.Children.Add(new TextBlock { Text = desc, Foreground = Brushes.White, FontSize = 11.5, Opacity = 0.92, TextWrapping = TextWrapping.Wrap });
            bar.Child = t;
            hier.Children.Add(bar);
        }
        var scale = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var least = new TextBlock { Text = "Least effective ↓", Style = Res<Style>("Muted"), FontSize = 11 };
        DockPanel.SetDock(least, Dock.Right);
        scale.Children.Add(least);
        scale.Children.Add(new TextBlock { Text = "↑ Most effective", Style = Res<Style>("Muted"), FontSize = 11 });
        hier.Children.Add(scale);

        // Fire extinguisher classes
        var ext = new StackPanel();
        foreach (var (cls, shape, color, what) in new[]
        {
            ("A", "Triangle", Color.FromRgb(0x1E, 0x8E, 0x3E), "Ordinary combustibles — wood, paper, cloth, rubber, many plastics"),
            ("B", "Square", Color.FromRgb(0xD3, 0x2F, 0x2F), "Flammable liquids and gases — gasoline, oil, solvents, propane"),
            ("C", "Circle", Color.FromRgb(0x1F, 0x6F, 0xC9), "Energized electrical equipment"),
            ("D", "Star", Color.FromRgb(0xC9, 0xA2, 0x00), "Combustible metals — magnesium, titanium, sodium"),
            ("K", "Hexagon", Color.FromRgb(0x22, 0x22, 0x22), "Cooking oils and fats in commercial kitchens"),
        })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var b = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 12, 0), ToolTip = shape };
            b.Child = new TextBlock { Text = cls, Foreground = Brushes.White, FontWeight = FontWeights.Black, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(b, Dock.Left);
            row.Children.Add(b);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = $"Class {cls} · {shape}", FontWeight = FontWeights.SemiBold });
            t.Children.Add(new TextBlock { Text = what, Style = Res<Style>("Muted"), FontSize = 11.5 });
            row.Children.Add(t);
            ext.Children.Add(row);
        }
        ext.Children.Add(Label("USE IT: P.A.S.S."));
        ext.Children.Add(KeyRows([("Pull", "the pin"), ("Aim", "low, at the base of the fire"), ("Squeeze", "the handle"), ("Sweep", "side to side")]));
        ext.Children.Add(Para("Only fight a small, contained fire with a clear exit behind you. If in doubt, get out and call 911."));
        ext.Children.Add(Label("UPKEEP (1910.157)"));
        ext.Children.Add(KeyRows([("Monthly", "Visual inspection"), ("Yearly", "Maintenance check, recorded on the tag"), ("Yearly", "Training for employees expected to use them")]));

        page.Children.Add(TwoColumns(Section("Hierarchy of controls (NIOSH)", hier), Section("Fire extinguisher classes", ext)));

        // Signal words & safety colors
        var signs = new StackPanel();
        foreach (var (word, bg, fg, meaning) in new[]
        {
            ("DANGER", Color.FromRgb(0xC8, 0x10, 0x2E), Colors.White, "Will result in death or serious injury if not avoided"),
            ("WARNING", Color.FromRgb(0xFF, 0x78, 0x00), Colors.Black, "Could result in death or serious injury"),
            ("CAUTION", Color.FromRgb(0xFF, 0xD1, 0x00), Colors.Black, "Could result in minor or moderate injury"),
            ("NOTICE", Color.FromRgb(0x00, 0x56, 0xA3), Colors.White, "Property damage or practices not related to injury"),
            ("SAFETY FIRST", Color.FromRgb(0x00, 0x8A, 0x4B), Colors.White, "Safety instructions, first aid and emergency equipment"),
        })
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var b = new Border { Width = 116, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(bg), Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(0, 0, 12, 0) };
            b.Child = new TextBlock { Text = word, Foreground = new SolidColorBrush(fg), FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(b, Dock.Left);
            row.Children.Add(b);
            row.Children.Add(new TextBlock { Text = meaning, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            signs.Children.Add(row);
        }

        page.Children.Add(TwoColumns(
            Section("Lockout / tagout steps (29 CFR 1910.147)", Stack(
                Numbered([
                    "Prepare — identify every energy source (electrical, hydraulic, pneumatic, mechanical, thermal, chemical, gravity).",
                    "Notify affected employees that the equipment is being locked out.",
                    "Shut down the equipment the normal way.",
                    "Isolate it from every energy source.",
                    "Apply your own lock and tag to each isolation point.",
                    "Release or restrain stored energy (bleed, block, ground, discharge).",
                    "Verify — try to start it and test for zero energy. Return controls to off.",
                ]),
                Label("RESTORING"),
                Para("Clear tools and people, reinstall guards, notify affected employees, and remove locks — only the person who applied a lock removes it. Inspect each energy control procedure at least once a year."))),
            Section("Safety signs and colors (ANSI Z535)", signs)));

        page.Children.Add(Section("Safety Data Sheet — the 16 sections", Columns3(
        [
            "1  Identification", "2  Hazard(s) identification", "3  Composition / ingredients", "4  First-aid measures",
            "5  Fire-fighting measures", "6  Accidental release measures", "7  Handling and storage", "8  Exposure controls / PPE",
            "9  Physical and chemical properties", "10  Stability and reactivity", "11  Toxicological information", "12  Ecological information*",
            "13  Disposal considerations*", "14  Transport information*", "15  Regulatory information*", "16  Other information (revision date)",
        ]), padBottom: 6));
        page.Children.Add(SourceNote("* Sections 12–15 are on every SDS, but OSHA doesn't enforce their content. Summaries of OSHA 29 CFR 1910.1200 and 1910.147, NIOSH, NFPA 10 and ANSI Z535 — check the full standard before relying on a detail."));
        return page;
    }

    // =====================================================================
    // DOT & fleet rules
    // =====================================================================

    private FrameworkElement BuildFleet()
    {
        var page = new StackPanel();
        page.Children.Add(TwoColumns(
            Section("Hours of service — property-carrying (49 CFR 395.3)", Stack(
                KeyRows([
                    ("11-hour limit", "Drive up to 11 hours after 10 consecutive hours off duty"),
                    ("14-hour limit", "No driving after the 14th hour since coming on duty"),
                    ("30-minute break", "Required after 8 cumulative hours of driving (on-duty not driving or off duty counts)"),
                    ("60/70-hour limit", "No driving after 60 hours on duty in 7 days, or 70 hours in 8 days"),
                    ("34-hour restart", "34 or more consecutive hours off resets the 7/8-day period"),
                    ("Sleeper berth", "Split the 10 hours off: at least 7 in the berth plus at least 2 off duty or in the berth"),
                    ("Adverse conditions", "Driving and duty window may be extended by up to 2 hours"),
                    ("Short-haul", "Within 150 air-miles and back within 14 hours: no logs needed, time records instead"),
                ]),
                Label("PASSENGER-CARRYING"),
                Para("10 hours driving after 8 consecutive hours off · no driving after 15 hours on duty · 60/70-hour limit."))),
            Section("Post-accident drug & alcohol testing (49 CFR 382.303)", Stack(
                TestTable(),
                Label("TIME LIMITS"),
                KeyRows([
                    ("Alcohol", "Within 2 hours — if not, document why; stop trying after 8 hours"),
                    ("Drugs", "Within 32 hours — stop trying after that and document why"),
                    ("Driver", "No alcohol for 8 hours after the accident or until tested"),
                ])))));

        page.Children.Add(TwoColumns(
            Section("What counts as a DOT accident (49 CFR 390.5)", Stack(
                Para("An occurrence involving a commercial motor vehicle on a highway that results in:"),
                Bullets(["A fatality", "Bodily injury to a person who immediately receives medical treatment away from the scene", "One or more vehicles towed from the scene because of disabling damage"]),
                Para("Getting in or out of a parked vehicle, and loading or unloading cargo, are not DOT accidents. Record accidents on the accident register and keep it for 3 years."))),
            Section("Recurring fleet deadlines", KeyRows([
                ("Medical certificate", "Up to 24 months (shorter when the examiner says so)"),
                ("MVR review", "Each driver's motor vehicle record at least every 12 months"),
                ("Annual inspection", "Every vehicle at least once every 12 months"),
                ("DVIR", "Driver vehicle inspection reports kept 3 months"),
                ("Clearinghouse", "Full query before hire, limited query on each driver every year"),
                ("Accident register", "Kept 3 years after each accident"),
            ]))));
        page.Children.Add(SourceNote("Summaries of FMCSA rules (49 CFR Parts 382, 390, 391, 395 and 396). Exceptions apply — check the regulation or fmcsa.dot.gov before relying on a detail."));
        return page;
    }

    private FrameworkElement TestTable()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new[]
        {
            ("TYPE OF ACCIDENT", "CITATION", "TEST?"),
            ("Human fatality", "Yes", "Yes"),
            ("Human fatality", "No", "Yes"),
            ("Injury treated away from the scene", "Yes", "Yes"),
            ("Injury treated away from the scene", "No", "No"),
            ("Disabling damage — tow-away", "Yes", "Yes"),
            ("Disabling damage — tow-away", "No", "No"),
        };
        for (int r = 0; r < rows.Length; r++)
        {
            g.RowDefinitions.Add(new RowDefinition());
            var (a, b, c) = rows[r];
            var cells = new[] { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                var t = new TextBlock { Text = cells[i], Margin = new Thickness(0, 3, 8, 3), TextWrapping = TextWrapping.Wrap };
                if (r == 0) t.Style = Res<Style>("TableHeader");
                else if (i == 2) { t.FontWeight = FontWeights.Bold; t.SetResourceReference(TextBlock.ForegroundProperty, c == "Yes" ? "Brush.Danger" : "Brush.TextMuted"); }
                Grid.SetRow(t, r); Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
        }
        var note = new StackPanel();
        note.Children.Add(g);
        note.Children.Add(new TextBlock { Text = "\"Citation\" means the CMV driver was cited for a moving traffic violation from the accident.", Style = Res<Style>("Muted"), FontSize = 11.5 });
        return note;
    }

    // =====================================================================
    // OSHA & rates
    // =====================================================================

    private FrameworkElement BuildOsha()
    {
        var page = new StackPanel();
        page.Children.Add(TwoColumns(
            Section("Reporting to OSHA (29 CFR 1904.39)", Stack(
                KeyRows([
                    ("Within 8 hours", "A work-related fatality"),
                    ("Within 24 hours", "An in-patient hospitalization, an amputation, or loss of an eye"),
                ]),
                Para("Call 1-800-321-OSHA (6742), call or visit your area office, or report online at osha.gov. Fatalities count if they happen within 30 days of the incident; hospitalizations, amputations and eye losses within 24 hours."))),
            Section("Recordkeeping calendar (29 CFR 1904)", KeyRows([
                ("7 calendar days", "Record a recordable case on the OSHA 300 log and 301 form"),
                ("Feb 1 – Apr 30", "Post the certified 300A summary where employees can see it"),
                ("March 2", "Electronic submission (ITA) for covered establishments"),
                ("5 years", "Keep the 300, 300A and 301 records"),
            ]))));

        page.Children.Add(TwoColumns(
            Section("Is it recordable? (1904.7)", Stack(
                Para("A work-related injury or illness that's a new case and results in any of:"),
                Bullets(["Death", "Days away from work", "Restricted work or job transfer", "Medical treatment beyond first aid", "Loss of consciousness", "A significant injury or illness diagnosed by a physician or licensed health care professional (e.g. cancer, fractured bone, punctured eardrum)"]))),
            Section("Rate formulas", Stack(
                Formula("TRIR", "Recordable cases × 200,000 ÷ hours worked"),
                Formula("DART", "(Days-away + restricted/transfer cases) × 200,000 ÷ hours worked"),
                Formula("LTIR", "Lost-time (days-away) cases × 200,000 ÷ hours worked"),
                Formula("Severity", "Lost workdays × 200,000 ÷ hours worked"),
                Formula("Accident frequency", "Vehicle accidents × 1,000,000 ÷ miles driven"),
                Para("200,000 hours = 100 full-time employees working 40 hours a week for 50 weeks. Guardrail calculates these for you on the Dashboard from Monthly Data.")))));
        page.Children.Add(SourceNote("Summaries of 29 CFR 1904. Electronic submission rules depend on establishment size and industry — check osha.gov/injuryreporting."));
        return page;
    }

    // =====================================================================
    // OSHA topics
    // =====================================================================

    private FrameworkElement BuildOshaTopics()
    {
        var page = new StackPanel();

        page.Children.Add(TwoColumns(
            Section("Fall protection", Stack(
                Label("WHEN IT'S REQUIRED"),
                KeyRows([
                    ("General industry", "4 ft (1910.28)"),
                    ("Construction", "6 ft (1926.501)"),
                    ("Scaffolds", "10 ft (1926.451)"),
                    ("Any height", "Over dangerous equipment or machinery"),
                ]),
                Label("GUARDRAILS AND ARREST SYSTEMS"),
                Bullets([
                    "Top rail 42 in (±3 in) high, holds 200 lb; midrail about halfway; toeboard at least 3.5 in.",
                    "Personal fall arrest anchors hold 5,000 lb per person (or are designed by a qualified person).",
                    "Free fall no more than 6 ft, and the worker must not hit a lower level.",
                    "Plan rescue — suspension trauma can set in within minutes.",
                ]),
                Label("LADDERS"),
                Bullets([
                    "Set extension ladders at 4:1 — 1 ft out for every 4 ft up.",
                    "Side rails extend 3 ft above the landing.",
                    "Keep three points of contact; face the ladder; don't stand on the top cap or top step.",
                    "Inspect before use; tag out damaged ladders.",
                    "New fixed ladders over 24 ft need a ladder safety or fall arrest system (cages phase out by 2036).",
                ]))),
            Section("Training — who needs it and how often", KeyRows([
                ("Hazard Communication", "Before first assignment and whenever a new hazard is introduced"),
                ("Lockout / tagout", "At hire, when jobs or equipment change; procedures inspected yearly"),
                ("Forklifts", "Before operating; evaluated at least every 3 years"),
                ("Respirators", "Medical evaluation, then fit test before first use and every year"),
                ("Hearing conservation", "Every year for anyone at or above 85 dBA"),
                ("Bloodborne pathogens", "At hire and every year"),
                ("Fire extinguishers", "At hire and every year (if expected to use them)"),
                ("Emergency action plan", "At hire, when the plan or their role changes"),
                ("Confined space", "Before entry duties and when duties change"),
                ("Fall protection", "Before exposure (construction); retrain when needed"),
                ("PPE", "Before use: when, what, how to wear, limits, care"),
                ("First aid", "Required where no clinic or hospital is near; keep certifications current"),
            ]))));

        page.Children.Add(TwoColumns(
            Section("Noise (1910.95)", Stack(
                KeyRows([
                    ("85 dBA (8-hr avg)", "Action level — hearing conservation program, free hearing protection, baseline audiogram within 6 months, yearly audiograms and training"),
                    ("90 dBA (8-hr avg)", "Permissible exposure limit — engineering or administrative controls, hearing protection required"),
                    ("Every +5 dB", "Halves the allowed time (95 dBA = 4 hours, 100 dBA = 2 hours)"),
                    ("115 dBA", "Never exceed (continuous noise)"),
                ]),
                Para("Rule of thumb: if you have to raise your voice to talk to someone an arm's length away, it's probably over 85 dBA."))),
            Section("Heat and cold stress", Stack(
                Label("HEAT — WATER, REST, SHADE"),
                Bullets([
                    "Drink about 1 cup of water every 15–20 minutes, even if not thirsty.",
                    "Acclimatize new and returning workers: about 20% of a normal workload on day one, building up over 1–2 weeks.",
                    "Heat exhaustion: heavy sweating, weakness, dizziness, nausea, headache — move to shade, cool down, give water.",
                    "Heat stroke: confusion, slurred speech, fainting or seizures, very high body temperature — CALL 911 and cool the person immediately (ice, cold water, wet cloths).",
                ]),
                Label("COLD"),
                Bullets([
                    "Hypothermia: shivering, confusion, slurred speech — get them warm and dry, call 911 if severe.",
                    "Frostbite: numbness, white or grayish skin — don't rub it; warm slowly and get medical care.",
                ])))));

        page.Children.Add(TwoColumns(
            Section("Confined spaces (1910.146)", Stack(
                Para("A confined space is large enough to enter, has limited ways in or out, and isn't designed for continuous occupancy. It's permit-required if it also has (or could have) a hazardous atmosphere, engulfment hazard, inward-sloping walls that could trap someone, or any other serious hazard."),
                KeyRows([
                    ("Entry supervisor", "Signs the permit, makes sure tests and rescue are in place"),
                    ("Attendant", "Stays outside, keeps a count, summons rescue — never enters"),
                    ("Entrant", "Knows the hazards, communicates, gets out when told"),
                ]),
                Label("TEST THE AIR — IN THIS ORDER"),
                KeyRows([
                    ("1. Oxygen", "19.5% – 23.5%"),
                    ("2. Flammables", "Below 10% of the LEL"),
                    ("3. Toxics", "Below the exposure limits (e.g. H2S, CO)"),
                ]),
                Para("Most confined-space deaths are would-be rescuers. Don't enter to rescue unless you're trained, equipped and part of the plan."))),
            Section("Excavations and trenches (1926 Subpart P)", Stack(
                Bullets([
                    "5 ft or deeper: a protective system — sloping, benching, shoring or a trench box (unless it's all stable rock).",
                    "20 ft or deeper: the protective system must be designed by a registered professional engineer.",
                    "4 ft or deeper: a ladder, ramp or stairway within 25 ft of lateral travel.",
                    "Keep spoil piles and equipment at least 2 ft back from the edge.",
                    "A competent person inspects daily, before each shift and after rain or anything that changes conditions.",
                    "Call 811 before you dig.",
                ]),
                Label("MAXIMUM SLOPES (UNDER 20 FT)"),
                KeyRows([
                    ("Stable rock", "Vertical (90°)"),
                    ("Type A soil", "¾ : 1 (53°)"),
                    ("Type B soil", "1 : 1 (45°)"),
                    ("Type C soil", "1½ : 1 (34°)"),
                ])))));

        page.Children.Add(TwoColumns(
            Section("Electrical", Stack(
                KeyRows([
                    ("Overhead lines", "Keep people and conductive objects at least 10 ft from lines up to 50 kV, plus 4 in for every 10 kV above that"),
                    ("Cranes", "20 ft from lines up to 350 kV (1926.1408) unless the line is de-energized"),
                    ("GFCIs", "On temporary power and all 120 V, 15/20/30 A receptacles at construction sites"),
                    ("Cords", "No splices, no missing ground pins; don't run through doors or walls"),
                    ("Panels", "Keep 3 ft of clear working space in front of electrical panels"),
                ]),
                Para("Arc-flash boundaries, labels and PPE categories come from NFPA 70E — only qualified workers work on or near exposed energized parts."))),
            Section("Forklifts (1910.178)", Stack(
                Bullets([
                    "Only trained, evaluated operators — evaluated at least every 3 years.",
                    "Refresher training after an accident or near miss, unsafe driving, a bad evaluation, or a new type of truck or workplace.",
                    "Inspect before every shift (every shift if run around the clock); take defective trucks out of service.",
                    "No one under 18 may operate a forklift (U.S. child labor rules).",
                    "Wear the seat belt; forks low when traveling; look in the direction of travel; no riders.",
                    "Know the stability triangle — tip-overs happen turning with a raised load.",
                ])))));

        page.Children.Add(TwoColumns(
            Section("PPE (1910.132)", Stack(
                Bullets([
                    "Do a written, certified hazard assessment to decide what PPE each job needs.",
                    "The employer pays for required PPE — except ordinary safety-toe shoes, prescription safety glasses and everyday clothing.",
                    "Train before use and replace damaged PPE.",
                ]),
                Label("STANDARDS TO LOOK FOR"),
                KeyRows([
                    ("Eye / face", "ANSI Z87.1 — \"Z87+\" means impact rated"),
                    ("Hard hats", "ANSI Z89.1 — Type I (top) or II (top and side); Class G 2,200 V, E 20,000 V, C no electrical protection"),
                    ("High visibility", "ANSI/ISEA 107 — Class 2 or 3 near traffic"),
                    ("Cut gloves", "ANSI/ISEA 105 — levels A1 (lowest) to A9"),
                    ("Safety footwear", "ASTM F2413"),
                ]))),
            Section("Eyewash and first aid", Stack(
                KeyRows([
                    ("Eyewash reach", "Within 10 seconds (about 55 ft) of the hazard, on the same level, unobstructed"),
                    ("Flush", "15 minutes of tepid water (60–100°F)"),
                    ("Upkeep", "Activate plumbed units weekly; inspect all yearly (ANSI Z358.1)"),
                    ("First aid", "If no clinic or hospital is near, have trained first-aiders and supplies (1910.151)"),
                    ("Kits", "ANSI/ISEA Z308.1 Class A (common injuries) or Class B (higher-risk sites)"),
                ]),
                Para("OSHA requires suitable eyewash facilities wherever corrosive materials are used (1910.151(c))."))));

        page.Children.Add(TwoColumns(
            Section("Written programs most sites need", KeyRows([
                ("Emergency action plan", "1910.38 — may be verbal with 10 or fewer employees"),
                ("Fire prevention plan", "1910.39 — same 10-employee rule"),
                ("Hazard Communication", "1910.1200 — chemical list, labels, SDSs, training"),
                ("Energy control (LOTO)", "1910.147 — plus machine-specific procedures"),
                ("Respiratory protection", "1910.134 — when respirators are required"),
                ("Bloodborne pathogens", "1910.1030 — exposure control plan reviewed yearly; hepatitis B vaccine offered free within 10 working days"),
                ("Hearing conservation", "1910.95 — at or above 85 dBA"),
                ("Permit-required confined spaces", "1910.146 — if workers enter them"),
                ("PPE hazard assessment", "1910.132(d) — written certification"),
            ])),
            Section("Postings and other quick rules", Stack(
                Bullets([
                    "Display the OSHA \"Job Safety and Health — It's the Law\" poster where employees will see it.",
                    "Post the OSHA 300A summary February 1 – April 30.",
                    "Post any OSHA citation at or near the hazard for 3 working days or until it's fixed, whichever is longer.",
                    "Tell employees each year about their right to see exposure and medical records (1910.1020).",
                    "Exit routes stay clear, marked and unlocked from the inside; at least 28 in wide.",
                    "Compressed air for cleaning must be reduced below 30 psi, with chip guarding and PPE.",
                    "Employees can't be retaliated against for reporting an injury or a hazard (Section 11(c)).",
                ])))));

        page.Children.Add(TwoColumns(
            Section("OSHA penalties (2026 maximums)", Stack(
                KeyRows([
                    ("Serious", "$16,550 per violation"),
                    ("Other-than-serious", "$16,550 per violation"),
                    ("Failure to abate", "$16,550 per day past the abatement date"),
                    ("Willful or repeated", "$165,514 per violation"),
                ]),
                Para("No inflation increase was applied for 2026, so these are the 2025 amounts. State-plan states set their own (at least as high)."))),
            Section("What OSHA inspects first", Numbered([
                "Imminent danger",
                "Severe injuries and illnesses — fatalities, hospitalizations, amputations, eye losses",
                "Worker complaints",
                "Referrals from other agencies, organizations or the media",
                "Targeted inspections — high-hazard industries and emphasis programs",
                "Follow-up inspections",
            ]))));

        page.Children.Add(SourceNote("Quick summaries of OSHA standards (29 CFR 1910 and 1926) and consensus standards (ANSI, NFPA). State-plan states may be stricter. Check the full standard at osha.gov before relying on a detail."));
        return page;
    }

    private static FrameworkElement Formula(string name, string formula)
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
        b.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        var d = new DockPanel();
        var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        n.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        DockPanel.SetDock(n, Dock.Left);
        d.Children.Add(n);
        d.Children.Add(new TextBlock { Text = formula, FontFamily = new FontFamily("Consolas, Segoe UI"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        b.Child = d;
        return b;
    }

    // =====================================================================
    // Emergency numbers
    // =====================================================================

    public static readonly (string Group, string Name, string Number, string Note)[] Numbers =
    [
        ("Emergency", "Police, fire, EMS", "911", "Call first for any emergency."),
        ("Hazmat (24 hours)", "CHEMTREC", "1-800-424-9300", "U.S., Canada, U.S. Virgin Islands · from elsewhere 703-527-3887"),
        ("Hazmat (24 hours)", "CHEMTEL, Inc.", "1-888-255-3924", "From elsewhere 813-248-0573"),
        ("Hazmat (24 hours)", "INFOTRAC", "1-800-535-5053", "From elsewhere 352-323-3500"),
        ("Hazmat (24 hours)", "Verisk 3E", "1-800-451-8346", "From elsewhere 760-602-8703"),
        ("Hazmat (24 hours)", "CANUTEC (Canada)", "1-888-226-8832", "1-888-CANUTEC · 613-996-6666 · *666 on a cell phone in Canada"),
        ("Spills & releases", "National Response Center (NRC)", "1-800-424-8802", "Required for reportable-quantity releases and marine pollutants. Calling CHEMTREC does not count as calling the NRC."),
        ("Health", "Poison Control (U.S.)", "1-800-222-1222", "Nationwide, 24 hours."),
        ("Workplace", "OSHA", "1-800-321-6742", "1-800-321-OSHA — report fatalities (8 h) and serious injuries (24 h)."),
        ("Military shipments", "Explosives / ammunition (U.S. Army)", "703-695-4695", "Or 703-695-4696"),
        ("Military shipments", "Other hazmat (Defense Logistics Agency)", "1-800-851-8061", ""),
    ];

    private FrameworkElement BuildNumbers()
    {
        var page = new StackPanel();
        foreach (var group in Numbers.GroupBy(n => n.Group))
        {
            var list = new StackPanel();
            foreach (var (_, name, number, note) in group)
            {
                var row = new Button { Style = Res<Style>("TableRow"), Padding = new Thickness(4, 10, 4, 10), ToolTip = "Click to copy the number" };
                var d = new DockPanel();
                var copy = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 13, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                copy.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                DockPanel.SetDock(copy, Dock.Right);
                d.Children.Add(copy);
                var num = new TextBlock { Text = number, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
                num.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                DockPanel.SetDock(num, Dock.Right);
                d.Children.Add(num);
                var t = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                t.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
                if (note.Length > 0) t.Children.Add(new TextBlock { Text = note, Style = Res<Style>("Muted"), FontSize = 11.5 });
                d.Children.Add(t);
                row.Content = d;
                string toCopy = number;
                row.Click += (_, _) =>
                {
                    try { Clipboard.SetText(toCopy); Toaster.Show($"{toCopy} copied."); } catch { /* clipboard busy */ }
                };
                list.Children.Add(row);
            }
            page.Children.Add(Section(group.Key, list, padBottom: 6));
        }
        page.Children.Add(SourceNote("Numbers from the ERG 2024 inside back cover and osha.gov. Add your own site's numbers (plant emergency line, nearest hospital, company safety contact) to your emergency action plan."));
        return page;
    }

    // =====================================================================
    // Building blocks
    // =====================================================================

    private static FrameworkElement Section(string title, UIElement content, double padBottom = 16)
    {
        var card = new Border { Style = Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(20, 16, 20, padBottom) };
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, Style = Res<Style>("CardTitle"), FontSize = 15, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        s.Children.Add(content);
        card.Child = s;
        return card;
    }

    private static Grid TwoColumns(FrameworkElement left, FrameworkElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    private static UniformGrid Columns3(IEnumerable<string> items)
    {
        var g = new UniformGrid { Columns = 3 };
        foreach (var i in items) g.Children.Add(new TextBlock { Text = i, Margin = new Thickness(0, 0, 12, 8), TextWrapping = TextWrapping.Wrap });
        return g;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    private static FrameworkElement Row(FrameworkElement left, FrameworkElement right)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        left.Margin = new Thickness(0, 0, 18, 0);
        left.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(left, Dock.Left);
        d.Children.Add(left);
        d.Children.Add(right);
        return d;
    }

    private static TextBlock Para(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), LineHeight = 19 };

    private static TextBlock Label(string text) => new() { Text = text, Style = Res<Style>("SectionLabel"), Margin = new Thickness(0, 10, 0, 6) };

    private static FrameworkElement BulletLine(string text)
    {
        var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var dot = new TextBlock { Text = "•", Margin = new Thickness(2, 0, 9, 0), FontWeight = FontWeights.Bold };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        DockPanel.SetDock(dot, Dock.Left);
        d.Children.Add(dot);
        d.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return d;
    }

    private static StackPanel Bullets(IEnumerable<string> items)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var i in items) s.Children.Add(BulletLine(i));
        return s;
    }

    private static StackPanel Numbered(IEnumerable<string> items)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        int n = 1;
        foreach (var i in items)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var circle = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            circle.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            var num = new TextBlock { Text = (n++).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            num.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            circle.Child = num;
            DockPanel.SetDock(circle, Dock.Left);
            d.Children.Add(circle);
            d.Children.Add(new TextBlock { Text = i, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            s.Children.Add(d);
        }
        return s;
    }

    private static Grid KeyRows(IEnumerable<(string Key, string Value)> rows)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        int r = 0;
        foreach (var (k, v) in rows)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var kt = new TextBlock { Text = k, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 12, 3), TextWrapping = TextWrapping.Wrap };
            var vt = new TextBlock { Text = v, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
            vt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            Grid.SetRow(kt, r); Grid.SetRow(vt, r); Grid.SetColumn(vt, 1);
            g.Children.Add(kt); g.Children.Add(vt);
            r++;
        }
        return g;
    }

    private static FrameworkElement ColorKey(Color color, string text)
    {
        var d = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var sw = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
        DockPanel.SetDock(sw, Dock.Left);
        d.Children.Add(sw);
        d.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return d;
    }

    private static FrameworkElement Callout(Color color, string glyph, string title, string text)
    {
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1.2) };
        b.Background = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B));
        b.BorderBrush = new SolidColorBrush(color);
        var d = new DockPanel();
        var ic = new TextBlock { Text = glyph, Style = Res<Style>("Icon"), FontSize = 18, Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(ic, Dock.Left);
        d.Children.Add(ic);
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        s.Children.Add(new TextBlock { Text = text, Style = Res<Style>("Muted"), Margin = new Thickness(0, 3, 0, 0) });
        d.Children.Add(s);
        b.Child = d;
        return b;
    }

    private static Border Badge(string text, Color color)
    {
        var b = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Background = new SolidColorBrush(Color.FromArgb(0x2A, color.R, color.G, color.B));
        b.Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) };
        return b;
    }

    private static FrameworkElement SourceNote(string text)
    {
        var d = new DockPanel { Margin = new Thickness(2, 2, 0, 16) };
        var ic = new TextBlock { Text = "", Style = Res<Style>("Icon"), FontSize = 12, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        ic.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(ic, Dock.Left);
        d.Children.Add(ic);
        d.Children.Add(new TextBlock { Text = text, Style = Res<Style>("Muted"), FontSize = 11.5 });
        return d;
    }

    private static Button SmallButton(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, Style = Res<Style>("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Style = Res<Style>("SmallButton"), Content = row, VerticalAlignment = VerticalAlignment.Center };
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { Toaster.Show("Couldn't open the browser."); }
    }

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);
}

/// <summary>ERG section colors.</summary>
internal static class Ergs
{
    public static readonly Color Orange = Color.FromRgb(0xF2, 0x8C, 0x28);
    public static readonly Color Green = Color.FromRgb(0x1F, 0x9D, 0x55);
    public static readonly Color Blue = Color.FromRgb(0x2B, 0x7B, 0xD6);
}
