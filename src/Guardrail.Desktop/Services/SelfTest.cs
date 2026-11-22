using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Guardrail.CrashAi;
using Guardrail.Data;
using Guardrail.Models;
using Guardrail.Views;

namespace Guardrail.Services;

/// <summary>
/// Headless checks run by CI: <c>Guardrail.exe --self-test result.txt</c>.
/// Opens every page in both themes, builds the add/edit dialog, and round-trips the
/// JSON store in a temp folder (never the user's data). Exit code = number of failures.
/// </summary>
public static class SelfTest
{
    public static string? OutputFile { get; set; }
    public static bool IsRunning => OutputFile != null;

    private static readonly StringBuilder Report = new();
    private static int _failed;

    /// <summary>Called by App's unhandled-exception handler while the self-test runs.</summary>
    public static void RecordUnhandled(Exception ex)
    {
        _failed++;
        Report.AppendLine($"FAIL  Unhandled exception — {ex}");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failed++;
        Report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static async Task Step(string name, Action action)
    {
        try
        {
            action();
            // Let layout, data binding and rendering run so XAML errors surface here.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Application.Current.MainWindow?.UpdateLayout();
            Check(name, true);
        }
        catch (Exception ex)
        {
            Check(name, false, ex.ToString());
        }
    }

    // ----- Web links for Crash Review -----

    private static async Task WebVideoChecks()
    {
        var named = new VideoReview { CameraName = "Unit 104" };
        Check("Crash AI: camera vehicle named in the report",
            named.Describe("The camera vehicle drifted across the yellow centre line.") == "Unit 104 drifted across the yellow centre line."
            && named.Describe("The white car moved into the camera vehicle's lane.") == "The white car moved into Unit 104's lane."
            && named.Describe("camera vehicle") == "Unit 104");
        Check("Crash AI: no \"you\" wording, even in older reports",
            new VideoReview().Describe("The black car was ahead of you, in your lane, for 3.0+ s.") == "The black car was ahead of the camera vehicle, in its lane, for 3.0+ s."
            && new VideoReview().Describe("Your vehicle was most likely at fault") == "The camera vehicle was most likely at fault"
            && named.Describe("Your vehicle crossed your path.") == "Unit 104 crossed Unit 104's path.");
        Check("Crash AI: unit numbers become names", VideoReview.NameForUnit("104") == "Unit 104" && VideoReview.NameForUnit("Truck 12") == "Truck 12");

        Check("Web: links recognized", WebVideo.LooksLikeUrl("https://youtu.be/abc") && !WebVideo.LooksLikeUrl("C:\\videos\\a.mp4") && !WebVideo.LooksLikeUrl("hello"));
        Check("Web: Dropbox share link becomes a download", WebVideo.Normalize("https://www.dropbox.com/s/abc/crash.mp4?dl=0").Contains("dl=1"));
        Check("Web: Google Drive share link becomes a download",
            WebVideo.Normalize("https://drive.google.com/file/d/1AbC_dEf/view?usp=sharing") == "https://drive.google.com/uc?export=download&id=1AbC_dEf");
        Check("Web: OneDrive link becomes a download", WebVideo.Normalize("https://1drv.ms/v/s!Abc123").EndsWith("download=1"));
        Check("Web: video found inside a page",
            WebVideo.FindVideoInHtml("<html><head><meta property=\"og:video\" content=\"/clips/crash.mp4\"></head></html>", new Uri("https://portal.example.com/share/9")) == "https://portal.example.com/clips/crash.mp4"
            && WebVideo.FindVideoInHtml("<video controls src=\"https://cdn.example.com/a.webm\"></video>", new Uri("https://x.example.com/")) == "https://cdn.example.com/a.webm"
            && WebVideo.FindVideoInHtml("<p>no video</p>", new Uri("https://x.example.com/")) is null);
        Check("Web: friendly names", WebVideo.DisplayName("https://www.youtube.com/watch?v=abc123") == "youtube.com · v=abc123");

        // Real downloads from a tiny local web server: a direct video link and a page with the video in it.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop.Token); } catch { break; }
                using (client)
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, leaveOpen: true);
                    string first = await reader.ReadLineAsync() ?? "";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    bool page = first.Contains("/page");
                    byte[] body = page ? System.Text.Encoding.UTF8.GetBytes("<html><video src=\"/clip.mp4\"></video></html>") : payload;
                    string head = $"HTTP/1.1 200 OK\r\nContent-Type: {(page ? "text/html" : "video/mp4")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));
                    await stream.WriteAsync(body);
                }
            }
        });
        string dir = Path.Combine(Path.GetTempPath(), "guardrail-web-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string direct = await WebVideo.DownloadAsync($"http://127.0.0.1:{port}/clip.mp4", Path.Combine(dir, "a"), null, () => false, CancellationToken.None);
            Check("Web: direct video link downloads", File.ReadAllBytes(direct).SequenceEqual(payload), direct);
            string fromPage = await WebVideo.DownloadAsync($"http://127.0.0.1:{port}/page", Path.Combine(dir, "b"), null, () => false, CancellationToken.None);
            Check("Web: video inside a page downloads", File.ReadAllBytes(fromPage).SequenceEqual(payload), fromPage);
        }
        catch (Exception ex) { Check("Web: downloads from a link", false, ex.ToString()); }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await server; } catch { /* stopped */ }
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    // ----- PIN / password protection -----

    /// <summary>Encryption end to end in a temporary folder (never the real data).</summary>
    private static void LockChecks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "guardrail-lock-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        DataLock.FolderOverride = dir;
        try
        {
            string records = Path.Combine(dir, "actions.json"), doc = Path.Combine(dir, "sds", "sheet.txt"), clip = Path.Combine(dir, "accidents", "clip.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(doc)!);
            Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
            new JsonStore<ActionItem>(dir, "actions").Add(new ActionItem { Title = "Secret guard repair" });
            File.WriteAllText(doc, "hello");
            File.WriteAllText(clip, "video bytes");

            Check("Lock: PIN rules", DataLock.Validate("12", "PIN") != null && DataLock.Validate("2468", "PIN") == null && DataLock.Validate("short", "Password") != null);
            string recovery = DataLock.Enable("2468", "PIN", 10);
            Check("Lock: turning on encrypts records", DataLock.IsEncryptedFile(records) && !File.ReadAllText(records).Contains("Secret"));
            Check("Lock: documents encrypted, videos left as they are", DataLock.IsEncryptedFile(doc) && !DataLock.IsEncryptedFile(clip));
            Check("Lock: encrypted records load while unlocked", new JsonStore<ActionItem>(dir, "actions").Items.SingleOrDefault()?.Title == "Secret guard repair");

            DataLock.Forget();
            var locked = new JsonStore<ActionItem>(dir, "actions");
            locked.Add(new ActionItem { Title = "must not overwrite" });
            Check("Lock: a locked store reads nothing and never overwrites", locked.Items.Count == 1 && DataLock.IsEncryptedFile(records));
            Check("Lock: wrong PIN is refused", !DataLock.Unlock("1111") && !DataLock.IsOpen);
            Check("Lock: right PIN unlocks", DataLock.Unlock("2468") && new JsonStore<ActionItem>(dir, "actions").Items.Count == 1);
            DataLock.Forget();
            Check("Lock: recovery key unlocks (any case and spacing)", DataLock.UnlockWithRecoveryKey(recovery.ToLowerInvariant().Replace("-", " ")));
            DataLock.ChangeSecret("correct horse", "Password");
            DataLock.Forget();
            Check("Lock: changed password works, old PIN doesn't", !DataLock.Unlock("2468") && DataLock.Unlock("correct horse"));
            DataLock.SetAutoLock(30);
            Check("Lock: auto-lock setting saved", DataLock.Info?.AutoLockMinutes == 30);
            DataLock.Disable();
            Check("Lock: turning off decrypts everything", !DataLock.IsEnabled && File.ReadAllText(records).Contains("Secret") && File.ReadAllText(doc) == "hello");
        }
        catch (Exception ex) { Check("Lock: encryption round trip", false, ex.ToString()); }
        finally
        {
            DataLock.Forget();
            DataLock.FolderOverride = null;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static async Task LockAndLocationUiChecks(MainWindow window)
    {
        await Step("Lock: set-up dialog builds", () => { var d = new SetLockDialog(change: false); d.Show(); d.UpdateLayout(); d.Close(); });
        await Step("Lock: recovery key dialog builds", () => { var d = new RecoveryKeyDialog("ABCDE-FGHJK-LMNPQ-RSTUV-WXYZ2") { ForceClose = true }; d.Show(); d.UpdateLayout(); d.Close(); });
        await Step("Lock: unlock window builds", () => { var w = new UnlockWindow(); w.Show(); w.UpdateLayout(); w.Close(); });

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{theme}] Apply theme", () => App.ApplyTheme(theme));
            await Step($"[{theme}] Page: Locations", window.ShowLocations);
        }
        await Step("Locations: Location field offers the list and still accepts typing", () =>
        {
            var dialog = new FormDialog("Self-test", "Suggestions", new[]
            {
                new FormField { Key = "location", Label = "Location", Value = "Typed Site", Suggestions = ["Dallas Plant", "North Terminal"] },
            }, allowDelete: false, saveText: "Save");
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });
        var loc = new Location { Name = "Dallas Plant", Region = "South", District = "North Texas" };
        Check("Locations: hierarchy text", loc.Hierarchy == "South › North Texas" && loc.IsActive && loc.Number.StartsWith("LOC"));
    }

    // ----- Guardrail Server sync (no server needed) -----

    private static async Task ServerSyncChecks()
    {
        await Step("Server: connect dialog builds", () => { var d = new ConnectServerDialog(); d.Show(); d.UpdateLayout(); d.Close(); });
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "gr-synctest-" + Guid.NewGuid().ToString("N")[..6]);
            var store = new JsonStore<ActionItem>(folder, "sync-test");
            ISyncStore s = store;
            var changes = new List<(string, Guid, bool)>();
            void Hook(string n, Guid id, bool del) { if (n == "sync-test") changes.Add((n, id, del)); }
            SyncHooks.LocalChange += Hook;
            var a = new ActionItem { Title = "Fix the guard" };
            store.Add(a);
            string json = s.Serialize(a.Id)!;
            Check("Server: local add is queued", changes.Count == 1 && !changes[0].Item3);
            Check("Server: record serializes", json.Contains("Fix the guard") && s.ModifiedMs(a.Id) > 0);
            s.SetNumber(a.Id, 42);
            Check("Server: server number applied", store.Items[0].Seq == 42 && store.Items[0].Number.EndsWith("0042"));
            var remote = new ActionItem { Title = "From a teammate", Seq = 43 };
            s.ApplyRemote([(remote.Id, System.Text.Json.JsonSerializer.Serialize(remote)), (a.Id, null)]);
            Check("Server: remote changes applied without being queued", store.Items.Count == 1 && store.Items[0].Title == "From a teammate" && changes.Count == 1);
            store.Delete(store.Items[0]);
            Check("Server: local delete is queued", changes.Count == 2 && changes[1].Item3);
            SyncHooks.LocalChange -= Hook;
            SyncHooks.Stores.Remove(store);
            Check("Server: every data file is a sync collection, once", SyncHooks.Stores.Select(x => x.Name).All(Guardrail.Sync.Protocol.IsValidCollection)
                && SyncHooks.Stores.Select(x => x.Name).Distinct().Count() == SyncHooks.Stores.Count,
                string.Join(", ", SyncHooks.Stores.Select(x => x.Name)));
            Check("Server: address shorthand", Guardrail.Sync.SyncApiClient.NormalizeUrl("192.168.1.20") == "http://192.168.1.20:5080/");
            try { Directory.Delete(folder, true); } catch { }
        }
        catch (Exception ex) { Check("Server: sync hooks", false, ex.ToString()); }
    }

    // ----- Safety References -----

    private static async Task ReferencesChecks(MainWindow window)
    {
        try
        {
            Check("ERG: data loads", ErgData.Materials.Count > 2500 && ErgData.Guides.Count == 64, $"{ErgData.Materials.Count} materials, {ErgData.Guides.Count} guides");
            var gasoline = ErgData.Search("1203");
            Check("ERG: 1203 is gasoline, guide 128", gasoline.Any(m => m.Name == "Gasoline" && m.Guide == "128"), string.Join("; ", gasoline.Select(m => m.Guide + " " + m.Name)));
            Check("ERG: UN prefix accepted", ErgData.Search("UN1203").Count == gasoline.Count && ErgData.Search("na 1993").Any(m => m.Name == "Diesel fuel"));
            var propane = ErgData.Search("propane");
            Check("ERG: name search finds propane (1075, guide 115)", propane.Count > 0 && propane[0].Id == "1075" && propane[0].Guide == "115", propane.FirstOrDefault()?.ToString() ?? "none");
            Check("ERG: multi-word search", ErgData.Search("sulfuric acid").Any(m => m.Id == "1830"));
            var chlorine = ErgData.DistancesFor("1017");
            Check("ERG: chlorine has Table 1 distances (large spill → Table 3)", chlorine.Count == 1 && chlorine[0].Small[0] == "60 m (200 ft)" && chlorine[0].LargeUsesTable3,
                chlorine.Count > 0 ? string.Join(", ", chlorine[0].Small.Concat(chlorine[0].Large)) : "none");
            var h2s = ErgData.DistancesFor("1053");
            Check("ERG: hydrogen sulfide large-spill distances", h2s.Count > 0 && h2s[0].Large.Length == 3 && h2s[0].Large[0] == "400 m (1250 ft)");
            Check("ERG: gasoline is not highlighted", !ErgData.IsHighlighted("1203"));
            Check("ERG: water-reactive table", ErgData.WaterReactiveFor("1360").Any(w => w.Gases == "PH3"));
            var g128 = ErgData.Guide("128");
            Check("ERG: guide 128 text", g128 is not null && g128.Title.Contains("Flammable Liquids") && g128.Lines.Any(l => l.Kind == 'H' && l.Text == "EMERGENCY RESPONSE") && g128.Lines.Count(l => l.Kind == 'B') > 20,
                g128?.Title ?? "missing");
            Check("ERG: guide 121 is intentionally blank", ErgData.Guide("121")?.IsBlank == true);
            Check("ERG: polymerization flag", ErgData.Search("1010").All(m => m.MayPolymerize && m.GuideNumber == "116"));
            Check("ERG: general first aid", ErgData.GeneralFirstAid.Count >= 10);
            Check("References: every placard has a real guide", ReferencesView.Placards.All(p => ErgData.Guide(p.Guide) is { IsBlank: false }));
        }
        catch (Exception ex) { Check("ERG: data", false, ex.ToString()); }

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{theme}] Apply theme", () => App.ApplyTheme(theme));
            await Step($"[{theme}] Page: Safety References", () => window.GoTo("references"));
            if (window.PageHost.Content is not ReferencesView view) { Check("References: page opens", false); continue; }
            foreach (var (key, title, _) in ReferencesView.Tabs)
                await Step($"[{theme}] References: {title}", () => view.Select(key));
            await Step($"[{theme}] References: chlorine lookup with distances", () => view.Lookup("1017"));
            await Step($"[{theme}] References: name search", () => view.Lookup("acid"));
            await Step($"[{theme}] References: guide from a placard", () => view.OpenGuide("153"));
            await Step($"[{theme}] References: blank guide", () => view.OpenGuide("121"));
            await Step($"[{theme}] References: no matches", () => view.Lookup("zzzz not a chemical"));
            await Step($"[{theme}] References: cleared search", () => view.Lookup(""));
        }
    }

    // ----- Crash Review AI -----

    private static Sample FakeSample(double t, CameraStep cam, params Detection[] dets) =>
        new() { T = t, Camera = cam, Detections = dets.ToList(), Jpeg = [] };

    /// <summary>Scripted scenes for the fault rules, the real engine on a generated video, and the report dialog.</summary>
    private static async Task CrashAiChecks()
    {
        // 1) Dashcam rear-end: a car ahead in our lane grows until we hit it; jolt, then we stop.
        try
        {
            var samples = new List<Sample>();
            for (int i = 0; i <= 70; i++)
            {
                double t = i / 10.0, k = Math.Min(t, 5.0);
                double w = 0.12 * Math.Exp(0.33 * k), h = 0.08 * Math.Exp(0.33 * k), y2 = 0.55 + 0.4 * k / 5.0;
                var cam = new CameraStep
                {
                    Mag = t < 5.2 ? 0.003 + 0.0002 * Math.Sin(i) : 0.00005, Expand = t < 5.2 ? 0.002 : 0, Tx = i == 50 ? 0.03 : 0.0004 * Math.Sin(i * 1.7),
                    Inliers = 0.8, Points = 120,
                };
                samples.Add(FakeSample(t, cam, new Detection(new Guardrail.CrashAi.Box(0.5 - w / 2, y2 - h, 0.5 + w / 2, y2), 0.9f, Classes.Car)));
            }
            var (rep, plan) = Reasoner.Analyze(new Extraction { Samples = samples, Duration = 7 }, 0.1);
            Check("Crash AI: rear-ending the car ahead is our fault", rep.Verdict == Verdicts.Camera && rep.CollisionType == "Rear-end",
                $"{rep.Verdict} {rep.CollisionType} {rep.Confidence}% at {rep.ImpactSeconds}");
            Check("Crash AI: impact time found", rep.ImpactSeconds is > 4.5 and < 5.3, rep.ImpactSeconds?.ToString() ?? "none");
            Check("Crash AI: key moments planned", plan.Moments.Count >= 3 && rep.FaultShares.Sum() == 100);
            Check("Crash AI: every party gets a share", rep.Shares.Count >= 2 && rep.Shares.Sum(x => x.Percent) == 100 && rep.Shares.Any(x => x.IsCamera),
                string.Join(", ", rep.Shares.Select(x => $"{x.Party} {x.Percent}%")));
            Check("Crash AI: camera vehicle involved, road users listed", rep.CameraInvolved && rep.RoadUsers.Any(u => u.Involved), $"{rep.RoadUsers.Count} road users");
            Check("Crash AI: detailed narrative and driver notes", rep.Narrative.Length > 40 && rep.DriverNotes.Count > 0 && rep.Location.Length > 0, rep.Narrative);
        }
        catch (Exception ex) { Check("Crash AI: rear-end scene", false, ex.ToString()); }

        // 2) Fixed camera: car A drives into stopped car B.
        try
        {
            var samples = new List<Sample>();
            for (int i = 0; i <= 60; i++)
            {
                double t = i / 10.0;
                double ax = Math.Min(0.1 + 0.12 * t, 0.46);
                var a = new Detection(new Guardrail.CrashAi.Box(ax, 0.6, ax + 0.15, 0.7), 0.9f, Classes.Car);
                var b = new Detection(new Guardrail.CrashAi.Box(0.5, 0.6, 0.65, 0.7), 0.9f, Classes.Car);
                samples.Add(FakeSample(t, new CameraStep { Points = 300, Inliers = 1 }, a, b));
            }
            var (rep, _) = Reasoner.Analyze(new Extraction { Samples = samples, Duration = 6 }, 0.9);
            Check("Crash AI: fixed camera, moving car hits a stopped car", rep.CameraKind == "Fixed camera" && rep.Verdict == Verdicts.A,
                $"{rep.CameraKind} {rep.Verdict} {rep.CollisionType} {rep.Confidence}%");
            Check("Crash AI: a fixed camera is never a party", !rep.CameraInvolved && rep.Shares.All(x => !x.IsCamera) && rep.Shares.Sum(x => x.Percent) == 100,
                string.Join(", ", rep.Shares.Select(x => $"{x.Party} {x.Percent}%")));
        }
        catch (Exception ex) { Check("Crash AI: fixed camera scene", false, ex.ToString()); }

        // 3) Nothing happening: no collision.
        try
        {
            var samples = Enumerable.Range(0, 50).Select(i => FakeSample(i / 10.0, new CameraStep { Mag = 0.003, Expand = 0.002, Points = 100, Inliers = 0.9 },
                new Detection(new Guardrail.CrashAi.Box(0.45, 0.5, 0.55, 0.58), 0.9f, Classes.Car))).ToList();
            var (rep, _) = Reasoner.Analyze(new Extraction { Samples = samples, Duration = 5 }, 0.1);
            Check("Crash AI: steady traffic is not a collision", rep.Verdict == Verdicts.NoCollision, rep.Verdict);
        }
        catch (Exception ex) { Check("Crash AI: quiet scene", false, ex.ToString()); }

        // 3b) Documents: facts out of a police report, and the combined call.
        try
        {
            const string police = "PEACE OFFICER'S CRASH REPORT\nCrash Date: 03/14/2025   Crash Time: 4:37 PM\nLocation: Intersection of W Main St and N Oak Ave, Springfield\n" +
                "Weather: Rain\nUnit 1: 2019 Ford F-150, White.\nUnit 2: 2021 Toyota Camry, Gray.\n" +
                "Narrative: Unit 2 disregarded the red signal and entered the intersection. The front of Unit 1 struck the right side of Unit 2. " +
                "Driver of Unit 2 admitted to looking at her cell phone.\nContributing Factors: Unit 2 - Disregard Stop and Go Signal; Driver Inattention.\n" +
                "Citations: Unit 2 driver was cited for running a red light.\nInjuries: Unit 2 driver - possible injury. Unit 1 driver - not injured.";
            var facts = Guardrail.CrashAi.Documents.DocumentFacts.Extract(police);
            var u2 = facts.Parties.FirstOrDefault(p => p.Key == "unit 2");
            var u1 = facts.Parties.FirstOrDefault(p => p.Key == "unit 1");
            Check("Crash AI documents: police report recognised", facts.Kind == "police report", facts.Kind);
            Check("Crash AI documents: citation and violations found", u2 is { Citations.Count: > 0 } && u2.Factors.ContainsKey("ran a red light") && u2.Score > (u1?.Score ?? 0),
                u2 is null ? "no unit 2" : $"{u2.Label}: {string.Join(", ", u2.Factors.Keys)}; cited {u2.Citations.Count}");
            Check("Crash AI documents: location, time and weather", facts.Locations.Any(l => l.Contains("Main St")) && facts.Times.Count > 0 && facts.Conditions.Contains("rain"),
                string.Join(" | ", facts.Locations));
            var video = new CrashReport
            {
                Verdict = Verdicts.Unclear, Confidence = 40, Headline = "Fault is shared or unclear", CollisionType = "Crossing paths (intersection / angle)",
                Shares = [new PartyShare { Party = "Camera vehicle", Percent = 50, IsCamera = true }, new PartyShare { Party = "V1 gray car", Percent = 50 }],
                RoadUsers = [new RoadUser { Tag = "V1", Label = "gray car", Kind = "vehicle", Involved = true }],
            };
            var combined = Guardrail.CrashAi.Documents.CaseAssessment.Combine(video, [new("report.txt", facts)], "unit 1");
            var top = combined.Shares.FirstOrDefault();
            Check("Crash AI documents: the report moves fault onto the cited driver", top is { IsCamera: false } && top.Party == "V1 gray car" && top.Percent >= 60 && combined.Shares.Sum(x => x.Percent) == 100,
                string.Join(", ", combined.Shares.Select(x => $"{x.Party} {x.Percent}%")));
            Check("Crash AI documents: rich text reader", Guardrail.CrashAi.Documents.DocumentReader.RtfToText(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}Unit 2 ran the red light.\par}").Contains("Unit 2 ran the red light"));
            Check("Crash AI documents: web page reader", Guardrail.CrashAi.Documents.DocumentReader.HtmlToText("<html><head><title>Crash</title></head><body><nav>menu</nav><p>A blue Honda Civic rear-ended a minivan.</p></body></html>").Text.Contains("Honda Civic"));
        }
        catch (Exception ex) { Check("Crash AI documents", false, ex.ToString()); }

        // 4) The real engine (model + OpenCV + video decoding) on a generated video.
        Check("Crash AI: model is built in", CrashAnalyzer.ModelAvailable);
        string dir = Path.Combine(Path.GetTempPath(), "guardrail-crash-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            string video = Path.Combine(dir, "synthetic.avi");
            using (var writer = new OpenCvSharp.VideoWriter(video, OpenCvSharp.FourCC.MJPG, 15, new OpenCvSharp.Size(640, 360)))
            {
                Check("Crash AI: can write a test video", writer.IsOpened());
                for (int f = 0; f < 45; f++)
                {
                    using var frame = new OpenCvSharp.Mat(360, 640, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(90, 110, 120));
                    OpenCvSharp.Cv2.Rectangle(frame, new OpenCvSharp.Rect(40 + f * 6, 200, 60, 40), new OpenCvSharp.Scalar(30, 30, 200), -1);
                    OpenCvSharp.Cv2.Line(frame, new OpenCvSharp.Point(320, 360), new OpenCvSharp.Point(320 + f, 180), new OpenCvSharp.Scalar(255, 255, 255), 3);
                    writer.Write(frame);
                }
            }
            var report = await Task.Run(() => CrashAnalyzer.Analyze(video, Path.Combine(dir, "out")));
            Check("Crash AI: engine analyzes a video end to end", report.DurationSeconds > 2 && report.Engine.Length > 0,
                $"{report.Verdict} · {report.DurationSeconds}s · {report.AnalysisSeconds}s to analyze");
        }
        catch (Exception ex) { Check("Crash AI: engine on a generated video", false, ex.ToString()); }
        finally { try { Directory.Delete(dir, true); } catch { /* ignore */ } }

        // 5) Report dialog with every section.
        await Step("Crash AI: report dialog builds and shows", () =>
        {
            var review = new VideoReview
            {
                VideoPath = @"C:\videos\dashcam.mp4", State = ReviewState.Done, AnalyzedAt = DateTime.Now,
                Report = new CrashReport
                {
                    Verdict = Verdicts.Camera, Confidence = 87, Headline = "The camera vehicle was most likely at fault",
                    Summary = "The camera vehicle drifted across the yellow centre line into the oncoming lane.",
                    CollisionType = "Head-on (crossed the centre line)", CameraKind = "Dashcam (moving)", SplitScreen = true,
                    ImpactSeconds = 8.5, DurationSeconds = 21, Parties = ["camera vehicle", "white truck"], FaultShares = [94, 6],
                    Findings = [new(5.5, "The yellow centre line was on the camera vehicle's left."), new(7.5, "The camera vehicle drifted across it."), new(8.5, "Head-on.")],
                    KeyFrames = [new(6.5, "key1.jpg", "2 seconds before"), new(8.5, "key3.jpg", "Impact")],
                    SpeedTrace = Enumerable.Range(0, 100).Select(i => 3.0 + Math.Sin(i / 9.0)).ToList(), TraceStepSeconds = 0.2,
                    Shares = [new PartyShare { Party = "Camera vehicle", Percent = 95, IsCamera = true, Reasons = ["crossed the centre line"] }, new PartyShare { Party = "V1 white truck", Percent = 5 }],
                    CameraInvolved = true, Location = "directly ahead in the camera vehicle's lane, at the front of the camera vehicle",
                    RoadUsers = [new RoadUser { Tag = "V1", Label = "white truck", Kind = "vehicle", Involved = true, Role = "struck by the camera vehicle", Where = "ahead", Notes = ["oncoming"] },
                                 new RoadUser { Tag = "V2", Label = "red car", Kind = "vehicle", Role = "not involved", Where = "to the left of the camera vehicle" }],
                    DriverNotes = [new(6.0, "The camera vehicle was travelling at a steady speed."), new(8.0, "No braking was seen before the impact.")],
                    DriverIssues = ["crossed the centre line"], CabinNotes = [new(1.0, "The cab camera didn't show a phone in the 10 seconds before the collision.")],
                    Narrative = "At 0:08.5 the video shows a head-on collision. Involved: the camera vehicle, V1 (white truck).",
                },
                Documents = [new ReviewDocument { Title = "Police report.pdf", Text = "Unit 1 crossed the center line and struck Unit 2. Unit 1 driver was cited for failure to maintain lane." }],
            };
            var dialog = new CrashReportDialog(review);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });
    }

    public static async Task<int> RunAsync(MainWindow window)
    {
        Report.AppendLine($"Guardrail EHS self-test {DateTime.Now:u}");

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{theme}] Apply theme", () => App.ApplyTheme(theme));
            foreach (var module in ModuleCatalog.Toolbar)
                await Step($"[{theme}] Page: {module.Title}", () => window.GoTo(module.Key));
            foreach (var section in ModuleCatalog.FleetSections)
                await Step($"[{theme}] Fleet Hub: {section.Title}", () => window.GoTo(section.Key));
            await Step($"[{theme}] Page: Settings", window.ShowSettings);
            await Step($"[{theme}] Actions filtered to Overdue", () => window.OpenModule("actions", new ActionsView("Overdue")));
        }

        // The shared add/edit dialog with every field type.
        await Step("Form dialog builds and shows", () =>
        {
            var dialog = new FormDialog("Self-test", "Every field type", new[]
            {
                new FormField { Key = "t", Label = "Text", Required = true, Value = "hello" },
                new FormField { Key = "m", Label = "Multi-line", Kind = FieldKind.MultiLine, Value = "a\nb" },
                new FormField { Key = "d", Label = "Date", Kind = FieldKind.Date, Value = (DateTime?)DateTime.Today },
                new FormField { Key = "c", Label = "Choice", Kind = FieldKind.Choice, Options = ["A", "B"], Value = "B" },
                new FormField { Key = "n", Label = "Number", Kind = FieldKind.Number, Value = 2500.5m },
                new FormField { Key = "f", Label = "File", Kind = FieldKind.File, Value = @"C:\example\policy.pdf" },
            }, allowDelete: true);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });

        // The inspection checklist window.
        await Step("Checklist window builds and shows", () =>
        {
            var starter = InspectionTemplate.Starters()[0];
            var inspection = new Inspection
            {
                TemplateName = starter.Name,
                Location = "Self-test",
                Items = starter.Items.Select(t => new ChecklistItem { Text = t }).ToList(),
            };
            inspection.Items[0].Result = ChecklistItem.Fail;
            var window = new ChecklistWindow(inspection);
            window.Show();
            window.UpdateLayout();
            window.Close();
        });

        // JSON store round trip in a temp folder.
        var temp = Path.Combine(Path.GetTempPath(), "guardrail-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new JsonStore<ActionItem>(temp, "actions");
            var a = new ActionItem { Title = "Fix guard rail", DueDate = DateTime.Today.AddDays(-1) };
            store.Add(a);
            Check("Store: first record numbered ACT-0001", a.Number == "ACT-0001", a.Number);
            Check("Store: past due date shows Overdue", a.DisplayStatus == Status.Overdue, a.DisplayStatus);
            store.Add(new ActionItem { Title = "Second" });
            var reloaded = new JsonStore<ActionItem>(temp, "actions");
            Check("Store: reload keeps both records", reloaded.Items.Count == 2, reloaded.Items.Count.ToString());
            Check("Store: reload keeps fields", reloaded.Items[0].Title == "Fix guard rail" && reloaded.Items[0].DueDate == a.DueDate);
            var first = reloaded.Items[0];
            first.Status = Status.Closed;
            reloaded.Update(first);
            reloaded.Delete(reloaded.Items[1]);
            var again = new JsonStore<ActionItem>(temp, "actions");
            Check("Store: update + delete persisted", again.Items.Count == 1 && again.Items[0].IsClosed);

            var t = new TrainingRecord { Employee = "Pat", Course = "Forklift", ExpiresOn = DateTime.Today.AddDays(10) };
            Check("Training: expiring in 10 days is Expiring soon", t.DisplayStatus == Status.ExpiringSoon, t.DisplayStatus);
            t.ExpiresOn = DateTime.Today.AddDays(-1);
            Check("Training: past expiry is Expired", t.DisplayStatus == Status.Expired, t.DisplayStatus);

            // Regulatory Compliance
            var yearly = new ComplianceItem { Title = "Stormwater permit", Frequency = "Yearly", DueDate = DateTime.Today.AddDays(-40) };
            Check("Compliance: past due is Overdue", yearly.DisplayStatus == Status.Overdue, yearly.DisplayStatus);
            yearly.MarkDone(DateTime.Today);
            Check("Compliance: yearly rolls forward past today", !yearly.IsCompleted && yearly.DueDate > DateTime.Today
                  && yearly.DueDate <= DateTime.Today.AddYears(1), yearly.DueDate.ToString("yyyy-MM-dd"));
            var monthly = new ComplianceItem { Frequency = "Monthly", DueDate = DateTime.Today.AddMonths(-3) };
            monthly.MarkDone(DateTime.Today);
            Check("Compliance: missed months skip to the next one ahead", monthly.DueDate > DateTime.Today
                  && monthly.DueDate <= DateTime.Today.AddMonths(1), monthly.DueDate.ToString("yyyy-MM-dd"));
            var once = new ComplianceItem { Frequency = "One-time", DueDate = DateTime.Today.AddDays(10) };
            Check("Compliance: due in 10 days is Due soon", once.DisplayStatus == Status.DueSoon, once.DisplayStatus);
            once.MarkDone(DateTime.Today);
            Check("Compliance: one-time item completes", once.IsCompleted && once.DisplayStatus == Status.Completed);

            // Claims Register
            var claim = new Claim { Claimant = "Pat", Reserve = 5000m, Paid = 1250.50m };
            Check("Claims: incurred = paid + open reserve", claim.Incurred == 6250.50m, claim.Incurred.ToString());
            claim.Status = Status.Closed;
            Check("Claims: closed claim drops its reserve", claim.Incurred == 1250.50m, claim.Incurred.ToString());
            var claims = new JsonStore<Claim>(temp, "claims");
            claims.Add(new Claim { Claimant = "Lee", Reserve = 1234.56m });
            var claimsAgain = new JsonStore<Claim>(temp, "claims");
            Check("Claims: money survives save/load", claimsAgain.Items[0].Reserve == 1234.56m && claimsAgain.Items[0].Number == "CLM-0001");

            // Library: the file is copied in, and replacing it leaves one file
            var srcA = Path.Combine(temp, "policy-v1.txt");
            var srcB = Path.Combine(temp, "policy-v2.txt");
            File.WriteAllText(srcA, "v1");
            File.WriteAllText(srcB, "v2");
            var docId = Guid.NewGuid();
            var stored1 = Editors.StoreLibraryFile(docId, srcA);
            var stored2 = Editors.StoreLibraryFile(docId, srcB);
            var docFolder = Path.Combine(DataStore.LibraryFolder, docId.ToString("N"));
            Check("Library: file copied into the data folder", File.Exists(Path.Combine(DataStore.Folder, stored2))
                  && File.ReadAllText(Path.Combine(DataStore.Folder, stored2)) == "v2", stored2);
            Check("Library: replacing the file keeps only the new one", Directory.GetFiles(docFolder).Length == 1 && stored1 != stored2);
            try { Directory.Delete(docFolder, recursive: true); } catch { /* ignore */ }
            var doc = new LibraryDocument { ReviewDue = DateTime.Today.AddDays(-1) };
            Check("Library: past review date is Review overdue", doc.DisplayStatus == Status.ReviewOverdue, doc.DisplayStatus);

            // Monthly Data
            Check("Monthly: any day maps to the 1st", MonthlyEntry.FirstOfMonth(new DateTime(2026, 3, 17)) == new DateTime(2026, 3, 1));
            var months = new JsonStore<MonthlyEntry>(temp, "monthly");
            months.Add(new MonthlyEntry { Month = new DateTime(2026, 3, 1), Location = "Plant", HoursWorked = 17325.5m, MilesDriven = 4200m });
            var monthsAgain = new JsonStore<MonthlyEntry>(temp, "monthly");
            Check("Monthly: hours and miles survive save/load", monthsAgain.Items[0].HoursWorked == 17325.5m && monthsAgain.Items[0].MilesDriven == 4200m);

            // Behavioral Observations
            var card = new BbsObservation { SafeCount = 9, AtRiskCount = 1 };
            Check("BBS: 9 safe / 1 at-risk is 90% safe", card.PercentSafeText == "90%", card.PercentSafeText);
            Check("BBS: at-risk card can become an action", card.CanCreateAction && card.DisplayStatus == Status.AtRiskFound);
            card.LinkedAction = "ACT-0001";
            Check("BBS: linked card can't create a second action", !card.CanCreateAction && card.HasLinkedAction);
            Check("BBS: all-safe card", new BbsObservation { SafeCount = 5 }.DisplayStatus == Status.AllSafe);

            // Work Permits
            var permit = new WorkPermit { ValidFrom = DateTime.Today.AddDays(-2), ValidUntil = DateTime.Today.AddDays(-1) };
            Check("Permits: active past its end date shows Expired", permit.DisplayStatus == Status.Expired, permit.DisplayStatus);
            permit.Status = Status.Closed;
            Check("Permits: closed permit isn't Expired", permit.DisplayStatus == Status.Closed, permit.DisplayStatus);
            Check("Permits: valid today", new WorkPermit().DisplayStatus == Status.Active);

            // Lockout / Tagout
            Check("LOTO: never inspected", new LotoProcedure().DisplayStatus == Status.NotInspected);
            Check("LOTO: inspected 400 days ago is Overdue", new LotoProcedure { LastInspection = DateTime.Today.AddDays(-400) }.DisplayStatus == Status.Overdue);
            Check("LOTO: inspected 350 days ago is Due soon", new LotoProcedure { LastInspection = DateTime.Today.AddDays(-350) }.DisplayStatus == Status.DueSoon);
            Check("LOTO: inspected today is Current", new LotoProcedure { LastInspection = DateTime.Today }.DisplayStatus == Status.Current);

            // Safety Data Sheets (file storage shared with Library and LOTO)
            var sdsId = Guid.NewGuid();
            var sdsFile = Editors.StoreFile("sds", sdsId, srcA);
            var sds = new SdsEntry { Product = "Acetone", StoredFile = sdsFile };
            Check("SDS: file stored under data\\sds", sdsFile.StartsWith("sds") && File.Exists(Path.Combine(DataStore.Folder, sdsFile)), sdsFile);
            Check("SDS: with a file is On file", sds.DisplayStatus == Status.OnFile);
            Check("SDS: without a file is Missing SDS", new SdsEntry().DisplayStatus == Status.MissingSds);
            try { Directory.Delete(Path.Combine(DataStore.Folder, "sds", sdsId.ToString("N")), recursive: true); } catch { /* ignore */ }

            // Incidents and OSHA rates
            var cut = new Incident { Type = Incident.InjuryIllness, Classification = Incident.MedicalTreatment };
            var sprain = new Incident { Type = Incident.InjuryIllness, Classification = Incident.DaysAwayCase, DaysAway = 5 };
            var firstAid = new Incident { Type = Incident.InjuryIllness, Classification = Incident.FirstAid };
            var spill = new Incident { Type = "Environmental", Classification = Incident.DaysAwayCase };
            Check("Incidents: medical treatment is recordable", cut.IsRecordable && !cut.IsDart);
            Check("Incidents: days away is recordable, DART and lost time", sprain.IsRecordable && sprain.IsDart && sprain.IsLostTime);
            Check("Incidents: first aid only is not recordable", !firstAid.IsRecordable);
            Check("Incidents: only injury / illness cases are recordable", !spill.IsRecordable);
            var metrics = new SafetyMetrics { Incidents = [cut, sprain, firstAid, spill], Hours = 200_000m, Miles = 500_000m };
            Check("Rates: TRIR = 2 cases × 200,000 ÷ 200,000 hours", SafetyMetrics.Format(metrics.Trir) == "2.00", SafetyMetrics.Format(metrics.Trir));
            Check("Rates: DART and LTIR", SafetyMetrics.Format(metrics.Dart) == "1.00" && SafetyMetrics.Format(metrics.Ltir) == "1.00");
            Check("Rates: lost days rate", SafetyMetrics.Format(metrics.LostDaysRate) == "5.00", SafetyMetrics.Format(metrics.LostDaysRate));
            Check("Rates: no hours shows a dash", SafetyMetrics.Format(new SafetyMetrics { Incidents = [cut] }.Trir) == "—");
            var vehicle = new SafetyMetrics { Incidents = [new Incident { Type = Incident.MotorVehicle }], Miles = 500_000m, ExtraVehicleAccidents = 1 };
            Check("Rates: accident frequency per million miles", SafetyMetrics.Format(vehicle.AccidentFrequencyRate) == "4.00", SafetyMetrics.Format(vehicle.AccidentFrequencyRate));

            // Risk Assessment
            Check("Risk: 2 × 2 is Low", RiskAssessment.LevelFor(4) == Status.Low);
            Check("Risk: 3 × 3 is Medium", RiskAssessment.LevelFor(9) == Status.Medium);
            Check("Risk: 3 × 4 is High", RiskAssessment.LevelFor(12) == Status.High);
            Check("Risk: 4 × 4 is Extreme", RiskAssessment.LevelFor(16) == Status.Extreme);
            Check("Risk: rating labels parse", RiskAssessment.ParseRating("4 – Likely") == 4 && RiskAssessment.ParseRating("") == 3);

            // Audits & Inspections
            var audit = new Inspection
            {
                IsComplete = true,
                Items =
                [
                    new ChecklistItem { Result = ChecklistItem.Pass }, new ChecklistItem { Result = ChecklistItem.Pass },
                    new ChecklistItem { Result = ChecklistItem.Pass }, new ChecklistItem { Result = ChecklistItem.Fail },
                    new ChecklistItem { Result = ChecklistItem.NotApplicable },
                ],
            };
            Check("Audits: score ignores N/A (3 of 4 = 75%)", audit.ScoreText == "75%", audit.ScoreText);
            Check("Audits: failed item is a finding that can become an action", audit.DisplayStatus == Status.FindingsOpen && audit.CanCreateActions);
            Check("Audits: starter checklists", InspectionTemplate.Starters().Count == 3 && InspectionTemplate.Starters().All(t => t.Items.Count > 0));
            var general = new InspectionTemplate { Name = "Forklift pre-use check" };
            var dallas = new InspectionTemplate { Name = "Forklift (Dallas)", Sites = ["Dallas Plant"] };
            var fl3 = new InspectionTemplate { Name = "Forklift 3", Sites = ["Dallas Plant"], Equipment = ["Forklift 3"], BasedOn = "Forklift pre-use check" };
            var maint = new InspectionTemplate { Name = "Maintenance shop", Departments = ["Maintenance"] };
            Check("Checklists: general one applies anywhere", general.MatchScore("Austin", "", "") == 0 && general.IsGeneral);
            Check("Checklists: site checklist only at its site", dallas.MatchScore("dallas plant", "", "") > 0 && dallas.MatchScore("Austin", "", "") < 0);
            Check("Checklists: equipment version beats the site version", fl3.MatchScore("Dallas Plant", "", "Forklift 3") > dallas.MatchScore("Dallas Plant", "", "Forklift 3"));
            Check("Checklists: equipment version not offered for other equipment", fl3.MatchScore("Dallas Plant", "", "Forklift 4") < 0);
            Check("Checklists: department checklist needs that department", maint.MatchScore("Dallas Plant", "Maintenance", "") > 0 && maint.MatchScore("Dallas Plant", "Shipping", "") < 0);
            Check("Checklists: lists parse from commas", InspectionTemplate.ParseList("A, b;  a ,C").Count == 3 && fl3.ScopeText.Contains("Forklift 3"));

            // Management of Change
            var change = new ChangeRequest();
            Check("MOC: requested → under review", change.NextStage == Status.UnderReview);
            change.Stage = Status.Implemented;
            Check("MOC: implemented → closed", change.NextStage == Status.Closed);
            var temp1 = new ChangeRequest { Duration = "Temporary", TemporaryUntil = DateTime.Today.AddDays(-1) };
            Check("MOC: temporary change past its date is flagged", temp1.DisplayStatus == Status.TemporaryExpired);

            // Fleet Hub
            var driver = new Driver
            {
                ApplicationStatus = FleetStatus.Accepted, RoadTest = "Yes",
                LicenseExpires = DateTime.Today.AddYears(2), MedicalCardExpires = DateTime.Today.AddYears(1), LastMvrReview = DateTime.Today.AddMonths(-2),
            };
            Check("Fleet: complete driver file is Qualified", driver.DisplayStatus == FleetStatus.Qualified, driver.DisplayStatus);
            driver.MedicalCardExpires = DateTime.Today.AddDays(10);
            Check("Fleet: medical card in 10 days is Expiring soon", driver.DisplayStatus == Status.ExpiringSoon, driver.DisplayStatus);
            driver.LastMvrReview = DateTime.Today.AddMonths(-13);
            Check("Fleet: MVR review over a year old is Expired", driver.DisplayStatus == Status.Expired && driver.Problems.Count == 1, string.Join("; ", driver.Problems));
            Check("Fleet: applicant shows application status", new Driver().DisplayStatus == FleetStatus.Applied);
            Check("Fleet: missing dates make the file Incomplete", new Driver { ApplicationStatus = FleetStatus.Accepted }.DisplayStatus == FleetStatus.Incomplete);

            var truck = new Vehicle { LastAnnualInspection = DateTime.Today.AddMonths(-11).AddDays(-20), RegistrationExpires = DateTime.Today.AddYears(1) };
            Check("Fleet: annual inspection due in about 10 days is Due soon", truck.DisplayStatus == Status.DueSoon, truck.DisplayStatus);
            truck.LastAnnualInspection = DateTime.Today.AddYears(-2);
            Check("Fleet: annual inspection overdue", truck.DisplayStatus == FleetStatus.InspectionOverdue, truck.DisplayStatus);
            truck.RegistrationExpires = DateTime.Today.AddDays(-1);
            Check("Fleet: expired registration comes first", truck.DisplayStatus == FleetStatus.RegistrationExpired, truck.DisplayStatus);

            var dvir = new Dvir { DefectsFound = true };
            Check("Fleet: DVIR with defects is open until repaired", dvir.DefectsOpen && dvir.DisplayStatus == FleetStatus.DefectsOpen);
            dvir.RepairedOn = DateTime.Today;
            Check("Fleet: certified DVIR is Repaired", !dvir.DefectsOpen && dvir.DisplayStatus == FleetStatus.Repaired);

            Check("Fleet: 11.5 hours driving is an 11-hour violation", HosLog.DetectViolation(11.5m, 13m, HosLog.None) == "11-hour driving");
            Check("Fleet: 14.5-hour window is a 14-hour violation", HosLog.DetectViolation(10m, 14.5m, HosLog.None) == "14-hour window");
            Check("Fleet: 10 hours driving in a 12-hour window is compliant", HosLog.DetectViolation(10m, 12m, HosLog.None) == HosLog.None);

            Check("Fleet: tow-away makes an accident DOT recordable", new FleetAccident { TowAway = "Yes" }.IsDotRecordable);
            Check("Fleet: property damage only is not DOT recordable", !new FleetAccident().IsDotRecordable);
            Check("Fleet: quarterly service repeats every 3 months", new MaintenanceOrder { Repeat = "Every 3 months" }.RepeatMonths == 3);
            Check("Fleet: overdue work order", new MaintenanceOrder { DueDate = DateTime.Today.AddDays(-1) }.DisplayStatus == Status.Overdue);

            var register = new SafetyMetrics { Incidents = [new Incident { Type = Incident.MotorVehicle }], Miles = 1_000_000m, UseFleetRegister = true, FleetAccidents = 3 };
            Check("Fleet: accident register drives the accident frequency rate", SafetyMetrics.Format(register.AccidentFrequencyRate) == "3.00", SafetyMetrics.Format(register.AccidentFrequencyRate));

            var audit2 = FleetInsights.DotAudit();
            Check("Fleet: DOT mock audit runs every check", audit2.Count == 10, audit2.Count.ToString());
            Check("Fleet: overview alerts build", FleetInsights.Alerts() is not null);

            // Every report builds
            foreach (var report in ReportCatalog.All)
            {
                try
                {
                    var table = report.Build(DateTime.Today.Year);
                    Check($"Report builds: {report.Title}", table.Headers.Length > 0 && table.Rows.All(r => r.Length <= table.Headers.Length));
                }
                catch (Exception ex) { Check($"Report builds: {report.Title}", false, ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Check("Store round trip", false, ex.ToString());
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }

        await CrashAiChecks();
        LockChecks();
        await WebVideoChecks();
        await LockAndLocationUiChecks(window);
        await ReferencesChecks(window);
        await ServerSyncChecks();

        await Step("Back to Dashboard", () => window.GoTo(ModuleCatalog.Dashboard.Key));

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        return _failed;
    }
}
