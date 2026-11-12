using Guardrail.Data;

namespace Guardrail.Services;

public sealed record ReportTable(string[] Headers, List<string[]> Rows, string? Summary = null);

/// <param name="UsesYear">True when the report is for one calendar year (the page shows year buttons).</param>
public sealed record ReportDef(string Key, string Title, string Glyph, string Category, string Description, bool UsesYear, Func<int, ReportTable> Build);

/// <summary>Every report in Guardrail. Each one builds a table from the local records; the Reports page previews and exports it.</summary>
public static class ReportCatalog
{
    public const string Safety = "Safety";
    public const string Compliance = "Compliance";
    public const string Fleet = "Fleet";

    private static string D(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";

    public static readonly List<ReportDef> All =
    [
        new("osha300", "OSHA 300 log", "", Safety,
            "Log of work-related injuries and illnesses for the year: every recordable injury / illness case, with OSHA's columns G–M. Keep it for 5 years.",
            true, Osha300),
        new("osha300a", "OSHA 300A summary", "", Safety,
            "Year-end summary to post February 1 – April 30: case totals, days away and restricted, injury types, average employees and hours worked.",
            true, Osha300A),
        new("rates-by-month", "Incident rates by month", "", Safety,
            "Hours, recordable and DART cases and TRIR / DART rate for each month, from Incidents and Monthly Data.",
            true, RatesByMonth),
        new("incidents-by-type", "Incidents by type and location", "", Safety,
            "How many incidents of each type happened at each location during the year.",
            true, IncidentsByType),
        new("overdue-actions", "Overdue actions", "", Safety,
            "Every open action past its due date, oldest first, with owner and source.",
            false, _ => OverdueActions()),
        new("inspection-findings", "Inspection findings", "", Safety,
            "Every failed checklist item from completed inspections during the year, with its notes and linked actions.",
            true, InspectionFindings),
        new("training-expiring", "Training expiring or expired", "", Compliance,
            "Training records that expire in the next 60 days or have already expired.",
            false, _ => TrainingExpiring()),
        new("compliance-due", "Compliance due in 60 days", "", Compliance,
            "Permits, licenses, reports and inspections that are overdue or due in the next 60 days.",
            false, _ => ComplianceDue()),
        new("loto-due", "LOTO inspections due", "", Compliance,
            "Lockout / tagout procedures whose annual periodic inspection is overdue, due within 30 days, or never done.",
            false, _ => LotoDue()),
        new("sds-missing", "Products missing an SDS", "", Compliance,
            "Chemical inventory entries with no Safety Data Sheet on file.",
            false, _ => SdsMissing()),
        new("open-claims", "Open claims", "", Compliance,
            "Every open claim with reserve, paid, total incurred and days open.",
            false, _ => OpenClaims()),

        new("driver-applications", "Driver application status", "\uE77B", Fleet,
            "Every driver and applicant with their application status, license class and when they were added.",
            false, _ => DriverApplications()),
        new("dq-status", "Driver qualification file status", "\uE77B", Fleet,
            "Active drivers' license, medical certificate and annual MVR dates, and anything missing or expired.",
            false, _ => DriverQualification()),
        new("vehicle-renewals", "Vehicle maintenance and renewal status", "\uE804", Fleet,
            "Each vehicle's registration, annual DOT inspection and next scheduled service.",
            false, _ => VehicleRenewals()),
        new("accident-register", "DOT accident register", "\uE7BA", Fleet,
            "The 49 CFR 390.15 register for the year: date, city and state, driver, injuries, fatalities and hazmat. Keep three years.",
            true, AccidentRegister),
        new("dvir-defects", "DVIR defects and repairs", "\uE9D9", Fleet,
            "Every DVIR with defects during the year and whether the repair was certified.",
            true, DvirDefects),
        new("hos-violations", "Hours-of-service violations", "\uE823", Fleet,
            "Every reviewed log with a violation during the year.",
            true, HosViolations),
        new("fleet-roster", "Fleet roster", "\uE804", Fleet,
            "Every vehicle with VIN, plate, assigned driver, odometer and status.",
            false, _ => FleetRoster()),
    ];

    // ------------------------------------------------------------------ OSHA

    private static List<Incident> RecordablesIn(int year) =>
        DataStore.Incidents.Items.Where(i => i.IsRecordable && i.Date.Year == year).OrderBy(i => i.Date).ThenBy(i => i.Seq).ToList();

    public static ReportTable Osha300(int year)
    {
        static string X(bool b) => b ? "X" : "";
        var rows = RecordablesIn(year).Select(i => new[]
        {
            i.Number, i.PersonInvolved, i.JobTitle, D(i.Date), i.Location,
            $"{i.Description}{(i.BodyPart.Length > 0 ? $" ({i.BodyPart})" : "")}",
            X(i.Classification == Incident.Fatality),
            X(i.Classification == Incident.DaysAwayCase),
            X(i.Classification == Incident.RestrictedTransfer),
            X(i.Classification == Incident.MedicalTreatment),
            i.DaysAway.ToString(), i.DaysRestricted.ToString(), i.CaseType,
        }).ToList();
        return new ReportTable(
            ["(A) Case no.", "(B) Employee name", "(C) Job title", "(D) Date of injury / onset", "(E) Where the event occurred",
             "(F) Injury / illness, body part, what caused it", "(G) Death", "(H) Days away", "(I) Job transfer / restriction",
             "(J) Other recordable", "(K) Days away from work", "(L) Days on transfer / restriction", "(M) Injury / illness type"],
            rows,
            rows.Count == 0 ? $"No recordable cases in {year}." : $"{rows.Count} recordable case{(rows.Count == 1 ? "" : "s")} in {year}.");
    }

    public static ReportTable Osha300A(int year)
    {
        var cases = RecordablesIn(year);
        var m = SafetyMetrics.ForYear(year);
        int Type(string t) => cases.Count(c => c.CaseType == t);
        var rows = new List<string[]>
        {
            new[] { "Number of cases", "(G) Total deaths", cases.Count(c => c.Classification == Incident.Fatality).ToString() },
            new[] { "Number of cases", "(H) Total cases with days away from work", cases.Count(c => c.Classification == Incident.DaysAwayCase).ToString() },
            new[] { "Number of cases", "(I) Total cases with job transfer or restriction", cases.Count(c => c.Classification == Incident.RestrictedTransfer).ToString() },
            new[] { "Number of cases", "(J) Total other recordable cases", cases.Count(c => c.Classification == Incident.MedicalTreatment).ToString() },
            new[] { "Number of days", "(K) Total days away from work", cases.Sum(c => c.DaysAway).ToString() },
            new[] { "Number of days", "(L) Total days of job transfer or restriction", cases.Sum(c => c.DaysRestricted).ToString() },
            new[] { "Injury and illness types", "(M1) Injuries", Type("Injury").ToString() },
            new[] { "Injury and illness types", "(M2) Skin disorders", Type("Skin disorder").ToString() },
            new[] { "Injury and illness types", "(M3) Respiratory conditions", Type("Respiratory condition").ToString() },
            new[] { "Injury and illness types", "(M4) Poisonings", Type("Poisoning").ToString() },
            new[] { "Injury and illness types", "(M5) Hearing loss", Type("Hearing loss").ToString() },
            new[] { "Injury and illness types", "(M6) All other illnesses", Type("All other illnesses").ToString() },
            new[] { "Employment information", "Annual average number of employees", m.AverageEmployees.ToString("0") },
            new[] { "Employment information", "Total hours worked by all employees last year", m.Hours.ToString("0") },
            new[] { "Rates (not on the form)", "Total recordable incident rate (TRIR)", SafetyMetrics.Format(m.Trir) },
            new[] { "Rates (not on the form)", "DART rate", SafetyMetrics.Format(m.Dart) },
        };
        return new ReportTable(["Section", "Item", "Value"], rows,
            m.Hours == 0 ? $"Enter {year}'s hours worked in Monthly Data to fill in the employment information." : $"Summary for {year}. A company executive must certify the form before it's posted.");
    }

    public static ReportTable RatesByMonth(int year)
    {
        var rows = new List<string[]>();
        for (int month = 1; month <= 12; month++)
        {
            var start = new DateTime(year, month, 1);
            if (start > DateTime.Today) break;
            var m = SafetyMetrics.Compute(start, start.AddMonths(1).AddDays(-1));
            rows.Add([start.ToString("MMM yyyy"), m.Hours.ToString("N0"), m.Recordables.ToString(), m.DartCases.ToString(),
                      SafetyMetrics.Format(m.Trir), SafetyMetrics.Format(m.Dart), m.Miles.ToString("N0"), m.VehicleAccidents.ToString()]);
        }
        var y = SafetyMetrics.ForYear(year);
        rows.Add([$"{year} total", y.Hours.ToString("N0"), y.Recordables.ToString(), y.DartCases.ToString(),
                  SafetyMetrics.Format(y.Trir), SafetyMetrics.Format(y.Dart), y.Miles.ToString("N0"), y.VehicleAccidents.ToString()]);
        return new ReportTable(["Month", "Hours worked", "Recordable cases", "DART cases", "TRIR", "DART rate", "Miles driven", "Vehicle incidents"], rows,
            "Rates are per 200,000 hours. Months without hours in Monthly Data show —.");
    }

    public static ReportTable IncidentsByType(int year)
    {
        var incidents = DataStore.Incidents.Items.Where(i => i.Date.Year == year).ToList();
        var rows = incidents
            .GroupBy(i => i.Location.Trim().Length == 0 ? "(no location)" : i.Location.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new[] { g.Key }
                .Concat(Incident.Types.Select(t => g.Count(i => i.Type == t).ToString()))
                .Append(g.Count().ToString())
                .ToArray())
            .ToList();
        if (rows.Count > 0)
            rows.Add(new[] { "All locations" }.Concat(Incident.Types.Select(t => incidents.Count(i => i.Type == t).ToString())).Append(incidents.Count.ToString()).ToArray());
        return new ReportTable(new[] { "Location" }.Concat(Incident.Types).Append("Total").ToArray(), rows,
            $"{incidents.Count} incident{(incidents.Count == 1 ? "" : "s")} in {year}.");
    }

    // ------------------------------------------------------------------ Other modules

    public static ReportTable OverdueActions()
    {
        var rows = DataStore.Actions.Items.Where(a => a.IsOverdue).OrderBy(a => a.DueDate)
            .Select(a => new[] { a.Number, a.Title, a.Owner, a.Location, a.Priority, D(a.DueDate), ((DateTime.Today - a.DueDate!.Value.Date).Days).ToString(), a.Source })
            .ToList();
        return new ReportTable(["Action #", "Title", "Owner", "Location", "Priority", "Due", "Days overdue", "Source"], rows, $"{rows.Count} overdue action{(rows.Count == 1 ? "" : "s")}.");
    }

    public static ReportTable InspectionFindings(int year)
    {
        var rows = DataStore.Inspections.Items.Where(i => i.IsComplete && i.Date.Year == year)
            .OrderBy(i => i.Date)
            .SelectMany(i => i.Items.Where(x => x.Result == ChecklistItem.Fail)
                .Select(x => new[] { i.Number, D(i.Date), i.TemplateName, i.Location, i.Inspector, x.Text, x.Note, i.LinkedActions }))
            .ToList();
        return new ReportTable(["Inspection #", "Date", "Checklist", "Location", "Inspector", "Failed item", "Note", "Actions"], rows, $"{rows.Count} finding{(rows.Count == 1 ? "" : "s")} in {year}.");
    }

    public static ReportTable TrainingExpiring()
    {
        var limit = DateTime.Today.AddDays(60);
        var rows = DataStore.Training.Items.Where(t => t.ExpiresOn is { } e && e.Date <= limit)
            .OrderBy(t => t.ExpiresOn)
            .Select(t => new[] { t.Employee, t.Course, D(t.CompletedOn), D(t.ExpiresOn), t.DisplayStatus, t.Trainer })
            .ToList();
        return new ReportTable(["Employee", "Course", "Completed", "Expires", "Status", "Trainer"], rows, $"{rows.Count} record{(rows.Count == 1 ? "" : "s")} expired or expiring in 60 days.");
    }

    public static ReportTable ComplianceDue()
    {
        var limit = DateTime.Today.AddDays(60);
        var rows = DataStore.Compliance.Items.Where(c => !c.IsCompleted && c.DueDate.Date <= limit)
            .OrderBy(c => c.DueDate)
            .Select(c => new[] { c.Number, c.Title, c.Agency, c.Type, c.Reference, c.Location, c.Owner, D(c.DueDate), c.DisplayStatus })
            .ToList();
        return new ReportTable(["Req #", "Requirement", "Agency", "Type", "Reference", "Location", "Owner", "Due", "Status"], rows, $"{rows.Count} requirement{(rows.Count == 1 ? "" : "s")} overdue or due in 60 days.");
    }

    public static ReportTable LotoDue()
    {
        var rows = DataStore.Loto.Items.Where(l => l.DisplayStatus is Status.Overdue or Status.DueSoon or Status.NotInspected)
            .OrderBy(l => l.NextInspection ?? DateTime.MinValue)
            .Select(l => new[] { l.Number, l.Equipment, l.AssetTag, l.Location, l.LastText, l.NextText, l.DisplayStatus })
            .ToList();
        return new ReportTable(["LOTO #", "Equipment", "Asset tag", "Location", "Last inspected", "Next due", "Status"], rows, $"{rows.Count} procedure{(rows.Count == 1 ? "" : "s")} need an inspection.");
    }

    public static ReportTable SdsMissing()
    {
        var rows = DataStore.Sds.Items.Where(s => !s.HasFile).OrderBy(s => s.Product)
            .Select(s => new[] { s.Number, s.Product, s.Manufacturer, s.Location, s.SignalWord, s.Hazards })
            .ToList();
        return new ReportTable(["SDS #", "Product", "Manufacturer", "Location", "Signal word", "Hazards"], rows, $"{rows.Count} product{(rows.Count == 1 ? "" : "s")} without a Safety Data Sheet.");
    }

    public static ReportTable OpenClaims()
    {
        var open = DataStore.Claims.Items.Where(c => c.IsOpen).OrderBy(c => c.DateOfLoss).ToList();
        var rows = open.Select(c => new[] { c.Number, c.Type, c.Claimant, D(c.DateOfLoss), c.Carrier, c.CarrierClaimNumber,
                                            c.Reserve.ToString("C0"), c.Paid.ToString("C0"), c.Incurred.ToString("C0"), c.DaysOpen.ToString() }).ToList();
        return new ReportTable(["Claim #", "Type", "Claimant", "Date of loss", "Carrier", "Carrier claim #", "Reserve", "Paid", "Incurred", "Days open"], rows,
            $"{rows.Count} open claim{(rows.Count == 1 ? "" : "s")}, {open.Sum(c => c.Incurred):C0} total incurred.");
    }

    // ------------------------------------------------------------------ Fleet

    public static ReportTable DriverApplications()
    {
        var rows = DataStore.Drivers.Items.OrderBy(d => d.ApplicationStatus).ThenBy(d => d.Name)
            .Select(d => new[] { d.Number, d.Name, d.ApplicationStatus, d.LicenseClass, d.Location, D(d.CreatedAt.Date), D(d.HireDate) })
            .ToList();
        return new ReportTable(["Driver #", "Name", "Application status", "License class", "Location", "Added", "Hire date"], rows,
            $"{rows.Count} driver{(rows.Count == 1 ? "" : "s")} and applicants.");
    }

    public static ReportTable DriverQualification()
    {
        var active = DataStore.Drivers.Items.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
        var rows = active.Select(d => new[] { d.Name, d.ClassText, D(d.LicenseExpires), D(d.MedicalCardExpires), D(d.LastMvrReview), D(d.MvrDue),
                                              d.RoadTest, d.DisplayStatus, string.Join("; ", d.Problems) }).ToList();
        return new ReportTable(["Driver", "License", "License expires", "Medical card expires", "Last MVR review", "MVR due", "Road test", "Status", "Problems"], rows,
            $"{active.Count(d => d.DisplayStatus == FleetStatus.Qualified)} of {active.Count} active drivers fully qualified.");
    }

    public static ReportTable VehicleRenewals()
    {
        var open = DataStore.Maintenance.Items.Where(w => !w.IsComplete).ToList();
        var rows = DataStore.Vehicles.Items.Where(v => v.IsActive).OrderBy(v => v.UnitNumber)
            .Select(v =>
            {
                var next = open.Where(w => string.Equals(w.UnitNumber, v.UnitNumber, StringComparison.OrdinalIgnoreCase)).MinBy(w => w.DueDate);
                return new[] { v.UnitNumber, v.Description, D(v.RegistrationExpires), D(v.LastAnnualInspection), D(v.AnnualDue),
                               next is null ? "" : $"{next.Service} ({next.Number})", next is null ? "" : D(next.DueDate), v.DisplayStatus };
            })
            .ToList();
        return new ReportTable(["Unit", "Vehicle", "Registration expires", "Last annual inspection", "Annual due", "Next service", "Service due", "Status"], rows,
            $"{rows.Count} active vehicle{(rows.Count == 1 ? "" : "s")}.");
    }

    public static ReportTable AccidentRegister(int year)
    {
        var list = DataStore.FleetAccidents.Items.Where(a => a.Date.Year == year).OrderBy(a => a.Date).ToList();
        var rows = list.Select(a => new[] { a.Number, D(a.Date), a.CityState, a.Driver, a.UnitNumber, a.Injuries.ToString(), a.Fatalities.ToString(),
                                            a.Hazmat, a.TowAway, a.IsDotRecordable ? "Yes" : "No", a.Preventability, a.PoliceReport }).ToList();
        return new ReportTable(["Accident #", "Date", "City and state", "Driver", "Unit", "Injuries", "Fatalities", "Hazmat released", "Tow-away", "DOT recordable", "Preventability", "Police report"], rows,
            $"{list.Count} accident{(list.Count == 1 ? "" : "s")} in {year}, {list.Count(a => a.IsDotRecordable)} DOT recordable.");
    }

    public static ReportTable DvirDefects(int year)
    {
        var rows = DataStore.Dvirs.Items.Where(r => r.DefectsFound && r.Date.Year == year).OrderBy(r => r.Date)
            .Select(r => new[] { r.Number, D(r.Date), r.UnitNumber, r.Driver, r.Defects, r.DisplayStatus, D(r.RepairedOn), r.RepairedBy })
            .ToList();
        return new ReportTable(["DVIR #", "Date", "Unit", "Driver", "Defects", "Status", "Repaired on", "Certified by"], rows,
            $"{rows.Count} DVIR{(rows.Count == 1 ? "" : "s")} with defects in {year}.");
    }

    public static ReportTable HosViolations(int year)
    {
        var rows = DataStore.Hos.Items.Where(h => h.HasViolation && h.Date.Year == year).OrderBy(h => h.Date)
            .Select(h => new[] { h.Number, D(h.Date), h.Driver, h.Violation, h.DrivingText, h.OnDutyText, h.ReviewedBy, h.Notes })
            .ToList();
        return new ReportTable(["HOS #", "Date", "Driver", "Violation", "Driving hours", "On-duty hours", "Reviewed by", "Notes"], rows,
            $"{rows.Count} violation{(rows.Count == 1 ? "" : "s")} in {year}.");
    }

    public static ReportTable FleetRoster()
    {
        var rows = DataStore.Vehicles.Items.OrderBy(v => v.UnitNumber)
            .Select(v => new[] { v.UnitNumber, v.Type, v.Description, v.Vin, $"{v.Plate} {v.PlateState}".Trim(), v.AssignedDriver, v.Location, v.OdometerText, v.DisplayStatus })
            .ToList();
        return new ReportTable(["Unit", "Type", "Vehicle", "VIN", "Plate", "Assigned driver", "Location", "Odometer", "Status"], rows,
            $"{rows.Count} vehicle{(rows.Count == 1 ? "" : "s")}.");
    }
}
