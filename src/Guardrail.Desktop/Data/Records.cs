using System.Text.Json.Serialization;

namespace Guardrail.Data;

/// <summary>Values used by more than one record type.</summary>
public static class Status
{
    public const string Open = "Open";
    public const string InProgress = "In progress";
    public const string Closed = "Closed";
    public const string Overdue = "Overdue";
    public const string ActionCreated = "Action created";
    public const string Current = "Current";
    public const string ExpiringSoon = "Expiring soon";
    public const string Expired = "Expired";
    public const string NoExpiry = "No expiry";
    public const string DueSoon = "Due soon";
    public const string Upcoming = "Upcoming";
    public const string Completed = "Completed";
    public const string Denied = "Denied";
    public const string ReviewOverdue = "Review overdue";
    public const string ReviewDueSoon = "Review due soon";
    public const string NoReviewDate = "No review date";
    public const string AllSafe = "All safe";
    public const string AtRiskFound = "At-risk found";
    public const string Active = "Active";
    public const string Cancelled = "Cancelled";
    public const string NotInspected = "Not inspected";
    public const string OnFile = "On file";
    public const string MissingSds = "Missing SDS";
}

public abstract class RecordBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Seq { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonIgnore] public abstract string Prefix { get; }
    [JsonIgnore] public string Number => $"{Prefix}-{Seq:0000}";
}

/// <summary>A corrective or preventive action.</summary>
public sealed class ActionItem : RecordBase
{
    public static readonly string[] Priorities = ["Low", "Medium", "High"];
    public static readonly string[] Statuses = [Data.Status.Open, Data.Status.InProgress, Data.Status.Closed];

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = Data.Status.Open;
    /// <summary>Where the action came from, e.g. "OBS-0004".</summary>
    public string Source { get; set; } = "";
    public DateTime? ClosedAt { get; set; }

    [JsonIgnore] public override string Prefix => "ACT";
    [JsonIgnore] public bool IsClosed => Status == Data.Status.Closed;
    [JsonIgnore] public bool IsOverdue => !IsClosed && DueDate is { } due && due.Date < DateTime.Today;
    [JsonIgnore] public string DisplayStatus => IsOverdue ? Data.Status.Overdue : Status;
    [JsonIgnore] public string DueText => DueDate?.ToString("MMM d, yyyy") ?? "—";
}

/// <summary>A hazard report or positive "good catch" from the field.</summary>
public sealed class Observation : RecordBase
{
    public static readonly string[] Types = ["Unsafe condition", "Unsafe behavior", "Good catch"];
    public static readonly string[] Statuses = [Data.Status.Open, Data.Status.ActionCreated, Data.Status.Closed];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Type { get; set; } = "Unsafe condition";
    public string Location { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = Data.Status.Open;
    /// <summary>Number of the action created from this observation, e.g. "ACT-0007".</summary>
    public string LinkedAction { get; set; } = "";

    [JsonIgnore] public override string Prefix => "OBS";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public bool IsGoodCatch => Type == "Good catch";
    [JsonIgnore] public bool CanCreateAction => string.IsNullOrEmpty(LinkedAction) && Status != Data.Status.Closed;
}

/// <summary>One completed training for one person.</summary>
public sealed class TrainingRecord : RecordBase
{
    public const int ExpiringSoonDays = 30;

    public string Employee { get; set; } = "";
    public string Course { get; set; } = "";
    public DateTime CompletedOn { get; set; } = DateTime.Today;
    public DateTime? ExpiresOn { get; set; }
    public string Trainer { get; set; } = "";
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "TRN";
    [JsonIgnore] public string CompletedText => CompletedOn.ToString("MMM d, yyyy");
    [JsonIgnore] public string ExpiresText => ExpiresOn?.ToString("MMM d, yyyy") ?? "—";

    [JsonIgnore]
    public string DisplayStatus => ExpiresOn switch
    {
        null => Status.NoExpiry,
        { } d when d.Date < DateTime.Today => Status.Expired,
        { } d when d.Date <= DateTime.Today.AddDays(ExpiringSoonDays) => Status.ExpiringSoon,
        _ => Status.Current,
    };
}

/// <summary>A regulatory requirement: a permit, license, report, inspection or other obligation with a due date.</summary>
public sealed class ComplianceItem : RecordBase
{
    public const int DueSoonDays = 30;
    public static readonly string[] Agencies = ["EPA", "OSHA", "DOT", "State", "Local", "Other"];
    public static readonly string[] Types = ["Permit", "License", "Report", "Inspection", "Plan", "Other"];
    public static readonly string[] Frequencies = ["One-time", "Monthly", "Quarterly", "Yearly"];

    public string Title { get; set; } = "";
    public string Agency { get; set; } = "OSHA";
    public string Type { get; set; } = "Permit";
    public string Reference { get; set; } = "";
    public string Location { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime DueDate { get; set; } = DateTime.Today.AddMonths(1);
    public string Frequency { get; set; } = "Yearly";
    public string Notes { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? LastCompleted { get; set; }

    [JsonIgnore] public override string Prefix => "REG";
    [JsonIgnore] public string DueText => IsCompleted ? "—" : DueDate.ToString("MMM d, yyyy");
    [JsonIgnore] public string LastText => LastCompleted?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public bool IsRecurring => Frequency != "One-time";

    [JsonIgnore]
    public string DisplayStatus =>
        IsCompleted ? Status.Completed
        : DueDate.Date < DateTime.Today ? Status.Overdue
        : DueDate.Date <= DateTime.Today.AddDays(DueSoonDays) ? Status.DueSoon
        : Status.Upcoming;

    /// <summary>Marks the requirement done. Recurring ones roll forward to the next due date; one-time ones close.</summary>
    public void MarkDone(DateTime doneOn)
    {
        LastCompleted = doneOn.Date;
        int months = Frequency switch { "Monthly" => 1, "Quarterly" => 3, "Yearly" => 12, _ => 0 };
        if (months == 0) { IsCompleted = true; return; }
        // Move past today, keeping the same day of the month, even if several periods were missed.
        do DueDate = DueDate.AddMonths(months); while (DueDate.Date <= doneOn.Date);
    }
}

/// <summary>An insurance claim: workers' comp, auto, liability or property.</summary>
public sealed class Claim : RecordBase
{
    public static readonly string[] Types = ["Workers' comp", "Auto", "General liability", "Property"];
    public static readonly string[] Statuses = [Data.Status.Open, Data.Status.Closed, Data.Status.Denied];

    public string Type { get; set; } = "Workers' comp";
    public string CarrierClaimNumber { get; set; } = "";
    public string Claimant { get; set; } = "";
    public DateTime DateOfLoss { get; set; } = DateTime.Today;
    public string Location { get; set; } = "";
    public string RelatedIncident { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string Adjuster { get; set; } = "";
    /// <summary>Money still set aside for this claim.</summary>
    public decimal Reserve { get; set; }
    public decimal Paid { get; set; }
    public string Status { get; set; } = Data.Status.Open;
    public DateTime? ClosedOn { get; set; }
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "CLM";
    [JsonIgnore] public bool IsOpen => Status == Data.Status.Open;
    /// <summary>Total incurred = paid so far + reserve still outstanding.</summary>
    [JsonIgnore] public decimal Incurred => Paid + (IsOpen ? Reserve : 0);
    [JsonIgnore] public string LossText => DateOfLoss.ToString("MMM d, yyyy");
    [JsonIgnore] public string PaidText => Paid.ToString("C0");
    [JsonIgnore] public string IncurredText => Incurred.ToString("C0");
    [JsonIgnore] public int DaysOpen => (int)((IsOpen ? DateTime.Today : (ClosedOn ?? UpdatedAt).Date) - CreatedAt.Date).TotalDays;
}

/// <summary>A controlled document (policy, procedure, program, form). The file itself is copied into the data folder.</summary>
public sealed class LibraryDocument : RecordBase
{
    public const int ReviewSoonDays = 30;
    public static readonly string[] Categories = ["Policy", "Procedure", "Program", "Form", "Training material", "Other"];

    public string Title { get; set; } = "";
    public string Category { get; set; } = "Policy";
    public string DocVersion { get; set; } = "1.0";
    public string Owner { get; set; } = "";
    public DateTime? ReviewDue { get; set; }
    public string Notes { get; set; } = "";
    /// <summary>Path of the stored copy, relative to the data folder (e.g. "library\&lt;id&gt;\Policy.pdf").</summary>
    public string StoredFile { get; set; } = "";

    [JsonIgnore] public override string Prefix => "DOC";
    [JsonIgnore] public string FileName => StoredFile.Length == 0 ? "" : System.IO.Path.GetFileName(StoredFile);
    [JsonIgnore] public bool HasFile => StoredFile.Length > 0;
    [JsonIgnore] public string ReviewText => ReviewDue?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string UpdatedText => UpdatedAt.ToString("MMM d, yyyy");

    [JsonIgnore]
    public string DisplayStatus => ReviewDue switch
    {
        null => Status.NoReviewDate,
        { } d when d.Date < DateTime.Today => Status.ReviewOverdue,
        { } d when d.Date <= DateTime.Today.AddDays(ReviewSoonDays) => Status.ReviewDueSoon,
        _ => Status.Current,
    };
}

/// <summary>Exposure data for one location and month: hours worked, headcount and miles driven (used for incident rates).</summary>
public sealed class MonthlyEntry : RecordBase
{
    /// <summary>Always the first day of the month.</summary>
    public DateTime Month { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public string Location { get; set; } = "";
    public decimal HoursWorked { get; set; }
    public decimal Employees { get; set; }
    public decimal MilesDriven { get; set; }
    public string EnteredBy { get; set; } = "";
    public string Notes { get; set; } = "";

    [JsonIgnore] public override string Prefix => "MON";
    [JsonIgnore] public string MonthText => Month.ToString("MMMM yyyy");
    [JsonIgnore] public string HoursText => HoursWorked.ToString("N0");
    [JsonIgnore] public string EmployeesText => Employees.ToString("0.#");
    [JsonIgnore] public string MilesText => MilesDriven == 0 ? "—" : MilesDriven.ToString("N0");

    public static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);
}

/// <summary>A behavior-based safety (BBS) observation card: how many safe and at-risk behaviors were seen.</summary>
public sealed class BbsObservation : RecordBase
{
    public static readonly string[] FocusAreas = ["PPE", "Body position", "Tools & equipment", "Housekeeping", "Procedures", "Driving", "Other"];
    public static readonly string[] YesNo = ["Yes", "No"];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Observer { get; set; } = "";
    public string Area { get; set; } = "";
    public string Task { get; set; } = "";
    public string FocusArea { get; set; } = "PPE";
    public int SafeCount { get; set; }
    public int AtRiskCount { get; set; }
    public string FeedbackGiven { get; set; } = "Yes";
    public string Comments { get; set; } = "";
    /// <summary>Number of the action created from this card, e.g. "ACT-0012".</summary>
    public string LinkedAction { get; set; } = "";

    [JsonIgnore] public override string Prefix => "BBS";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public int Total => SafeCount + AtRiskCount;
    [JsonIgnore] public string PercentSafeText => Total == 0 ? "—" : $"{100.0 * SafeCount / Total:0}%";
    [JsonIgnore] public string DisplayStatus => AtRiskCount > 0 ? Status.AtRiskFound : Status.AllSafe;
    [JsonIgnore] public bool CanCreateAction => AtRiskCount > 0 && LinkedAction.Length == 0;
    [JsonIgnore] public bool HasLinkedAction => LinkedAction.Length > 0;
}

/// <summary>A permit to work: hot work, confined space, working at height and so on.</summary>
public sealed class WorkPermit : RecordBase
{
    public static readonly string[] Types = ["Hot work", "Confined space", "Working at height", "Excavation", "Electrical", "Line break", "Other"];
    public static readonly string[] Statuses = [Data.Status.Active, Data.Status.Closed, Data.Status.Cancelled];

    public string Type { get; set; } = "Hot work";
    public string Work { get; set; } = "";
    public string Location { get; set; } = "";
    public string IssuedTo { get; set; } = "";
    public string IssuedBy { get; set; } = "";
    public DateTime ValidFrom { get; set; } = DateTime.Today;
    public DateTime ValidUntil { get; set; } = DateTime.Today;
    public string Precautions { get; set; } = "";
    public string Status { get; set; } = Data.Status.Active;
    public DateTime? ClosedOn { get; set; }

    [JsonIgnore] public override string Prefix => "PTW";
    [JsonIgnore] public bool IsActive => Status == Data.Status.Active;
    [JsonIgnore] public bool IsExpired => IsActive && ValidUntil.Date < DateTime.Today;
    [JsonIgnore] public string DisplayStatus => IsExpired ? Data.Status.Expired : Status;
    [JsonIgnore] public string ValidText => ValidFrom.Date == ValidUntil.Date
        ? ValidFrom.ToString("MMM d, yyyy")
        : $"{ValidFrom:MMM d} – {ValidUntil:MMM d, yyyy}";
}

/// <summary>An equipment-specific lockout/tagout (energy control) procedure, with its annual periodic inspection.</summary>
public sealed class LotoProcedure : RecordBase
{
    /// <summary>OSHA 1910.147(c)(6): periodic inspection at least annually.</summary>
    public const int InspectionIntervalDays = 365;
    public const int DueSoonDays = 30;

    public string Equipment { get; set; } = "";
    public string AssetTag { get; set; } = "";
    public string Location { get; set; } = "";
    public string ProcedureNumber { get; set; } = "";
    public string EnergySources { get; set; } = "";
    public string IsolationPoints { get; set; } = "";
    public DateTime? LastInspection { get; set; }
    public string Inspector { get; set; } = "";
    /// <summary>Optional procedure document, relative to the data folder (data\loto\&lt;id&gt;\file).</summary>
    public string StoredFile { get; set; } = "";

    [JsonIgnore] public override string Prefix => "LTO";
    [JsonIgnore] public bool HasFile => StoredFile.Length > 0;
    [JsonIgnore] public string FileName => HasFile ? System.IO.Path.GetFileName(StoredFile) : "";
    [JsonIgnore] public DateTime? NextInspection => LastInspection?.Date.AddDays(InspectionIntervalDays);
    [JsonIgnore] public string LastText => LastInspection?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string NextText => NextInspection?.ToString("MMM d, yyyy") ?? "Now";

    [JsonIgnore]
    public string DisplayStatus => NextInspection switch
    {
        null => Status.NotInspected,
        { } d when d < DateTime.Today => Status.Overdue,
        { } d when d <= DateTime.Today.AddDays(DueSoonDays) => Status.DueSoon,
        _ => Status.Current,
    };
}

/// <summary>A hazardous product in the chemical inventory and its Safety Data Sheet.</summary>
public sealed class SdsEntry : RecordBase
{
    public static readonly string[] SignalWords = ["Danger", "Warning", "None"];

    public string Product { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Location { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string SignalWord { get; set; } = "Warning";
    public string Hazards { get; set; } = "";
    public DateTime? RevisionDate { get; set; }
    public string Notes { get; set; } = "";
    /// <summary>The SDS file, relative to the data folder (data\sds\&lt;id&gt;\file).</summary>
    public string StoredFile { get; set; } = "";

    [JsonIgnore] public override string Prefix => "SDS";
    [JsonIgnore] public bool HasFile => StoredFile.Length > 0;
    [JsonIgnore] public string FileName => HasFile ? System.IO.Path.GetFileName(StoredFile) : "";
    [JsonIgnore] public string RevisionText => RevisionDate?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string DisplayStatus => HasFile ? Status.OnFile : Status.MissingSds;
}
