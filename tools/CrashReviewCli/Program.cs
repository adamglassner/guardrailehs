using System.Text.Json;
using Guardrail.CrashAi;

// Usage: CrashReviewCli <output folder> <video> [<video> ...]
if (args.Length < 2) { Console.Error.WriteLine("Usage: CrashReviewCli <output folder> <video>..."); return 2; }
string outRoot = args[0];
int failures = 0;
foreach (var video in args.Skip(1))
{
    string name = Path.GetFileNameWithoutExtension(video);
    Console.WriteLine($"== {name}");
    try
    {
        var report = CrashAnalyzer.Analyze(video, Path.Combine(outRoot, name));
        Console.WriteLine($"   {report.CameraKind} | {report.Verdict} {report.Confidence}% | {report.CollisionType} | impact {report.ImpactSeconds} | {string.Join(" vs ", report.Parties)} | {report.AnalysisSeconds}s");
        Console.WriteLine($"   {report.Headline}. {report.Summary}");
        foreach (var f in report.Findings) Console.WriteLine($"     {f.Seconds,6:0.0}  {f.Text}");
        File.WriteAllText(Path.Combine(outRoot, name, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"   FAILED: {ex}");
    }
}
return failures;
