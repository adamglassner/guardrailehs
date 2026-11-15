using System.Text.Json;
using Guardrail.CrashAi;

// Usage: CrashReviewCli <output folder> <video> [<video> ...]
// Set GUARDRAIL_DUMP=1 to also write each video's per-frame measurements (samples.json) for debugging.
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
