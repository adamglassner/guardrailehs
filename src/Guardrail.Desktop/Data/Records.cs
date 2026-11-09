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
