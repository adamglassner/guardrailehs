using System.Text.Json.Serialization;

namespace Guardrail.Data;

/// <summary>Fleet statuses (shared with the other modules through <see cref="Status"/> where they overlap).</summary>
public static class FleetStatus
{
    public const string Qualified = "Qualified";
    public const string Incomplete = "Incomplete";
    public const string Applied = "Applied";
    public const string Accepted = "Accepted";
    public const string OnHold = "On hold";
    public const string InService = "In service";
    public const string OutOfService = "Out of service";
    public const string Sold = "Sold";
    public const string RegistrationExpired = "Registration expired";
    public const string InspectionOverdue = "Inspection overdue";
    public const string NoDefects = "No defects";
    public const string DefectsOpen = "Defects open";
    public const string Repaired = "Repaired";
    public const string Scheduled = "Scheduled";
    public const string Compliant = "Compliant";
    public const string Violation = "Violation";
    public const string PendingReview = "Pending review";
    public const string Preventable = "Preventable";
    public const string NonPreventable = "Non-preventable";
    public const string Pass = "Pass";
    public const string Gaps = "Gaps";
}

/// <summary>A driver and their driver qualification (DQ) file.</summary>
public sealed class Driver : RecordBase
{
    public const int DueSoonDays = 30;
    public static readonly string[] LicenseClasses = ["CDL-A", "CDL-B", "CDL-C", "Non-CDL"];
    public static readonly string[] ApplicationStatuses = [FleetStatus.Applied, Status.InProgress, FleetStatus.Accepted, FleetStatus.OnHold, Status.Rejected];
    public static readonly string[] YesNo = ["No", "Yes"];

    public string Name { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string Location { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string LicenseState { get; set; } = "";
    public string LicenseClass { get; set; } = "CDL-A";
    public string Endorsements { get; set; } = "";
    public DateTime? LicenseExpires { get; set; }
    public DateTime? MedicalCardExpires { get; set; }
    public DateTime? LastMvrReview { get; set; }
    public DateTime? HireDate { get; set; }
    public string RoadTest { get; set; } = "No";
    public string ApplicationStatus { get; set; } = FleetStatus.Applied;
    public string Notes { get; set; } = "";
    /// <summary>Scanned DQ file, relative to the data folder (data\drivers\&lt;id&gt;\file).</summary>
    public string StoredFile { get; set; } = "";

    [JsonIgnore] public override string Prefix => "DRV";
    [JsonIgnore] public bool IsActive => ApplicationStatus == FleetStatus.Accepted;
    [JsonIgnore] public bool HasFile => StoredFile.Length > 0;
    [JsonIgnore] public DateTime? MvrDue => LastMvrReview?.Date.AddYears(1);
    [JsonIgnore] public string LicenseText => LicenseExpires?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string MedicalText => MedicalCardExpires?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string MvrText => MvrDue?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string ClassText => LicenseState.Length > 0 ? $"{LicenseClass} · {LicenseState}" : LicenseClass;

    /// <summary>Problems with this driver's qualification file (empty when everything is current).</summary>
    [JsonIgnore]
    public List<string> Problems
    {
        get
        {
            var today = DateTime.Today;
            var list = new List<string>();
            if (LicenseExpires is null) list.Add("License expiration date missing");
            else if (LicenseExpires.Value.Date < today) list.Add($"License expired {LicenseExpires:MMM d, yyyy}");
            if (MedicalCardExpires is null) list.Add("Medical certificate missing");
            else if (MedicalCardExpires.Value.Date < today) list.Add($"Medical certificate expired {MedicalCardExpires:MMM d, yyyy}");
            if (MvrDue is null) list.Add("No annual MVR review on file");
            else if (MvrDue.Value < today) list.Add($"Annual MVR review overdue since {MvrDue:MMM d, yyyy}");
            return list;
        }
    }

    /// <summary>Dates coming up in the next 30 days.</summary>
    [JsonIgnore]
    public List<(string What, DateTime When)> ComingUp
    {
        get
        {
            var today = DateTime.Today;
            var limit = today.AddDays(DueSoonDays);
            var list = new List<(string, DateTime)>();
            if (LicenseExpires is { } l && l.Date >= today && l.Date <= limit) list.Add(("License expires", l.Date));
            if (MedicalCardExpires is { } m && m.Date >= today && m.Date <= limit) list.Add(("Medical certificate expires", m.Date));
            if (MvrDue is { } v && v >= today && v <= limit) list.Add(("Annual MVR review due", v));
            return list;
        }
    }

    [JsonIgnore]
    public string DisplayStatus =>
        !IsActive ? ApplicationStatus
        : Problems.Any(p => p.Contains("expired") || p.Contains("overdue")) ? Status.Expired
        : Problems.Count > 0 ? FleetStatus.Incomplete
        : ComingUp.Count > 0 ? Status.ExpiringSoon
        : FleetStatus.Qualified;
}

/// <summary>A vehicle or piece of equipment in the fleet.</summary>
public sealed class Vehicle : RecordBase
{
    public const int DueSoonDays = 30;
    public static readonly string[] Types = ["Tractor", "Straight truck", "Trailer", "Van", "Pickup", "Car", "Equipment"];
    public static readonly string[] ServiceStatuses = [FleetStatus.InService, FleetStatus.OutOfService, FleetStatus.Sold];

    public string UnitNumber { get; set; } = "";
    public string Type { get; set; } = "Straight truck";
    public string Year { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string Vin { get; set; } = "";
    public string Plate { get; set; } = "";
    public string PlateState { get; set; } = "";
    public string AssignedDriver { get; set; } = "";
    public string Location { get; set; } = "";
    public decimal Odometer { get; set; }
    public DateTime? RegistrationExpires { get; set; }
    public DateTime? LastAnnualInspection { get; set; }
    public string ServiceStatus { get; set; } = FleetStatus.InService;
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "VEH";
    [JsonIgnore] public bool IsActive => ServiceStatus != FleetStatus.Sold;
    [JsonIgnore] public string Description => string.Join(" ", new[] { Year, Make, Model }.Where(s => s.Length > 0));
    [JsonIgnore] public DateTime? AnnualDue => LastAnnualInspection?.Date.AddYears(1);
    [JsonIgnore] public string RegistrationText => RegistrationExpires?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string AnnualText => AnnualDue?.ToString("MMM d, yyyy") ?? "Not on file";
    [JsonIgnore] public string OdometerText => Odometer == 0 ? "—" : Odometer.ToString("N0");

    [JsonIgnore]
    public string DisplayStatus
    {
        get
        {
            var today = DateTime.Today;
            var soon = today.AddDays(DueSoonDays);
            if (ServiceStatus != FleetStatus.InService) return ServiceStatus;
            if (RegistrationExpires is { } r && r.Date < today) return FleetStatus.RegistrationExpired;
            if (AnnualDue is null) return Status.NotInspected;
            if (AnnualDue.Value < today) return FleetStatus.InspectionOverdue;
            if ((RegistrationExpires is { } r2 && r2.Date <= soon) || AnnualDue.Value <= soon) return Status.DueSoon;
            return FleetStatus.InService;
        }
    }
}

/// <summary>A driver vehicle inspection report (pre-trip or post-trip).</summary>
public sealed class Dvir : RecordBase
{
    public static readonly string[] TripTypes = ["Pre-trip", "Post-trip"];
    public static readonly string[] DefectChoices = [FleetStatus.NoDefects, "Defects found"];
    public static readonly string[] SafeChoices = ["Yes", "No – out of service"];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Driver { get; set; } = "";
    public string UnitNumber { get; set; } = "";
    public string TripType { get; set; } = "Pre-trip";
    public decimal Odometer { get; set; }
    public bool DefectsFound { get; set; }
    public string Defects { get; set; } = "";
    public string SafeToOperate { get; set; } = "Yes";
    public DateTime? RepairedOn { get; set; }
    public string RepairedBy { get; set; } = "";
    public string RepairNotes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "DVR";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public bool DefectsOpen => DefectsFound && RepairedOn is null;
    [JsonIgnore] public string DisplayStatus => !DefectsFound ? FleetStatus.NoDefects : RepairedOn is null ? FleetStatus.DefectsOpen : FleetStatus.Repaired;
    [JsonIgnore] public string DefectsText => DefectsFound ? (Defects.Length > 0 ? Defects : "Defects found") : "None";
}

/// <summary>A maintenance work order: preventive maintenance, repairs, the annual DOT inspection.</summary>
public sealed class MaintenanceOrder : RecordBase
{
    public const string AnnualInspection = "Annual DOT inspection";
    public const int DueSoonDays = 14;
    public static readonly string[] Services = ["PM service", "Oil change", "Tires", "Brakes", AnnualInspection, "Repair", "Other"];
    public static readonly string[] Repeats = ["One-time", "Monthly", "Every 3 months", "Every 6 months", "Yearly"];

    public string UnitNumber { get; set; } = "";
    public string Service { get; set; } = "PM service";
    public string Description { get; set; } = "";
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(7);
    public decimal DueOdometer { get; set; }
    public string Vendor { get; set; } = "";
    public decimal Cost { get; set; }
    public DateTime? CompletedOn { get; set; }
    public string Repeat { get; set; } = "One-time";
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "WO";
    [JsonIgnore] public bool IsComplete => CompletedOn is not null;
    [JsonIgnore] public string DueText => DueDate.ToString("MMM d, yyyy");
    [JsonIgnore] public string CostText => Cost == 0 ? "—" : Cost.ToString("C0");
    [JsonIgnore] public int RepeatMonths => Repeat switch { "Monthly" => 1, "Every 3 months" => 3, "Every 6 months" => 6, "Yearly" => 12, _ => 0 };

    [JsonIgnore]
    public string DisplayStatus =>
        IsComplete ? Status.Completed
        : DueDate.Date < DateTime.Today ? Status.Overdue
        : DueDate.Date <= DateTime.Today.AddDays(DueSoonDays) ? Status.DueSoon
        : FleetStatus.Scheduled;
}

/// <summary>A reviewed driver day from hours-of-service (ELD) logs.</summary>
public sealed class HosLog : RecordBase
{
    public const string None = "None";
    public static readonly string[] Violations = [None, "11-hour driving", "14-hour window", "30-minute break", "60/70-hour", "Form & manner", "Other"];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Driver { get; set; } = "";
    public decimal DrivingHours { get; set; }
    public decimal OnDutyHours { get; set; }
    public string Violation { get; set; } = None;
    public string ReviewedBy { get; set; } = "";
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "HOS";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public bool HasViolation => Violation != None;
    [JsonIgnore] public string DisplayStatus => HasViolation ? FleetStatus.Violation : FleetStatus.Compliant;
    [JsonIgnore] public string DrivingText => DrivingHours.ToString("0.#");
    [JsonIgnore] public string OnDutyText => OnDutyHours.ToString("0.#");

    /// <summary>Property-carrying limits (49 CFR 395.3): 11 hours driving, 14-hour on-duty window.</summary>
    public static string DetectViolation(decimal driving, decimal onDuty, string chosen) =>
        chosen != None ? chosen
        : driving > 11 ? "11-hour driving"
        : onDuty > 14 ? "14-hour window"
        : None;
}

/// <summary>An entry in the DOT accident register (49 CFR 390.15).</summary>
public sealed class FleetAccident : RecordBase
{
    public static readonly string[] Preventabilities = [FleetStatus.PendingReview, FleetStatus.Preventable, FleetStatus.NonPreventable];
    public static readonly string[] YesNo = ["No", "Yes"];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Time { get; set; } = "";
    public string Driver { get; set; } = "";
    public string UnitNumber { get; set; } = "";
    public string Location { get; set; } = "";
    public string CityState { get; set; } = "";
    public string Description { get; set; } = "";
    public int Injuries { get; set; }
    public int Fatalities { get; set; }
    public string TowAway { get; set; } = "No";
    public string Hazmat { get; set; } = "No";
    public string Citation { get; set; } = "No";
    public string PoliceReport { get; set; } = "";
    public string Preventability { get; set; } = FleetStatus.PendingReview;
    public string Notes { get; set; } = "";
    /// <summary>Dashcam / CCTV video, relative to the data folder (data\accidents\&lt;id&gt;\file).</summary>
    public string StoredFile { get; set; } = "";

    [JsonIgnore] public override string Prefix => "ACC";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public bool HasFile => StoredFile.Length > 0;
    /// <summary>DOT recordable: a fatality, an injury treated away from the scene, or a vehicle towed away.</summary>
    [JsonIgnore] public bool IsDotRecordable => Fatalities > 0 || Injuries > 0 || TowAway == "Yes";
    [JsonIgnore] public string RecordableText => IsDotRecordable ? "DOT recordable" : "Not recordable";
    [JsonIgnore] public string DisplayStatus => Preventability;
}
