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
    public static readonly JsonStore<MonthlyEntry> Monthly = new(Folder, "monthly");
    public static readonly JsonStore<BbsObservation> Bbs = new(Folder, "bbs");
    public static readonly JsonStore<WorkPermit> Permits = new(Folder, "permits");
    public static readonly JsonStore<LotoProcedure> Loto = new(Folder, "loto");
    public static readonly JsonStore<SdsEntry> Sds = new(Folder, "sds");
    public static readonly JsonStore<Incident> Incidents = new(Folder, "incidents");
    public static readonly JsonStore<InspectionTemplate> Templates = new(Folder, "inspection-templates");
    public static readonly JsonStore<Inspection> Inspections = new(Folder, "inspections");
    public static readonly JsonStore<RiskAssessment> Risks = new(Folder, "risk-assessments");
    public static readonly JsonStore<ChangeRequest> Changes = new(Folder, "moc");
    public static readonly JsonStore<Driver> Drivers = new(Folder, "fleet-drivers");
    public static readonly JsonStore<Vehicle> Vehicles = new(Folder, "fleet-vehicles");
    public static readonly JsonStore<Dvir> Dvirs = new(Folder, "fleet-dvirs");
    public static readonly JsonStore<MaintenanceOrder> Maintenance = new(Folder, "fleet-maintenance");
    public static readonly JsonStore<HosLog> Hos = new(Folder, "fleet-hos");
    public static readonly JsonStore<FleetAccident> FleetAccidents = new(Folder, "fleet-accidents");

    /// <summary>Where Library files are kept: data\library\&lt;document id&gt;\&lt;file&gt;.</summary>
    public static string LibraryFolder => Path.Combine(Folder, "library");
}
