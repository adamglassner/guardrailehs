using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Guardrail.Services;

/// <summary>One line of the ERG's yellow/blue index: ID number → guide → name.</summary>
public sealed record ErgMaterial(string Id, string Guide, string Name)
{
    /// <summary>Guide number without the "P" (polymerization) suffix, e.g. "116P" → "116".</summary>
    public string GuideNumber => Guide.TrimEnd('P');
    public bool MayPolymerize => Guide.EndsWith('P');
    public string IdText => Id.Length > 0 ? Id : "—";
}

/// <summary>A Table 1 (green pages) row: initial isolation and protective action distances.</summary>
public sealed record ErgDistances(string Id, string Name, string[] Small, string[] Large)
{
    /// <summary>Large spills of six common gases (ammonia, chlorine, …) use Table 3 instead.</summary>
    public bool LargeUsesTable3 => Large.Length == 1 && Large[0] == "Table 3";
}

/// <summary>A Table 2 row: material that gives off toxic gas when spilled in water.</summary>
public sealed record ErgWaterReactive(string Id, string Name, string Gases);

/// <summary>One orange-page guide: line kinds are H (section), S (heading), s (sub-heading), B (bullet), T (text).</summary>
public sealed record ErgGuide(string Number, string Title, List<(char Kind, string Text)> Lines)
{
    public bool IsBlank => Lines.Count == 0 || Lines.Any(l => l.Text.Contains("left blank"));
}

/// <summary>
/// Offline copy of the 2024 Emergency Response Guidebook (PHMSA / Transport Canada / SCT):
/// the material index, all orange guides, Table 1 distances and Table 2 water-reactive materials.
/// Built from the published PDF by tools/erg/parse.py and embedded in the app.
/// </summary>
public static class ErgData
{
    public const string Edition = "ERG 2024";

    private static readonly Lazy<Data> Loaded = new(Load);

    public static IReadOnlyList<ErgMaterial> Materials => Loaded.Value.Materials;
    public static IReadOnlyDictionary<string, ErgGuide> Guides => Loaded.Value.Guides;
    public static IReadOnlyList<string> GeneralFirstAid => Loaded.Value.FirstAid;

    /// <summary>Table 1 rows for an ID number (empty when the material isn't toxic by inhalation).</summary>
    public static IReadOnlyList<ErgDistances> DistancesFor(string id) =>
        Loaded.Value.Table1.TryGetValue(id, out var rows) ? rows : [];

    /// <summary>Table 2 rows for an ID number (empty when not water-reactive).</summary>
    public static IReadOnlyList<ErgWaterReactive> WaterReactiveFor(string id) =>
        Loaded.Value.Table2.TryGetValue(id, out var rows) ? rows : [];

    public static bool IsHighlighted(string id) => id.Length > 0 && Loaded.Value.Table1.ContainsKey(id);

    public static ErgGuide? Guide(string number) => Guides.TryGetValue(number.TrimEnd('P'), out var g) ? g : null;

    /// <summary>
    /// Finds materials by ID number ("1203", "UN1203", "NA 1993") or by name words ("propane", "sulfuric acid").
    /// ID matches come first, then names that start with the text, then names that contain every word.
    /// </summary>
    public static List<ErgMaterial> Search(string query, int max = 200)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var id = Regex.Match(query, @"^(?:UN|NA|ID)?\s*(\d{4})$", RegexOptions.IgnoreCase);
        if (id.Success)
            return Materials.Where(m => m.Id == id.Groups[1].Value).Take(max).ToList();

        var words = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return Materials
            .Select(m => (m, name: m.Name.ToLowerInvariant()))
            .Where(x => words.All(w => x.name.Contains(w)))
            .OrderBy(x => x.name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : x.name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.name.Length)
            .Select(x => x.m)
            .Take(max)
            .ToList();
    }

    // ----- Loading -----

    private sealed record Data(
        List<ErgMaterial> Materials,
        Dictionary<string, ErgGuide> Guides,
        Dictionary<string, List<ErgDistances>> Table1,
        Dictionary<string, List<ErgWaterReactive>> Table2,
        List<string> FirstAid);

    private static Data Load()
    {
        using var raw = typeof(ErgData).Assembly.GetManifestResourceStream("erg2024.json.gz")
            ?? throw new InvalidOperationException("ERG data isn't included in this build.");
        using var gz = new GZipStream(raw, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        var root = doc.RootElement;

        var materials = root.GetProperty("materials").EnumerateArray()
            .Select(a => new ErgMaterial(a[0].GetString()!, a[1].GetString()!, a[2].GetString()!)).ToList();

        var guides = new Dictionary<string, ErgGuide>();
        foreach (var g in root.GetProperty("guides").EnumerateArray())
        {
            var lines = g.GetProperty("lines").EnumerateArray()
                .Select(l => (l[0].GetString()![0], l[1].GetString()!)).ToList();
            string num = g.GetProperty("num").GetString()!;
            guides[num] = new ErgGuide(num, g.GetProperty("title").GetString()!, lines);
        }

        var table1 = new Dictionary<string, List<ErgDistances>>();
        foreach (var t in root.GetProperty("table1").EnumerateArray())
        {
            var row = new ErgDistances(t.GetProperty("id").GetString()!, t.GetProperty("n").GetString()!,
                t.GetProperty("s").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                t.GetProperty("l").EnumerateArray().Select(x => x.GetString()!).ToArray());
            if (!table1.TryGetValue(row.Id, out var list)) table1[row.Id] = list = [];
            list.Add(row);
        }

        var table2 = new Dictionary<string, List<ErgWaterReactive>>();
        foreach (var t in root.GetProperty("table2").EnumerateArray())
        {
            var row = new ErgWaterReactive(t.GetProperty("id").GetString()!, t.GetProperty("n").GetString()!, t.GetProperty("gas").GetString()!);
            if (!table2.TryGetValue(row.Id, out var list)) table2[row.Id] = list = [];
            list.Add(row);
        }

        var firstAid = root.GetProperty("firstAid").EnumerateArray().Select(x => x.GetString()!).ToList();
        return new Data(materials, guides, table1, table2, firstAid);
    }
}
