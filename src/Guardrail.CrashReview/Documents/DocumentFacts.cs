using System.Text.RegularExpressions;

namespace Guardrail.CrashAi.Documents;

/// <summary>One road user as a document describes it ("Unit 2 (gray Toyota Camry)", "the red Dodge Charger").</summary>
public sealed class DocParty
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Colour / make / type words, used to match the party with a vehicle seen in the video.</summary>
    public string Description { get; set; } = "";
    public int Striking { get; set; }
    public int Struck { get; set; }
    public int RearEnded { get; set; }
    /// <summary>Behaviour that contributes to a crash ("ran a red light") with a weight 0..1.</summary>
    public Dictionary<string, double> Factors { get; set; } = [];
    public List<string> Citations { get; set; } = [];
    public int AtFault { get; set; }
    public int NotAtFault { get; set; }
    /// <summary>The sentences the facts came from.</summary>
    public List<string> Evidence { get; set; } = [];
    public bool IsPerson { get; set; }
    /// <summary>The document says this is the writer's / camera vehicle ("my car", "dashcam vehicle").</summary>
    public bool IsSelf { get; set; }

    /// <summary>How strongly the document points at this party as the cause.</summary>
    public double Score =>
        Math.Max(0, Factors.Values.Sum() + 0.6 * Math.Min(Citations.Count, 2) + 1.0 * Math.Min(AtFault, 2)
                    + 0.6 * Math.Min(RearEnded, 1) + 0.1 * Math.Min(Striking, 2) - 0.8 * NotAtFault);
}

/// <summary>What could be read out of one attached document.</summary>
public sealed class DocFacts
{
    /// <summary>"police report", "news article" or "document".</summary>
    public string Kind { get; set; } = "document";
    public List<string> Dates { get; set; } = [];
    public List<string> Times { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    public List<string> Conditions { get; set; } = [];
    public List<string> Injuries { get; set; } = [];
    public bool HitAndRun { get; set; }
    public List<DocParty> Parties { get; set; } = [];

    /// <summary>How much weight the document deserves: an officer's report more than a news story.</summary>
    public double Weight => Kind switch { "police report" => 1.0, "news article" => 0.6, _ => 0.5 };
}

/// <summary>
/// Reads crash facts out of plain text with rules — who hit whom, violations, citations, "at fault" statements,
/// where and when. Fully offline; nothing is sent anywhere.
/// </summary>
public static class DocumentFacts
{
    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string Colors = "white|black|silver|gray|grey|red|blue|green|yellow|orange|brown|tan|beige|gold|maroon|purple|dark|light|dark-colored|light-colored";
    private const string Makes = "acura|audi|bmw|buick|cadillac|chevrolet|chevy|chrysler|dodge|fiat|ford|gmc|honda|hyundai|infiniti|jaguar|jeep|kia|land rover|lexus|lincoln|"
        + "mazda|mercedes|mercedes-benz|mini|mitsubishi|nissan|peterbilt|freightliner|kenworth|ram|renault|rover|subaru|tesla|toyota|volkswagen|vw|volvo|"
        + "pontiac|saturn|scion|amazon|ups|fedex";
    private const string Types = "car|sedan|coupe|suv|pickup truck|pickup|truck|semi-truck|semi|tractor-trailer|18-wheeler|box truck|van|minivan|bus|school bus|motorcycle|"
        + "motorbike|scooter|e-scooter|moped|bicycle|bike|patrol car|police car|cruiser|squad car|vehicle|wagon|hatchback|convertible|jeep|trailer|"
        + "charger|mustang|civic|camry|corolla|accord|altima|f-150|silverado|tahoe|explorer|prius|clio|soul|model 3|model y";
    private const string People = "pedestrian|cyclist|bicyclist|motorcyclist|scooter rider|rider|trooper|deputy|officer|suspect|driver";

    private static readonly HashSet<string> Generic = ["vehicle", "driver", "car", "vehicles", "drivers"];
    private static readonly HashSet<string> Persons = ["pedestrian", "cyclist", "bicyclist", "motorcyclist", "scooter rider", "rider", "trooper", "deputy", "officer", "suspect"];
    private static readonly HashSet<string> ColorWords = [.. Colors.Split('|')];

    private static readonly Regex Mention = new(
        @"\b(?<self>my (?:car|vehicle|truck|van|suv)|our (?:car|vehicle|truck|van)|(?:the )?(?:dash ?cam|camera|reporting|insured(?:'s)?) vehicle)\b"
        + @"|\b(?:unit|vehicle|veh\.?|v)\s*#?\s*(?<unit>\d{1,2})\b"
        + @"|\b(?<desc>(?:(?:19|20)\d{2}\s+)?(?:(?:" + Colors + @")\s+)?(?:(?:" + Makes + @")\s+)?(?:[a-z0-9-]+\s+)?(?:" + Types + @"))\b"
        + @"|\b(?<person>" + People + @")s?\b", I);

    /// <summary>(label, weight, pattern): behaviour that causes crashes.</summary>
    internal static readonly (string Label, double Weight, Regex Rx)[] Factors =
    [
        ("ran a red light", 0.9, new(@"ran (?:a|the) red (?:light|signal)|disregard(?:ed)? (?:the )?(?:red|stop and go) signal|running (?:a|the) red light|disregard stop and go signal|red[- ]light violation", I)),
        ("ran a stop sign", 0.9, new(@"ran (?:a|the) stop sign|failed to stop at (?:a|the) stop sign|disregard(?:ed)? (?:the )?stop sign", I)),
        ("failed to yield", 0.8, new(@"fail(?:ed|ure)? to yield|did not yield|didn't yield", I)),
        ("turned across oncoming traffic", 0.6, new(@"turn(?:ed|ing)? left in front of|left turn in front of|improper turn", I)),
        ("followed too closely", 0.7, new(@"follow(?:ed|ing) too clos(?:e|ely)", I)),
        ("unsafe lane change", 0.7, new(@"unsafe lane change|changed lanes unsafely|suddenly changed lanes|cut (?:in front of|off)|improper lane change|failed to maintain (?:a single )?lane", I)),
        ("speeding", 0.5, new(@"speeding|too fast for conditions|excessive speed|speed was a factor|over (?:the )?(?:speed )?limit|unsafe speed|high rate of speed|high speed", I)),
        ("distracted / phone use", 0.6, new(@"cell(?:ular)? phone|mobile device|texting|distracted|inattention|looking at (?:his|her|their|a) phone|on (?:his|her|their|the) phone", I)),
        ("impaired (alcohol or drugs)", 0.8, new(@"\bDUI\b|\bDWI\b|\bOVI\b|intoxicated|under the influence|impaired|alcohol|drunk", I)),
        ("crossed the centre line / wrong way", 0.9, new(@"cross(?:ed|ing) (?:the )?(?:center|centre|double yellow) ?line|wrong[- ]way|into (?:the )?oncoming (?:lane|traffic)", I)),
        ("lost control", 0.5, new(@"lost control", I)),
        ("reckless or careless driving", 0.6, new(@"reckless|careless driving", I)),
        ("intentional", 1.0, new(@"intentionally|deliberately|on purpose|swerved (?:toward|towards|at)", I)),
        ("fled the scene", 0.25, new(@"fled|hit[- ]and[- ]run|left the scene|failed to stop and render", I)),
        ("equipment failure", 0.5, new(@"lost (?:a|its|his|her) (?:wheel|tire)|tire (?:blew|blowout)|brakes? (?:failed|failure)|lost (?:its |his |her )?brakes|mechanical (?:failure|defect)", I)),
    ];

    private static readonly Regex Strike = new(@"\b(rear[- ]ended|struck|hit|collided with|crashed into|slammed into|ran into|smashed into|plowed into|sideswiped|t-boned|drove (?:\w+ ){0,3}into)\b", I);
    private static readonly Regex Passive = new(@"\b(?:was|were|got|been) (?:rear[- ]ended|struck|hit|sideswiped|t-boned)(?: \w+){0,3}? by\b", I);
    private static readonly Regex Cite = new(@"\b(?:cited|ticketed|charged|arrested|issued (?:a )?(?:citation|ticket)s?)\b", I);
    private static readonly Regex AtFaultRx = new(@"\b(?:at[- ]fault|was at fault|caused the (?:crash|collision|accident)|responsible for the (?:crash|collision|accident)|was the cause)\b", I);
    private static readonly Regex NotFaultRx = new(@"\b(?:not at fault|no fault|was not cited|no citations?)\b", I);
    private static readonly Regex Pronoun = new(@"^(?:he|she|they|it)\b", I);
    private static readonly Regex MphOver = new(@"(\d{2,3})\s*mph in an? (\d{2,3})\s*mph (?:zone|limit|speed zone)", I);

    private const string Road = @"(?:St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Hwy|Highway|Route|Rte|Dr|Drive|Way|Lane|Ln|Turnpike|Freeway|Fwy|Expressway|Parkway|Pkwy|Pike|Court|Ct|Place|Pl|Circle|Bridge)";
    private const string Name = @"(?:[NSEW]\.? |North |South |East |West )?(?:[A-Z0-9][\w'.-]*\s){0,3}";
    private static readonly Regex[] Locs =
    [
        new(@"\bintersection of (" + Name + Road + @"\.?) and (" + Name + Road + @"\.?)"),
        new(@"\b(" + Name + Road + @"\.?) (?:and|&|at) (" + Name + Road + @"\.?)"),
        new(@"\b(\d{1,5}(?:00)? block of " + Name + Road + @"\.?)"),
        new(@"\b(?:on|along|of|near|at) (" + Name + Road + @"\.?(?: \d+)?(?: (?:East|West|North|South|[NSEW]B))?)"),
        new(@"\b((?:I|Interstate|US|U\.S\.|SR|State Route|Route|Highway|Hwy|County Road|CR)[- ]\d{1,4}[A-Z]?(?: (?:East|West|North|South|[NSEW]B|northbound|southbound|eastbound|westbound))?)"),
        new(@"\b(Exit \d+[A-Z]?)\b"),
        new(@"^\s*location\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        new(@"^\s*([A-Z][\w .'-]+, [A-Z][a-z]+(?: [A-Z][a-z]+)?)\s+[—-]"),
    ];
    private static readonly Regex DateRx = new(@"\b((?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\.? \d{1,2}(?:st|nd|rd|th)?,? \d{4}|\d{1,2}/\d{1,2}/\d{2,4})\b");
    private static readonly Regex TimeRx = new(@"\b(\d{1,2}(?::\d{2})?\s*(?:a\.?m\.?|p\.?m\.?))", I);
    private static readonly Regex CondRx = new(@"\b(rain(?:ing|y)?|wet|snow(?:ing|y)?|ic(?:e|y)|fog(?:gy)?|dark(?:ness)?|night|daylight|dusk|dawn|sun glare|glare|clear|cloudy)\b", I);
    private static readonly Regex InjRx = new(@"\b(killed|fatal(?:ly)?|died|dead|serious(?:ly)? injur\w*|minor injur\w*|possible injur\w*|non-life-threatening|life-threatening|not injured|no injuries|unharmed|not hurt|uninjured|transported)\b", I);

    public static List<string> Sentences(string text)
    {
        text = text.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"').Replace("\r", "");
        text = Regex.Replace(text, @"[ \t​ ]+", " ");
        return Regex.Split(text, @"(?<=[.!?])\s+(?=[A-Z0-9""])|\n+")
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    private static string KeyOf(Match m)
    {
        if (m.Groups["self"].Success) return "my vehicle";
        if (m.Groups["unit"].Success) return "unit " + m.Groups["unit"].Value;
        if (m.Groups["desc"].Success)
        {
            string d = m.Groups["desc"].Value.ToLowerInvariant();
            d = Regex.Replace(d, @"^(?:the|a|an|his|her|their|its|this|that)\s+", "");
            d = Regex.Replace(d, @"^(?:19|20)\d{2}\s+", "");
            return d;
        }
        return m.Groups["person"].Value.ToLowerInvariant();
    }

    public static DocFacts Extract(string text)
    {
        text ??= "";
        var sents = Sentences(text);
        var parties = new Dictionary<string, DocParty>();
        DocParty? Party(string k)
        {
            if (Generic.Contains(k)) return null;
            if (!parties.TryGetValue(k, out var p))
                parties[k] = p = new DocParty { Key = k, IsPerson = Persons.Contains(k), IsSelf = k == "my vehicle" };
            return p;
        }

        // Unit definitions: "Unit 1: 2019 Ford F-150, White" / "Vehicle 2 - Gray Toyota Camry".
        foreach (Match m in Regex.Matches(text, @"^\s*(?:unit|vehicle|veh\.?)\s*#?\s*(\d{1,2})\s*[:\-]\s*([^\n.]{3,80})", I | RegexOptions.Multiline))
        {
            var p = Party("unit " + m.Groups[1].Value)!;
            string desc = m.Groups[2].Value;
            var col = Regex.Match(desc, @"\b(" + Colors + @")\b", I);
            var mk = Regex.Match(desc, @"\b(?:(?:19|20)\d{2}\s+)?((?:" + Makes + @")\s+[\w-]+)", I);
            var bits = new[] { col.Success ? col.Groups[1].Value.ToLowerInvariant() : null, mk.Success ? mk.Groups[1].Value : null }.Where(b => b != null);
            p.Description = string.Join(" ", bits);
            if (p.Description.Length == 0) p.Description = desc.Trim()[..Math.Min(40, desc.Trim().Length)];
        }

        // Contributing-factor blocks: "Contributing Factors: Unit 2 - A; B. Unit 1 - C."
        foreach (Match blk in Regex.Matches(text, @"contributing factors?\s*:\s*(.+?)(?:\n\s*\n|\n[A-Z][a-z]+\s*:|$)", I | RegexOptions.Singleline))
            foreach (Match seg in Regex.Matches(blk.Groups[1].Value, @"(?:unit|vehicle)\s*#?\s*(\d{1,2})\s*[-:]\s*([^\n]+?)(?=(?:unit|vehicle)\s*#?\s*\d|$)", I | RegexOptions.Singleline))
            {
                var p = Party("unit " + seg.Groups[1].Value)!;
                string list = seg.Groups[2].Value;
                bool possible = Regex.IsMatch(list, "possible|suspected", I);
                foreach (var (label, w, rx) in Factors)
                    if (rx.IsMatch(list)) p.Factors[label] = Math.Max(p.Factors.GetValueOrDefault(label), w * (possible ? 0.6 : 1.0));
                p.Evidence.Add("Listed contributing factors: " + list.Trim().TrimEnd('.', ';'));
            }

        string? lastSubject = null, lastVehicle = null;
        foreach (var s in sents)
        {
            if (Regex.IsMatch(s, @"^\s*contributing factors?\s*:", I)) continue;
            var ks = new List<(int A, int B, string K)>();
            foreach (Match m in Mention.Matches(s))
            {
                string k = KeyOf(m);
                int a = m.Index, b = m.Index + m.Length;
                if (Generic.Contains(k))
                {
                    // "Unit 2 driver" / "the driver of the Civic": the specific mention beside it wins.
                    if (ks.Count > 0 && a - ks[^1].B <= 2) continue;
                    if (Regex.IsMatch(s[b..Math.Min(s.Length, b + 5)], @"^\s+of\b")) continue;
                    if (lastVehicle != null) ks.Add((a, b, lastVehicle));
                }
                else if (Party(k) != null)
                {
                    ks.Add((a, b, k));
                    if (!Persons.Contains(k)) lastVehicle = k;
                }
            }
            string? Before(int pos) => ks.LastOrDefault(x => x.B <= pos).K;
            string? After(int pos) => ks.FirstOrDefault(x => x.A >= pos).K;
            void Keep(DocParty p) { if (!p.Evidence.Contains(s)) p.Evidence.Add(s); }
            string? subj = ks.Count > 0 ? ks[0].K : null;
            if (Pronoun.IsMatch(s)) subj = lastSubject;

            // Who hit whom.
            var pm = Passive.Match(s);
            if (pm.Success)
            {
                string? victim = Before(pm.Index) ?? subj, striker = After(pm.Index + pm.Length);
                if (striker != null && striker != victim)
                {
                    parties[striker].Striking++; Keep(parties[striker]);
                    if (victim != null && parties.TryGetValue(victim, out var v)) v.Struck++;
                }
            }
            else
            {
                var sm = Strike.Match(s);
                if (sm.Success)
                {
                    string? striker = Before(sm.Index) ?? (Regex.IsMatch(s, @"^(?:he|she|they|it|the driver)\b", I) ? lastSubject : null);
                    string? victim = After(sm.Index + sm.Length);
                    if (striker != null && striker != victim && parties.TryGetValue(striker, out var sp))
                    {
                        sp.Striking++;
                        if (sm.Groups[1].Value.StartsWith("rear", StringComparison.OrdinalIgnoreCase)) sp.RearEnded++;
                        Keep(sp);
                        if (victim != null) parties[victim].Struck++;
                    }
                }
            }

            // Behaviour and violations.
            foreach (var (label, w, rx) in Factors)
            {
                var fm = rx.Match(s);
                if (!fm.Success) continue;
                string? who = Before(fm.Index) ?? subj ?? lastSubject;
                if (who == null || !parties.TryGetValue(who, out var p)) continue;
                if (label != "fled the scene" && Regex.IsMatch(s, @"\b(?:not|no|never|wasn't|was not|did not)\s+(?:\w+\s+){0,2}(?:" + rx + ")", I)) continue;
                p.Factors[label] = Math.Max(p.Factors.GetValueOrDefault(label), w);
                Keep(p);
            }
            foreach (Match sp in MphOver.Matches(s))
            {
                string? who = Before(sp.Index) ?? subj;
                int v = int.Parse(sp.Groups[1].Value), lim = int.Parse(sp.Groups[2].Value);
                if (who != null && parties.TryGetValue(who, out var p) && v > lim)
                {
                    p.Factors["speeding"] = Math.Max(p.Factors.GetValueOrDefault("speeding"), v - lim >= 10 ? 0.5 : 0.3);
                    Keep(p);
                }
            }
            var cm = Cite.Match(s);
            if (cm.Success)
            {
                string? who = Before(cm.Index) ?? subj ?? lastSubject;
                if (who != null && parties.TryGetValue(who, out var p))
                {
                    string what = s[(cm.Index + cm.Length)..].Trim(' ', '.');
                    p.Citations.Add(what.Length == 0 ? "a violation" : what[..Math.Min(120, what.Length)]);
                    Keep(p);
                }
            }
            var nf = NotFaultRx.Match(s);
            if (nf.Success)
            {
                string? who = Before(nf.Index) ?? subj;
                if (who != null && parties.TryGetValue(who, out var p)) p.NotAtFault++;
            }
            else if (AtFaultRx.Match(s) is { Success: true } am)
            {
                string? who = Before(am.Index) ?? After(am.Index + am.Length) ?? subj;
                if (who != null && parties.TryGetValue(who, out var p)) { p.AtFault++; Keep(p); }
            }
            if (subj != null) lastSubject = subj;
        }

        // "the Civic" and "blue Honda Civic" are the same car.
        foreach (var k in parties.Keys.OrderBy(k => k.Length).ToList())
        {
            if (k.StartsWith("unit ") || !parties.ContainsKey(k)) continue;
            var toks = k.Split(' ').ToHashSet();
            if (toks.All(ColorWords.Contains)) continue;
            var bigger = parties.Keys.Where(o => o != k && !o.StartsWith("unit ") && toks.IsProperSubsetOf(o.Split(' '))).ToList();
            if (bigger.Count != 1) continue;
            var a = parties[k]; var b = parties[bigger[0]];
            parties.Remove(k);
            foreach (var (f, w) in a.Factors) b.Factors[f] = Math.Max(b.Factors.GetValueOrDefault(f), w);
            b.Citations.AddRange(a.Citations); b.AtFault += a.AtFault; b.NotAtFault += a.NotAtFault; b.Striking += a.Striking;
            b.Struck += a.Struck; b.RearEnded += a.RearEnded; b.IsSelf |= a.IsSelf;
            b.Evidence.AddRange(a.Evidence.Where(e => !b.Evidence.Contains(e)));
        }

        var facts = new DocFacts();
        foreach (var p in parties.Values)
        {
            if (p.Factors.Count == 0 && p.Citations.Count == 0 && p.AtFault == 0 && p.Striking == 0 && p.Struck == 0 && !p.Key.StartsWith("unit ") && !p.IsSelf) continue;
            if (p.Description.Length == 0 && !p.Key.StartsWith("unit ")) p.Description = p.Key;
            p.Label = p.Key.StartsWith("unit ") && p.Description.Length > 0 ? $"{Cap(p.Key)} ({p.Description})" : Cap(p.Key);
            if (p.Evidence.Count > 4) p.Evidence = p.Evidence.Take(4).ToList();
            facts.Parties.Add(p);
        }
        foreach (var rx in Locs)
            foreach (Match m in rx.Matches(text))
            {
                string v = string.Join(" and ", m.Groups.Cast<Group>().Skip(1).Where(g => g.Success && g.Value.Length > 0).Select(g => g.Value.Trim(' ', '.', ',')));
                v = Regex.Replace(v, @"\s+", " ");
                if (v.Length > 3 && !facts.Locations.Any(l => l.Contains(v, StringComparison.OrdinalIgnoreCase)) && facts.Locations.Count < 6)
                    facts.Locations.Add(v);
            }
        facts.Dates = DateRx.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
        facts.Times = TimeRx.Matches(text).Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
        facts.Conditions = CondRx.Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();
        facts.Injuries = sents.Where(x => x.Length < 240 && InjRx.IsMatch(x)).Distinct().Take(6).ToList();
        facts.HitAndRun = Regex.IsMatch(text, @"hit[- ]and[- ]run|fled the scene|left the scene", I);
        facts.Kind = Regex.IsMatch(text, @"crash report|accident report|incident report|report number|case (?:no|number)|contributing factors|narrative\s*:", I) ? "police report"
            : Regex.IsMatch(text, @"\bpolice said|according to|told reporters|\breported\b", I) ? "news article" : "document";
        return facts;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
