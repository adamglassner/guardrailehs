using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>
/// Crash Review AI: give it an accident video, get back who was most likely at fault.
/// Everything runs locally on the CPU — no cloud, no account, no per-video fees.
/// </summary>
public static class CrashAnalyzer
{
    public const string EngineName = "Guardrail Crash Review 2.0 (YOLOX-S + rules)";
    private const string ModelResource = "yolox_s.onnx";
    private static readonly object ModelLock = new();
    private static string? _modelPath;

    /// <summary>Video file types the analyzer accepts.</summary>
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts", ".mts"];

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>True when the detection model is built into this copy of Guardrail.</summary>
    public static bool ModelAvailable => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ModelResource) != null || OverrideModelPath != null;

    /// <summary>For tools and tests: use a model file on disk instead of the embedded one.</summary>
    public static string? OverrideModelPath { get; set; }

    /// <summary>Unpacks the embedded model once to %LocalAppData%\Guardrail\models.</summary>
    public static string ModelPath()
    {
        if (OverrideModelPath != null) return OverrideModelPath;
        lock (ModelLock)
        {
            if (_modelPath != null && File.Exists(_modelPath)) return _modelPath;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModelResource)
                ?? throw new InvalidOperationException("The Crash Review AI model isn't included in this build of Guardrail.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            string hash = Convert.ToHexString(SHA256.HashData(ms.ToArray()))[..12].ToLowerInvariant();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardrail", "models");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"yolox_s-{hash}.onnx");
            if (!File.Exists(path) || new FileInfo(path).Length != ms.Length)
            {
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                File.Move(tmp, path, overwrite: true);
            }
            return _modelPath = path;
        }
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>
    /// Analyzes a video. Key-frame images are written into <paramref name="outputFolder"/>.
    /// Progress reports (stage text, 0..1). <paramref name="cameraHint"/>: "auto", "moving", "fixed" or "handheld".
    /// </summary>
    public static CrashReport Analyze(string videoPath, string outputFolder, IProgress<(string Stage, double Fraction)>? progress = null, CancellationToken cancel = default, string cameraHint = "auto")
    {
        var clock = Stopwatch.StartNew();
        progress?.Report(("Loading the AI model", 0));
        using var detector = new Detector(ModelPath());
        var stage = new InlineProgress(f => progress?.Report(("Watching the video", 0.02 + 0.92 * f)));
        var extraction = Extractor.Run(videoPath, detector, stage, cancel);
        cancel.ThrowIfCancellationRequested();
        progress?.Report(("Measuring how the camera moved", 0.95));
        MotionPass.RemoveFixtures(extraction.Samples);
        double staticFraction = MotionPass.Run(extraction.Samples, cancel);
        progress?.Report(("Working out what happened", 0.98));
        var (report, keys) = Reasoner.Analyze(extraction, staticFraction, cameraHint);
        Directory.CreateDirectory(outputFolder);
        report.KeyFrames = KeyFrames.Write(extraction, keys, report, outputFolder);
        report.AnalysisSeconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
        progress?.Report(("Done", 1));
        return report;
    }
}
