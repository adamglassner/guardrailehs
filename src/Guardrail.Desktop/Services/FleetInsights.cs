using Guardrail.Data;

namespace Guardrail.Services;

/// <summary>Something in the fleet that needs attention. <see cref="Severity"/> is "Overdue" or "Due soon".</summary>
public sealed record FleetAlert(string Area, string SectionKey, string Item, string Problem, DateTime? Due, string Severity)
{
    public string DueText => Due?.ToString("MMM d, yyyy") ?? "—";
}

public sealed record AuditGap(string Record, string Problem);

/// <summary>One DOT requirement checked across the records.</summary>
public sealed record AuditCheck(string Requirement, string Rule, string SectionKey, int Checked, List<AuditGap> Gaps)
{
    public string Status => Gaps.Count == 0 ? FleetStatus.Pass : FleetStatus.Gaps;
    public string ResultText => Checked == 0 ? "Nothing to check yet" : Gaps.Count == 0 ? $"All {Checked} OK" : $"{Gaps.Count} of {Checked} need attention";
    public string GapsText => string.Join("; ", Gaps.Take(4).Select(g => $"{g.Record}: {g.Problem}")) + (Gaps.Count > 4 ? $" (+{Gaps.Count - 4} more)" : "");
}

/// <summary>Cross-record fleet checks for the Fleet Hub overview, the DOT mock audit and fleet reports.</summary>
public static class FleetInsights
{
    private const string Overdue = "Overdue";
    private const string DueSoon = "Due soon";

    public static List<FleetAlert> Alerts()
    {
        var today = DateTime.Today;
        var alerts = new List<FleetAlert>();

        foreach (var d in DataStore.Drivers.Items.Where(d => d.IsActive))
        {
            foreach (var p in d.Problems) alerts.Add(new("Driver", "fleet-dq", d.Name, p, null, Overdue));
            foreach (var (what, when) in d.ComingUp) alerts.Add(new("Driver", "fleet-dq", d.Name, what, when, DueSoon));
        }

        foreach (var v in DataStore.Vehicles.Items.Where(v => v.ServiceStatus == FleetStatus.InService))
        {
            var unit = $"Unit {v.UnitNumber}";
            if (v.RegistrationExpires is { } reg)
            {
                if (reg.Date < today) alerts.Add(new("Vehicle", "fleet-vehicles", unit, "Registration expired", reg, Overdue));
                else if (reg.Date <= today.AddDays(Vehicle.DueSoonDays)) alerts.Add(new("Vehicle", "fleet-vehicles", unit, "Registration expires", reg, DueSoon));
            }
            if (v.AnnualDue is null) alerts.Add(new("Vehicle", "fleet-vehicles", unit, "No annual DOT inspection on file", null, Overdue));
            else if (v.AnnualDue.Value < today) alerts.Add(new("Vehicle", "fleet-vehicles", unit, "Annual DOT inspection overdue", v.AnnualDue, Overdue));
            else if (v.AnnualDue.Value <= today.AddDays(Vehicle.DueSoonDays)) alerts.Add(new("Vehicle", "fleet-vehicles", unit, "Annual DOT inspection due", v.AnnualDue, DueSoon));
        }

        foreach (var r in DataStore.Dvirs.Items.Where(r => r.DefectsOpen))
            alerts.Add(new("DVIR", "fleet-dvir", $"Unit {r.UnitNumber}", $"Defects not repaired ({r.Number}): {Short(r.Defects)}", r.Date, Overdue));

        foreach (var w in DataStore.Maintenance.Items.Where(w => !w.IsComplete))
        {
            if (w.DueDate.Date < today) alerts.Add(new("Maintenance", "fleet-maintenance", $"Unit {w.UnitNumber}", $"{w.Service} overdue ({w.Number})", w.DueDate, Overdue));
            else if (w.DueDate.Date <= today.AddDays(MaintenanceOrder.DueSoonDays)) alerts.Add(new("Maintenance", "fleet-maintenance", $"Unit {w.UnitNumber}", $"{w.Service} due ({w.Number})", w.DueDate, DueSoon));
        }

        foreach (var a in DataStore.FleetAccidents.Items.Where(a => a.Preventability == FleetStatus.PendingReview))
            alerts.Add(new("Accident", "fleet-accidents", a.Number, $"Preventability review pending: {a.Driver}, {a.DateText}", a.Date, Overdue));

        foreach (var h in DataStore.Hos.Items.Where(h => h.HasViolation && h.Date >= today.AddDays(-30)))
            alerts.Add(new("Hours of service", "fleet-hos", h.Driver, $"{h.Violation} violation on {h.DateText}", h.Date, Overdue));

        return alerts.OrderBy(a => a.Severity == Overdue ? 0 : 1).ThenBy(a => a.Due ?? DateTime.MinValue).ToList();
    }

    public static List<AuditCheck> DotAudit()
    {
        var today = DateTime.Today;
        var drivers = DataStore.Drivers.Items.Where(d => d.IsActive).ToList();
        var vehicles = DataStore.Vehicles.Items.Where(v => v.ServiceStatus == FleetStatus.InService).ToList();
        var checks = new List<AuditCheck>();

        List<AuditGap> DriverGaps(Func<Driver, string?> problem) =>
            drivers.Select(d => (d, p: problem(d))).Where(x => x.p != null).Select(x => new AuditGap(x.d.Name, x.p!)).ToList();

        checks.Add(new("Every driver has a valid license", "49 CFR 391.11, 391.51", "fleet-dq", drivers.Count,
            DriverGaps(d => d.LicenseExpires is null ? "No license expiration on file" : d.LicenseExpires.Value.Date < today ? $"Expired {d.LicenseExpires:MMM d, yyyy}" : null)));
        checks.Add(new("Every driver has a current medical certificate", "49 CFR 391.41, 391.43", "fleet-dq", drivers.Count,
            DriverGaps(d => d.MedicalCardExpires is null ? "No medical certificate on file" : d.MedicalCardExpires.Value.Date < today ? $"Expired {d.MedicalCardExpires:MMM d, yyyy}" : null)));
        checks.Add(new("Annual review of each driver's driving record (MVR)", "49 CFR 391.25", "fleet-dq", drivers.Count,
            DriverGaps(d => d.MvrDue is null ? "No MVR review on file" : d.MvrDue.Value < today ? $"Overdue since {d.MvrDue:MMM d, yyyy}" : null)));
        checks.Add(new("Road test or equivalent on file", "49 CFR 391.31", "fleet-dq", drivers.Count,
            DriverGaps(d => d.RoadTest == "Yes" ? null : "No road test on file")));

        checks.Add(new("Annual inspection of every vehicle in service", "49 CFR 396.17", "fleet-vehicles", vehicles.Count,
            vehicles.Where(v => v.AnnualDue is null || v.AnnualDue.Value < today)
                .Select(v => new AuditGap($"Unit {v.UnitNumber}", v.AnnualDue is null ? "No annual inspection on file" : $"Overdue since {v.AnnualDue:MMM d, yyyy}")).ToList()));
        checks.Add(new("Registrations current", "State registration", "fleet-vehicles", vehicles.Count,
            vehicles.Where(v => v.RegistrationExpires is { } r && r.Date < today)
                .Select(v => new AuditGap($"Unit {v.UnitNumber}", $"Registration expired {v.RegistrationExpires:MMM d, yyyy}")).ToList()));

        var dvirs = DataStore.Dvirs.Items;
        checks.Add(new("DVIR defects repaired and certified", "49 CFR 396.11", "fleet-dvir", dvirs.Count(r => r.DefectsFound),
            dvirs.Where(r => r.DefectsOpen).Select(r => new AuditGap($"{r.Number} (unit {r.UnitNumber})", $"Not repaired: {Short(r.Defects)}")).ToList()));

        var maintenance = DataStore.Maintenance.Items;
        checks.Add(new("Scheduled maintenance done on time", "49 CFR 396.3", "fleet-maintenance", maintenance.Count,
            maintenance.Where(w => !w.IsComplete && w.DueDate.Date < today)
                .Select(w => new AuditGap($"{w.Number} (unit {w.UnitNumber})", $"{w.Service} overdue since {w.DueText}")).ToList()));

        var accidents = DataStore.FleetAccidents.Items;
        checks.Add(new("Accident register complete and reviewed", "49 CFR 390.15", "fleet-accidents", accidents.Count,
            accidents.Where(a => a.Preventability == FleetStatus.PendingReview)
                .Select(a => new AuditGap(a.Number, $"Preventability review pending ({a.DateText})")).ToList()));

        var recentLogs = DataStore.Hos.Items.Where(h => h.Date >= today.AddDays(-30)).ToList();
        checks.Add(new("Hours-of-service logs reviewed, no recent violations", "49 CFR 395", "fleet-hos", recentLogs.Count,
            recentLogs.Where(h => h.HasViolation).Select(h => new AuditGap(h.Driver, $"{h.Violation} on {h.DateText}")).ToList()));

        return checks;
    }

    /// <summary>Share of checks with something to check that pass (0–100), or null when there's nothing to check.</summary>
    public static int? Readiness(List<AuditCheck> checks)
    {
        var applicable = checks.Where(c => c.Checked > 0).ToList();
        return applicable.Count == 0 ? null : (int)Math.Round(100.0 * applicable.Count(c => c.Gaps.Count == 0) / applicable.Count);
    }

    private static string Short(string text)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length == 0 ? "defects" : text.Length <= 60 ? text : text[..59] + "…";
    }
}
