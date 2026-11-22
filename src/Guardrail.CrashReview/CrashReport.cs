namespace Guardrail.CrashAi;

/// <summary>Who the analysis says was most likely at fault.</summary>
public static class Verdicts
{
    /// <summary>The vehicle the camera is mounted in.</summary>
    public const string Camera = "camera";
    /// <summary>The other road user (camera-vehicle collisions).</summary>
    public const string Other = "other";
    /// <summary>Road user A / B (collisions seen from a fixed camera or between two other road users).</summary>
    public const string A = "a";
    public const string B = "b";
    public const string Unclear = "unclear";
    public const string NoCollision = "none";
}

public sealed record Finding(double Seconds, string Text);

public sealed record KeyFrame(double Seconds, string FileName, string Caption);

/// <summary>A road user the AI followed through the video.</summary>
public sealed class RoadUser
{
    /// <summary>Short reference used in the report: V1, V2… for vehicles, P1… for people.</summary>
    public string Tag { get; set; } = "";
    /// <summary>"white car", "motorcycle", "pedestrian".</summary>
    public string Label { get; set; } = "";
    /// <summary>"vehicle", "two-wheeler" or "person".</summary>
    public string Kind { get; set; } = "";
    /// <summary>Where it was relative to the camera: "ahead, in your lane", "to your left", "in view".</summary>
    public string Where { get; set; } = "";
    public bool Involved { get; set; }
    /// <summary>"struck by your vehicle", "collided", "hit in a follow-on collision", "not involved".</summary>
    public string Role { get; set; } = "";
    /// <summary>What it was doing: "moved into your lane from the left at 0:03.1", "moving much faster than other traffic".</summary>
    public List<string> Notes { get; set; } = [];
    public double FirstSeconds { get; set; }
    public double LastSeconds { get; set; }
}

/// <summary>One party's share of the fault, with the reasons behind it.</summary>
public sealed class PartyShare
{
    public string Party { get; set; } = "";
    public int Percent { get; set; }
    public bool IsCamera { get; set; }
    public List<string> Reasons { get; set; } = [];
}

/// <summary>The result of one Crash Review AI run. Saved as JSON with the review.</summary>
public sealed class CrashReport
{
    public string Verdict { get; set; } = Verdicts.NoCollision;
    /// <summary>0–100.</summary>
    public int Confidence { get; set; }
    /// <summary>One line, e.g. "The camera vehicle was most likely at fault".</summary>
    public string Headline { get; set; } = "";
    /// <summary>A sentence or two explaining the call.</summary>
    public string Summary { get; set; } = "";
    /// <summary>"Rear-end", "Lane change / cut-in", "Head-on", …</summary>
    public string CollisionType { get; set; } = "";
    /// <summary>"Dashcam (moving)" or "Fixed camera".</summary>
    public string CameraKind { get; set; } = "";
    public bool SplitScreen { get; set; }
    public double? ImpactSeconds { get; set; }
    public double DurationSeconds { get; set; }
    /// <summary>Display names: [camera vehicle or A, other party or B].</summary>
    public List<string> Parties { get; set; } = [];
    /// <summary>Fault share for each party in <see cref="Parties"/>, summing to 100 (empty when no collision).</summary>
    public List<int> FaultShares { get; set; } = [];
    public List<Finding> Findings { get; set; } = [];
    /// <summary>Every party's share of the fault (all involved road users, plus the camera vehicle when it was moving).</summary>
    public List<PartyShare> Shares { get; set; } = [];
    /// <summary>Was the camera's own vehicle part of the collision?</summary>
    public bool CameraInvolved { get; set; }
    /// <summary>Where the collision happened, as seen from the camera ("in the lane to your left, a short distance ahead").</summary>
    public string Location { get; set; } = "";
    /// <summary>Every road user the AI followed long enough to describe.</summary>
    public List<RoadUser> RoadUsers { get; set; } = [];
    /// <summary>What the camera vehicle's driver did: speed, braking, reaction, lane keeping.</summary>
    public List<Finding> DriverNotes { get; set; } = [];
    /// <summary>Things the camera vehicle's driver did that contributed ("following too closely").</summary>
    public List<string> DriverIssues { get; set; } = [];
    /// <summary>What the cabin camera showed (split-screen recordings): a phone in hand, eyes off the road.</summary>
    public List<Finding> CabinNotes { get; set; } = [];
    /// <summary>A plain-language paragraph describing the whole incident.</summary>
    public string Narrative { get; set; } = "";
    /// <summary>The camera type used: "auto" or what the person picked.</summary>
    public string CameraSetting { get; set; } = "auto";
    public List<KeyFrame> KeyFrames { get; set; } = [];
    /// <summary>Camera vehicle's forward-motion index over the video (relative, not mph).</summary>
    public List<double> SpeedTrace { get; set; } = [];
    public double TraceStepSeconds { get; set; }
    public int VehiclesTracked { get; set; }
    public double AnalysisSeconds { get; set; }
    public string Engine { get; set; } = CrashAnalyzer.EngineName;
}
