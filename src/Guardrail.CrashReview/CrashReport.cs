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
    public List<KeyFrame> KeyFrames { get; set; } = [];
    /// <summary>Camera vehicle's forward-motion index over the video (relative, not mph).</summary>
    public List<double> SpeedTrace { get; set; } = [];
    public double TraceStepSeconds { get; set; }
    public int VehiclesTracked { get; set; }
    public double AnalysisSeconds { get; set; }
    public string Engine { get; set; } = CrashAnalyzer.EngineName;
}
