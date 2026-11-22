using System.Text.RegularExpressions;

namespace Guardrail.CrashAi.Documents;

/// <summary>An attached document after reading: its title, what kind it is and the facts found in it.</summary>
public sealed record AttachedFacts(string Title, DocFacts Facts);

/// <summary>One party's share of the fault in the combined (video + documents) assessment.</summary>
public sealed class CombinedShare
{
    public string Party { get; set; } = "";
    public int Percent { get; set; }
    public int? VideoPercent { get; set; }
    public int? DocumentPercent { get; set; }
    public List<string> Reasons { get; set; } = [];
    public bool IsCamera { get; set; }
}

/// <summary>What the video and the attached documents add up to.</summary>
public sealed class CaseResult
{
    public string Headline { get; set; } = "";
    public List<CombinedShare> Shares { get; set; } = [];
    public List<string> Where { get; set; } = [];
    public List<string> When { get; set; } = [];
    public List<string> Conditions { get; set; } = [];
    public List<string> Injuries { get; set; } = [];
    /// <summary>Plain-language notes on how the documents changed (or confirmed) the video's call.</summary>
    public List<string> Notes { get; set; } = [];
    /// <summary>Each document party and which road user in the video it was matched to.</summary>
    public List<(string DocParty, string Matched)> Matches { get; set; } = [];
    public bool UsedDocuments { get; set; }
}

/// <summary>
/// Combines the video analysis with facts from attached documents. Officer findings (citations, contributing factors,
/// "at fault") weigh heavily; news stories less. Re-running this is instant — the video isn't analyzed again.
/// </summary>
public static class CaseAssessment
{
    /// <param name="myParty">Key of the document party that is the camera vehicle ("unit 1"), "" for automatic, "none" when it isn't mentioned.</param>
    public static CaseResult Combine(CrashReport video, IReadOnlyList<AttachedFacts> docs, string myParty = "")
    {
        var r = new CaseResult();
        bool hasVideoCrash = video.Verdict != Verdicts.NoCollision && video.Shares.Count > 0;

        // ----- video side -----
        var parties = new List<CombinedShare>();
        foreach (var s in video.Shares)
            parties.Add(new CombinedShare { Party = s.Party, VideoPercent = s.Percent, IsCamera = s.IsCamera, Reasons = s.Reasons.ToList() });

        // ----- documents side -----
        var docScore = new Dictionary<CombinedShare, double>();
        double docStrength = 0;
        foreach (var (title, f) in docs)
        {
            var scored = f.Parties.Where(p => !IsBystander(p)).ToList();
            double total = scored.Sum(p => p.Score);
            foreach (var p in f.Parties)
            {
                var target = Match(p, parties, myParty, video, out bool isNew);
                if (isNew) parties.Add(target);
                r.Matches.Add((p.Label, target.Party));
                if (total >= 0.3 && !IsBystander(p))
                    docScore[target] = docScore.GetValueOrDefault(target) + f.Weight * p.Score / total;
                foreach (var c in p.Citations) target.Reasons.Add($"{Cap(f.Kind)}: cited {Trim(c)}");
                foreach (var (k, w) in p.Factors.OrderByDescending(kv => kv.Value))
                    target.Reasons.Add($"{Cap(f.Kind)}: {k}{(w < 0.45 ? " (possible)" : "")}");
                if (p.AtFault > 0) target.Reasons.Add($"{Cap(f.Kind)} says this party was at fault");
                if (p.NotAtFault > 0) target.Reasons.Add($"{Cap(f.Kind)} says this party was not at fault");
                if (p.RearEnded > 0) target.Reasons.Add($"{Cap(f.Kind)}: rear-ended another vehicle");
            }
            if (total >= 0.3) docStrength = Math.Max(docStrength, f.Weight * Math.Min(1, total / 1.5));
            r.Where.AddRange(f.Locations.Where(l => !r.Where.Contains(l, StringComparer.OrdinalIgnoreCase)));
            r.When.AddRange(f.Dates.Concat(f.Times).Where(x => !r.When.Contains(x)));
            r.Conditions.AddRange(f.Conditions.Where(x => !r.Conditions.Contains(x)));
            r.Injuries.AddRange(f.Injuries.Where(x => !r.Injuries.Contains(x)));
            if (f.HitAndRun && !r.Notes.Any(n => n.Contains("hit-and-run"))) r.Notes.Add($"The {f.Kind} describes a hit-and-run.");
        }
        if (!string.IsNullOrWhiteSpace(video.Location) && hasVideoCrash) r.Where.Insert(0, "Video: " + video.Location);
        r.UsedDocuments = docScore.Count > 0;

        // ----- combine -----
        double sumDoc = docScore.Values.Sum();
        double wv = hasVideoCrash ? Math.Max(0.3, video.Confidence / 100.0) : 0;
        double wd = sumDoc > 0 ? docStrength : 0;
        foreach (var p in parties)
        {
            if (sumDoc > 0) p.DocumentPercent = (int)Math.Round(100 * docScore.GetValueOrDefault(p) / sumDoc);
            double v = (p.VideoPercent ?? 0) / 100.0, d = (p.DocumentPercent ?? 0) / 100.0;
            double mix = wv + wd > 0 ? (wv * v + wd * d) / (wv + wd) : 0;
            p.Percent = (int)Math.Round(100 * mix);
        }
        // Rounding: make the shares add up to 100.
        int sum = parties.Sum(p => p.Percent);
        if (sum > 0 && sum != 100) parties.MaxBy(p => p.Percent)!.Percent += 100 - sum;
        r.Shares = parties.Where(p => p.Percent > 0 || p.VideoPercent > 0 || p.DocumentPercent > 0 || p.IsCamera)
            .OrderByDescending(p => p.Percent).ToList();
        foreach (var p in r.Shares) p.Reasons = p.Reasons.Distinct().ToList();

        // ----- notes and headline -----
        if (r.UsedDocuments && hasVideoCrash)
        {
            var vTop = parties.Where(p => p.VideoPercent != null).MaxBy(p => p.VideoPercent);
            var dTop = parties.Where(p => p.DocumentPercent != null).MaxBy(p => p.DocumentPercent);
            if (vTop != null && dTop != null)
                r.Notes.Insert(0, vTop == dTop
                    ? $"The documents agree with the video: {Who(dTop)} carries most of the fault."
                    : $"The documents point at {Who(dTop)}, while the video alone pointed at {Who(vTop)}. Official findings in the documents weigh heavily, so the combined call leans their way.");
        }
        else if (r.UsedDocuments)
            r.Notes.Insert(0, "The video didn't show the collision clearly, so the call is based on the documents.");
        else if (docs.Count > 0)
            r.Notes.Add("The attached documents didn't say who caused the crash, so the call is based on the video.");

        var top = r.Shares.FirstOrDefault();
        r.Headline = top == null || top.Percent == 0 ? (hasVideoCrash ? video.Headline : "No collision found")
            : top.Percent >= 60 ? $"{CapFirst(Who(top))} was most likely at fault ({top.Percent}%)"
            : "Fault looks shared: " + string.Join(", ", r.Shares.Where(s => s.Percent > 0).Take(3).Select(s => $"{Who(s)} {s.Percent}%"));
        return r;
    }

    private static string Who(CombinedShare s) => s.IsCamera ? "the camera vehicle" : s.Party;

    private static bool IsBystander(DocParty p) => p.Key is "trooper" or "deputy" or "officer" && p.Factors.Count == 0;

    /// <summary>Finds the video party a document party refers to, or makes a new document-only party.</summary>
    private static CombinedShare Match(DocParty p, List<CombinedShare> parties, string myParty, CrashReport video, out bool isNew)
    {
        isNew = false;
        var camera = parties.FirstOrDefault(x => x.IsCamera);
        bool mine = myParty.Length > 0 && myParty != "none" ? string.Equals(p.Key, myParty, StringComparison.OrdinalIgnoreCase) : p.IsSelf;
        if (mine)
        {
            if (camera != null) return camera;
            isNew = true;
            return new CombinedShare { Party = "Camera vehicle", IsCamera = true };
        }
        // Colour and type words against the road users seen in the video.
        var words = Words(p.Description + " " + p.Key);
        CombinedShare? best = null; int bestScore = 0;
        foreach (var x in parties.Where(x => !x.IsCamera))
        {
            var user = video.RoadUsers.FirstOrDefault(u => x.Party.StartsWith(u.Tag + " ", StringComparison.Ordinal));
            var vw = Words(user?.Label ?? x.Party);
            int score = 0;
            if (words.Colors.Overlaps(vw.Colors)) score += 2;
            else if (words.Colors.Count > 0 && vw.Colors.Count > 0) score -= 2;
            if (words.Types.Overlaps(vw.Types)) score += 1;
            if (score > bestScore) { best = x; bestScore = score; }
        }
        if (best != null && bestScore >= 2) return best;
        var existing = parties.FirstOrDefault(x => string.Equals(x.Party, p.Label, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        isNew = true;
        return new CombinedShare { Party = p.Label };
    }

    private static readonly Dictionary<string, string> TypeOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["car"] = "car", ["sedan"] = "car", ["coupe"] = "car", ["hatchback"] = "car", ["wagon"] = "car", ["convertible"] = "car", ["charger"] = "car",
        ["mustang"] = "car", ["civic"] = "car", ["camry"] = "car", ["corolla"] = "car", ["accord"] = "car", ["altima"] = "car", ["prius"] = "car",
        ["clio"] = "car", ["soul"] = "car", ["suv"] = "car", ["minivan"] = "car", ["van"] = "car", ["jeep"] = "car", ["tahoe"] = "car", ["explorer"] = "car",
        ["truck"] = "truck", ["pickup"] = "truck", ["semi"] = "truck", ["semi-truck"] = "truck", ["tractor-trailer"] = "truck", ["f-150"] = "truck", ["silverado"] = "truck",
        ["bus"] = "bus", ["motorcycle"] = "motorcycle", ["motorbike"] = "motorcycle", ["scooter"] = "motorcycle", ["e-scooter"] = "motorcycle", ["moped"] = "motorcycle",
        ["motorcyclist"] = "motorcycle", ["bicycle"] = "bicycle", ["bike"] = "bicycle", ["cyclist"] = "bicycle", ["bicyclist"] = "bicycle",
        ["pedestrian"] = "pedestrian", ["patrol"] = "car", ["cruiser"] = "car",
    };
    private static readonly Dictionary<string, string> ColorOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "white", ["black"] = "black", ["silver"] = "silver", ["gray"] = "gray", ["grey"] = "gray", ["red"] = "red", ["maroon"] = "red",
        ["blue"] = "blue", ["green"] = "green", ["yellow"] = "yellow", ["gold"] = "yellow", ["orange"] = "orange", ["brown"] = "brown", ["tan"] = "brown",
        ["beige"] = "brown", ["purple"] = "purple",
    };

    private static (HashSet<string> Colors, HashSet<string> Types) Words(string s)
    {
        var colors = new HashSet<string>(); var types = new HashSet<string>();
        foreach (var w in Regex.Split(s.ToLowerInvariant(), @"[^a-z0-9-]+"))
        {
            if (ColorOf.TryGetValue(w, out var c)) colors.Add(c);
            if (TypeOf.TryGetValue(w, out var t)) types.Add(t);
        }
        // Video colour names are approximate: silver and gray are hard to tell apart, as are black and dark gray.
        if (colors.Contains("silver")) colors.Add("gray");
        if (colors.Contains("gray")) colors.Add("silver");
        return (colors, types);
    }

    private static string Trim(string s) => s.Length > 90 ? s[..90] + "…" : s;
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string CapFirst(string s) => Cap(s);
}
