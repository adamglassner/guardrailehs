using System.Text.Json;
using Guardrail.CrashAi;
using Guardrail.CrashAi.Documents;

// Usage: CrashReviewCli <output folder> [--hint=auto|moving|fixed|handheld] [--docs=a.txt;b.pdf] <video> [<video> ...]
//        CrashReviewCli --docs <document> [<document> ...]      (just the facts read from documents)
// Set GUARDRAIL_DUMP=1 to also write each video's per-frame measurements (samples.json) for debugging.
if (args.Length >= 2 && args[0] == "--probe") return Probe.Run(args[1]);
if (args.Length >= 2 && args[0] == "--docs") return Docs.Run(args.Skip(1));
string hint = args.FirstOrDefault(a => a.StartsWith("--hint="))?["--hint=".Length..] ?? "auto";
var docFiles = (args.FirstOrDefault(a => a.StartsWith("--docs="))?["--docs=".Length..] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
args = args.Where(a => !a.StartsWith("--")).ToArray();
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
        var report = CrashAnalyzer.Analyze(video, dir, cameraHint: hint);
        Console.WriteLine($"   {report.CameraKind} | {report.Verdict} {report.Confidence}% | {report.CollisionType} | impact {report.ImpactSeconds} | camera involved: {report.CameraInvolved} | {report.AnalysisSeconds}s");
        Console.WriteLine($"   {report.Headline}. {report.Summary}");
        Console.WriteLine($"   Where: {report.Location}");
        Console.WriteLine($"   Shares: {string.Join(", ", report.Shares.Select(x => $"{x.Party} {x.Percent}%"))}");
        Console.WriteLine($"   {report.Narrative}");
        foreach (var f in report.Findings) Console.WriteLine($"     {f.Seconds,6:0.0}  {f.Text}");
        foreach (var f in report.DriverNotes.Concat(report.CabinNotes)) Console.WriteLine($"     drv {f.Seconds,6:0.0}  {f.Text}");
        foreach (var u in report.RoadUsers.Where(u => u.Involved)) Console.WriteLine($"     {u.Tag} {u.Label}: {u.Role}; {u.Where}; {string.Join("; ", u.Notes)}");
        if (docFiles.Length > 0)
        {
            var docs = docFiles.Select(f => new AttachedFacts(Path.GetFileName(f), DocumentFacts.Extract(DocumentReader.ReadFile(f)))).ToList();
            var c = CaseAssessment.Combine(report, docs);
            Console.WriteLine($"   With documents: {c.Headline} | {string.Join(", ", c.Shares.Select(x => $"{x.Party} {x.Percent}% (video {x.VideoPercent?.ToString() ?? "-"}, docs {x.DocumentPercent?.ToString() ?? "-"})"))}");
            foreach (var n in c.Notes) Console.WriteLine($"     note: {n}");
        }
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

static class Docs
{
    public static int Run(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            var facts = DocumentFacts.Extract(DocumentReader.ReadFile(f));
            Console.WriteLine($"== {Path.GetFileName(f)} | {facts.Kind} | when: {string.Join(", ", facts.Dates.Concat(facts.Times))} | where: {string.Join(" | ", facts.Locations)} | conditions: {string.Join(", ", facts.Conditions)} | hit-and-run: {facts.HitAndRun}");
            foreach (var i in facts.Injuries) Console.WriteLine($"   injury: {i}");
            foreach (var p in facts.Parties.OrderByDescending(p => p.Score))
                Console.WriteLine($"   {p.Label,-32} score {p.Score:0.00} | struck others {p.Striking} (rear-ended {p.RearEnded}) | was struck {p.Struck} | cited: {string.Join("; ", p.Citations)} | {string.Join(", ", p.Factors.Select(kv => $"{kv.Key} {kv.Value:0.0}"))}");
        }
        return 0;
    }
}

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
            using var full = new OpenCvSharp.Mat(g0.Rows, g0.Cols, OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All(255));
            var all = CameraMotion.GridFeatures(g0, full);
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
