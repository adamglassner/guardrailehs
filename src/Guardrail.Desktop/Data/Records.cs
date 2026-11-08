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
