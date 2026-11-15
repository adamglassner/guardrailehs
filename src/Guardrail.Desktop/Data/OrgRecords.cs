using System.Text.Json.Serialization;

namespace Guardrail.Data;

/// <summary>A site in the corporate → region → district → location hierarchy.</summary>
public sealed class Location : RecordBase
{
    public static readonly string[] Types = ["Plant", "Warehouse", "Office", "Terminal / yard", "Shop", "Job site", "Store", "Other"];
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly string[] Statuses = [Active, Inactive];

    public string Name { get; set; } = "";
    /// <summary>Short code used on reports, e.g. "DAL-01".</summary>
    public string Code { get; set; } = "";
    public string Type { get; set; } = "Plant";
    public string Region { get; set; } = "";
    public string District { get; set; } = "";
    public string Address { get; set; } = "";
    public string CityState { get; set; } = "";
    public string Manager { get; set; } = "";
    public string Phone { get; set; } = "";
    public decimal? Employees { get; set; }
    public string Status { get; set; } = Active;
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "LOC";
    [JsonIgnore] public bool IsActive => Status == Active;
    [JsonIgnore] public string DisplayStatus => Status;
    /// <summary>"South › Dallas" (whatever parts are filled in).</summary>
    [JsonIgnore] public string Hierarchy => string.Join(" › ", new[] { Region, District }.Where(s => s.Length > 0));
    [JsonIgnore] public string EmployeesText => Employees is { } e ? e.ToString("N0") : "—";

    /// <summary>
    /// How many records across Guardrail use this location's name (so renaming can carry them along).
    /// </summary>
    [JsonIgnore] public int UsageCount => LocationNames.Usage(Name);
}

/// <summary>Helpers for location names used across every module.</summary>
public static class LocationNames
{
    /// <summary>Active location names, for suggestions in every Location field.</summary>
    public static string[] ActiveNames() =>
        DataStore.Locations.Items.Where(l => l.IsActive).Select(l => l.Name).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Every record type with a Location, as (store items, getter, setter).</summary>
    private static IEnumerable<(IEnumerable<RecordBase> Items, Func<RecordBase, string> Get, Action<RecordBase, string> Set, Action<RecordBase> Save)> Fields()
    {
        yield return (DataStore.Actions.Items, r => ((ActionItem)r).Location, (r, v) => ((ActionItem)r).Location = v, r => DataStore.Actions.Update((ActionItem)r));
        yield return (DataStore.Observations.Items, r => ((Observation)r).Location, (r, v) => ((Observation)r).Location = v, r => DataStore.Observations.Update((Observation)r));
        yield return (DataStore.Compliance.Items, r => ((ComplianceItem)r).Location, (r, v) => ((ComplianceItem)r).Location = v, r => DataStore.Compliance.Update((ComplianceItem)r));
        yield return (DataStore.Claims.Items, r => ((Claim)r).Location, (r, v) => ((Claim)r).Location = v, r => DataStore.Claims.Update((Claim)r));
        yield return (DataStore.Monthly.Items, r => ((MonthlyEntry)r).Location, (r, v) => ((MonthlyEntry)r).Location = v, r => DataStore.Monthly.Update((MonthlyEntry)r));
        yield return (DataStore.Permits.Items, r => ((WorkPermit)r).Location, (r, v) => ((WorkPermit)r).Location = v, r => DataStore.Permits.Update((WorkPermit)r));
        yield return (DataStore.Loto.Items, r => ((LotoProcedure)r).Location, (r, v) => ((LotoProcedure)r).Location = v, r => DataStore.Loto.Update((LotoProcedure)r));
        yield return (DataStore.Sds.Items, r => ((SdsEntry)r).Location, (r, v) => ((SdsEntry)r).Location = v, r => DataStore.Sds.Update((SdsEntry)r));
        yield return (DataStore.Incidents.Items, r => ((Incident)r).Location, (r, v) => ((Incident)r).Location = v, r => DataStore.Incidents.Update((Incident)r));
        yield return (DataStore.Inspections.Items, r => ((Inspection)r).Location, (r, v) => ((Inspection)r).Location = v, r => DataStore.Inspections.Update((Inspection)r));
        yield return (DataStore.Risks.Items, r => ((RiskAssessment)r).Location, (r, v) => ((RiskAssessment)r).Location = v, r => DataStore.Risks.Update((RiskAssessment)r));
        yield return (DataStore.Changes.Items, r => ((ChangeRequest)r).Location, (r, v) => ((ChangeRequest)r).Location = v, r => DataStore.Changes.Update((ChangeRequest)r));
        yield return (DataStore.Drivers.Items, r => ((Driver)r).Location, (r, v) => ((Driver)r).Location = v, r => DataStore.Drivers.Update((Driver)r));
        yield return (DataStore.Vehicles.Items, r => ((Vehicle)r).Location, (r, v) => ((Vehicle)r).Location = v, r => DataStore.Vehicles.Update((Vehicle)r));
        yield return (DataStore.FleetAccidents.Items, r => ((FleetAccident)r).Location, (r, v) => ((FleetAccident)r).Location = v, r => DataStore.FleetAccidents.Update((FleetAccident)r));
    }

    public static int Usage(string name) =>
        name.Length == 0 ? 0 : Fields().Sum(f => f.Items.Count(r => string.Equals(f.Get(r), name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Renames a location everywhere it's used. Returns how many records changed.</summary>
    public static int Rename(string from, string to)
    {
        if (from.Length == 0 || string.Equals(from, to, StringComparison.Ordinal)) return 0;
        int changed = 0;
        foreach (var f in Fields())
            foreach (var r in f.Items.Where(r => string.Equals(f.Get(r), from, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                f.Set(r, to);
                f.Save(r);
                changed++;
            }
        return changed;
    }

    /// <summary>Location names already typed into records that aren't set up yet (offered as one-click imports).</summary>
    public static string[] UnlistedNamesInUse()
    {
        var known = new HashSet<string>(DataStore.Locations.Items.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        return Fields().SelectMany(f => f.Items.Select(f.Get)).Select(n => n.Trim())
            .Where(n => n.Length > 0 && !known.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
