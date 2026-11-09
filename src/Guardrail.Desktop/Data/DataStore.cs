using System.IO;

namespace Guardrail.Data;

/// <summary>
/// Local, on-this-PC storage for the modules that work today.
/// When Guardrail Server exists, these stores become the offline cache that syncs to it.
/// </summary>
public static class DataStore
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Guardrail", "data");

    public static readonly JsonStore<ActionItem> Actions = new(Folder, "actions");
    public static readonly JsonStore<Observation> Observations = new(Folder, "observations");
    public static readonly JsonStore<TrainingRecord> Training = new(Folder, "training");
    public static readonly JsonStore<ComplianceItem> Compliance = new(Folder, "compliance");
    public static readonly JsonStore<Claim> Claims = new(Folder, "claims");
    public static readonly JsonStore<LibraryDocument> Library = new(Folder, "library");

    /// <summary>Where Library files are kept: data\library\&lt;document id&gt;\&lt;file&gt;.</summary>
    public static string LibraryFolder => Path.Combine(Folder, "library");
}
