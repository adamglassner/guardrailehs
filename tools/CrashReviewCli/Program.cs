using System.Text.Json;
using Guardrail.CrashAi;

// Usage: CrashReviewCli <output folder> <video> [<video> ...]
// Set GUARDRAIL_DUMP=1 to also write each video's per-frame measurements (samples.json) for debugging.
if (args.Length >= 2 && args[0] == "--probe") return Probe.Run(args[1]);
if (args.Length < 2) { Console.Error.WriteLine("Usage: CrashReviewCli <output folder> <video>..."); return 2; }
string outRoot = args[0];
bool dump = Environment.GetEnvironmentVariable("GUARDRAIL_DUMP") == "1";
int failures = 0;
foreach (var video in args.Skip(1))
{
    string name = Path.GetFileNameWithoutExtension(video);
    string dir = Path.Combine(outRoot, name);
    Directory.CreateDirectory(dir);
    Console.WriteLine($"== {name}");
    try
    {
        var report = CrashAnalyzer.Analyze(video, dir);
        Console.WriteLine($"   {report.CameraKind} | {report.Verdict} {report.Confidence}% | {report.CollisionType} | impact {report.ImpactSeconds} | {string.Join(" vs ", report.Parties)} | {report.AnalysisSeconds}s");
        Console.WriteLine($"   {report.Headline}. {report.Summary}");
        foreach (var f in report.Findings) Console.WriteLine($"     {f.Seconds,6:0.0}  {f.Text}");
        File.WriteAllText(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        if (dump)
        {
            using var detector = new Detector(CrashAnalyzer.ModelPath());
            var ex = Extractor.Run(video, detector, null, CancellationToken.None);
            MotionPass.RemoveFixtures(ex.Samples);
            double staticFrac = MotionPass.Run(ex.Samples, CancellationToken.None);
            var rows = ex.Samples.Select(s => new
            {
                t = Math.Round(s.T, 3), s.Camera.Mag, s.Camera.Tx, s.Camera.Ty, s.Camera.Rot, s.Camera.Expand, s.Camera.Inliers, s.Camera.Points, s.Camera.Cut, s.YellowLine,
                dets = s.Detections.Select(d => new { d.Box.X1, d.Box.Y1, d.Box.X2, d.Box.Y2, d.Score, d.ClassId, d.Extra }),
            });
            File.WriteAllText(Path.Combine(dir, "samples.json"), JsonSerializer.Serialize(new
            {
                staticFrac, ex.Fps, view = new[] { ex.View.X, ex.View.Y, ex.View.Width, ex.View.Height }, ex.SplitScreen, samples = rows,
            }));
        }
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"   FAILED: {ex}");
    }
}
return failures;

static class Probe
{
    /// <summary>Feature-tracking diagnostics for one video: corners found with / without masks, LK successes.</summary>
    public static int Run(string video)
    {
        using var detector = new Detector(CrashAnalyzer.ModelPath());
        var ex = Extractor.Run(video, detector, null, CancellationToken.None);
        MotionPass.RemoveFixtures(ex.Samples);
        var picks = Enumerable.Range(0, 24).Select(k => ex.Samples[(int)(ex.Samples.Count * (k + 0.5) / 24)]).ToList();
        Console.WriteLine($"samples {ex.Samples.Count}");
        foreach (int i in new[] { 40, 60, 75, 76, 77, 78, 80, 120 })
        {
            if (i + 1 >= ex.Samples.Count) break;
            using var g0 = OpenCvSharp.Cv2.ImDecode(ex.Samples[i].Jpeg, OpenCvSharp.ImreadModes.Grayscale);
            using var g1 = OpenCvSharp.Cv2.ImDecode(ex.Samples[i + 1].Jpeg, OpenCvSharp.ImreadModes.Grayscale);
            var all = OpenCvSharp.Cv2.GoodFeaturesToTrack(g0, 300, 0.01, 8, null, 3, false, 0.04);
            var p1 = Array.Empty<OpenCvSharp.Point2f>();
            OpenCvSharp.Cv2.CalcOpticalFlowPyrLK(g0, g1, all, ref p1, out byte[] st, out _, new OpenCvSharp.Size(21, 21), 3);
            OpenCvSharp.Cv2.MeanStdDev(g0, out var mean, out var sd);
            Console.WriteLine($"t={ex.Samples[i].T:0.00} size={g0.Cols}x{g0.Rows} mean={mean.Val0:0.0} sd={sd.Val0:0.0} corners(no mask)={all.Length} lkOk={st.Count(b => b == 1)}");
            foreach (double q in new[] { 0.01, 0.005, 0.002, 0.001 })
                foreach (int bs in new[] { 3, 5 })
                    Console.WriteLine($"   quality={q} block={bs} corners={OpenCvSharp.Cv2.GoodFeaturesToTrack(g0, 300, q, 8, null, bs, false, 0.04).Length}");
            Directory.CreateDirectory("probe");
            File.WriteAllBytes(Path.Combine("probe", $"frame_{i}.jpg"), ex.Samples[i].Jpeg);
        }
        double frac = MotionPass.Run(ex.Samples, CancellationToken.None);
        Console.WriteLine($"static fraction {frac:0.000}");
        foreach (int i in new[] { 40, 60, 75, 76, 77, 78, 80, 120 })
            if (i < ex.Samples.Count) Console.WriteLine($"t={ex.Samples[i].T:0.00} points={ex.Samples[i].Camera.Points} mag={ex.Samples[i].Camera.Mag:0.0000} inl={ex.Samples[i].Camera.Inliers:0.00}");
        return 0;
    }
}
