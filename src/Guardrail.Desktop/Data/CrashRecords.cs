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
    /// <summary>The web link the video came from (empty for files on this PC).</summary>
    public string SourceUrl { get; set; } = "";
    /// <summary>What the camera vehicle is called in the report, e.g. "Unit 104" (empty = "the camera vehicle").</summary>
    public string CameraName { get; set; } = "";
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
    [JsonIgnore] public bool IsWeb => SourceUrl.Length > 0;
    [JsonIgnore] public string VideoName => IsWeb ? Guardrail.Services.WebVideo.DisplayName(SourceUrl) : Path.GetFileName(VideoPath);
    [JsonIgnore] public string DateText => (AnalyzedAt ?? CreatedAt).ToString("MMM d, yyyy h:mm tt");
    [JsonIgnore] public bool IsDone => State == ReviewState.Done && Report != null;
    [JsonIgnore] public bool VideoExists => File.Exists(VideoPath);
    /// <summary>Folder holding this review's key-frame images.</summary>
    [JsonIgnore] public string Folder => Path.Combine(DataStore.Folder, "crash-review", Id.ToString("N"));

    /// <summary>Pill text: the verdict when done, otherwise the state.</summary>
    [JsonIgnore]
    public string DisplayStatus => !IsDone ? State : Report!.Verdict switch
    {
        Verdicts.Camera => "Camera vehicle at fault",
        Verdicts.Other => "Other party at fault",
        Verdicts.A => "Vehicle A at fault",
        Verdicts.B => "Vehicle B at fault",
        Verdicts.Unclear => "Shared or unclear",
        _ => "No collision found",
    };

    [JsonIgnore] public string ConfidenceText => IsDone && Report!.Verdict != Verdicts.NoCollision ? $"{Report.Confidence}%" : "—";
    [JsonIgnore] public string TypeText => IsDone ? (Report!.CollisionType.Length > 0 ? Report.CollisionType : "—") : State == ReviewState.Failed ? Error : Stage;
    [JsonIgnore] public string CameraText => !IsDone ? "" : CameraName.Length > 0 ? $"{Report!.CameraKind} · {CameraName}" : Report!.CameraKind;

    /// <summary>
    /// Puts the camera vehicle's name into report text ("The camera vehicle drifted across…" → "Unit 104 drifted across…").
    /// Reports made by earlier versions said "your vehicle" / "you"; that wording is converted too.
    /// </summary>
    public string Describe(string text)
    {
        if (text.Length == 0) return text;
        text = text
            .Replace("ahead of you, in your lane", "ahead of the camera vehicle, in its lane")
            .Replace("into your stopped vehicle", "into the camera vehicle while it was stopped")
            .Replace("Your vehicle", "The camera vehicle").Replace("your vehicle", "the camera vehicle")
            .Replace("on your left", "on the camera vehicle's left").Replace("your path", "the camera vehicle's path").Replace("your lane", "the camera vehicle's lane");
        if (CameraName.Length == 0) return text;
        return text.Replace("The camera vehicle", CameraName).Replace("the camera vehicle", CameraName).Replace("camera vehicle", CameraName);
    }

    /// <summary>A name from a fleet unit number: "104" → "Unit 104"; "Truck 12" stays as it is.</summary>
    public static string NameForUnit(string unit) =>
        unit.Length == 0 ? "" : char.IsLetter(unit[0]) ? unit : $"Unit {unit}";
}
