using System.IO;
using System.Text.Json.Serialization;
using Guardrail.CrashAi;

namespace Guardrail.Data;

public static class ReviewState
{
    public const string Waiting = "Waiting";
    public const string Analyzing = "Analyzing";
    public const string Done = "Done";
    public const string Failed = "Couldn't analyze";
}

/// <summary>One accident video run through Crash Review AI.</summary>
public sealed class VideoReview : RecordBase
{
    public string VideoPath { get; set; } = "";
    public long VideoSize { get; set; }
    public DateTime VideoModified { get; set; }
    /// <summary>"Added by hand", "Watch folder" or "Accident ACC-0003".</summary>
    public string Source { get; set; } = "Added by hand";
    public Guid? AccidentId { get; set; }
    public string State { get; set; } = ReviewState.Waiting;
    public string Error { get; set; } = "";
    public DateTime? AnalyzedAt { get; set; }
    public CrashReport? Report { get; set; }

    /// <summary>Live progress while analyzing (not saved).</summary>
    [JsonIgnore] public double Progress { get; set; }
    [JsonIgnore] public string Stage { get; set; } = "";

    [JsonIgnore] public override string Prefix => "CR";
    [JsonIgnore] public string VideoName => Path.GetFileName(VideoPath);
    [JsonIgnore] public string DateText => (AnalyzedAt ?? CreatedAt).ToString("MMM d, yyyy h:mm tt");
    [JsonIgnore] public bool IsDone => State == ReviewState.Done && Report != null;
    [JsonIgnore] public bool VideoExists => File.Exists(VideoPath);
    /// <summary>Folder holding this review's key-frame images.</summary>
    [JsonIgnore] public string Folder => Path.Combine(DataStore.Folder, "crash-review", Id.ToString("N"));

    /// <summary>Pill text: the verdict when done, otherwise the state.</summary>
    [JsonIgnore]
    public string DisplayStatus => !IsDone ? State : Report!.Verdict switch
    {
        Verdicts.Camera => "Your vehicle at fault",
        Verdicts.Other => "Other party at fault",
        Verdicts.A => "Vehicle A at fault",
        Verdicts.B => "Vehicle B at fault",
        Verdicts.Unclear => "Shared or unclear",
        _ => "No collision found",
    };

    [JsonIgnore] public string ConfidenceText => IsDone && Report!.Verdict != Verdicts.NoCollision ? $"{Report.Confidence}%" : "—";
    [JsonIgnore] public string TypeText => IsDone ? (Report!.CollisionType.Length > 0 ? Report.CollisionType : "—") : State == ReviewState.Failed ? Error : Stage;
    [JsonIgnore] public string CameraText => IsDone ? Report!.CameraKind : "";
}
