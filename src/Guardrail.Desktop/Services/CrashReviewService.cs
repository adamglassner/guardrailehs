using System.IO;
using System.Windows;
using Guardrail.CrashAi;
using Guardrail.Data;

namespace Guardrail.Services;

/// <summary>
/// Runs Crash Review AI in the background, one video at a time, with no one needing to click anything:
/// videos added by hand, videos dropped into the watch folder, and videos attached to Accident Register entries.
/// </summary>
public static class CrashReviewService
{
    private static readonly SemaphoreSlim Signal = new(0);
    private static CancellationTokenSource _cancel = new();
    private static FileSystemWatcher? _watcher;
    private static bool _started;

    /// <summary>The review being analyzed right now, if any.</summary>
    public static VideoReview? Current { get; private set; }

    /// <summary>Raised on the UI thread when reviews are added, progress moves or a result comes in.</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;

        // Anything interrupted when Guardrail last closed goes back in the queue.
        foreach (var r in DataStore.CrashReviews.Items.Where(r => r.State == ReviewState.Analyzing).ToList())
        {
            r.State = ReviewState.Waiting;
            DataStore.CrashReviews.Update(r);
        }

        DataStore.FleetAccidents.Changed += (_, _) => QueueAccidentVideos();
        QueueAccidentVideos();
        ApplyWatchFolder();

        new Thread(Worker) { IsBackground = true, Name = "Crash Review AI", Priority = ThreadPriority.BelowNormal }.Start();
        Signal.Release();
    }

    // ----- Adding work -----

    /// <summary>Queues a video. Call on the UI thread.</summary>
    public static VideoReview? Add(string path, string source = "Added by hand", Guid? accidentId = null)
    {
        if (!CrashAnalyzer.IsVideo(path) || !File.Exists(path)) return null;
        var info = new FileInfo(path);
        var review = new VideoReview
        {
            VideoPath = info.FullName, VideoSize = info.Length, VideoModified = info.LastWriteTimeUtc,
            Source = source, AccidentId = accidentId, CameraName = GuessCameraName(info.Name, accidentId),
        };
        DataStore.CrashReviews.Add(review);
        Signal.Release();
        Changed?.Invoke();
        return review;
    }

    /// <summary>
    /// Names the camera vehicle when it can be told: the unit on the linked accident, or a fleet unit number
    /// that appears in the file name (dashcams often name files after the truck).
    /// </summary>
    public static string GuessCameraName(string fileName, Guid? accidentId)
    {
        if (accidentId is { } id && DataStore.FleetAccidents.Items.FirstOrDefault(a => a.Id == id) is { UnitNumber.Length: > 0 } acc)
            return VideoReview.NameForUnit(acc.UnitNumber);
        string name = Path.GetFileNameWithoutExtension(fileName);
        var match = DataStore.Vehicles.Items
            .Where(v => v.UnitNumber.Trim().Length >= 2)
            .OrderByDescending(v => v.UnitNumber.Length)
            .FirstOrDefault(v => System.Text.RegularExpressions.Regex.IsMatch(name,
                $@"(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(v.UnitNumber.Trim())}(?![A-Za-z0-9])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        return match is null ? "" : VideoReview.NameForUnit(match.UnitNumber.Trim());
    }

    /// <summary>Renames the camera vehicle on a review.</summary>
    public static void SetCameraName(VideoReview review, string name)
    {
        review.CameraName = name.Trim();
        DataStore.CrashReviews.Update(review);
        Changed?.Invoke();
    }

    /// <summary>Queues a web link: the video is downloaded, kept with the review, then analyzed. Call on the UI thread.</summary>
    public static VideoReview? AddUrl(string url, string source = "Web link")
    {
        if (!WebVideo.LooksLikeUrl(url)) return null;
        var review = new VideoReview { SourceUrl = url.Trim(), Source = $"{source} · {new Uri(url.Trim()).Host.Replace("www.", "")}" };
        DataStore.CrashReviews.Add(review);
        Signal.Release();
        Changed?.Invoke();
        return review;
    }

    /// <summary>Asked (once) before yt-dlp is downloaded. Runs on the UI thread.</summary>
    private static bool MayGetYtDlp() => OnUi(() =>
    {
        if (App.Settings.AllowYtDlp) return true;
        bool ok = Views.MessageDialog.Confirm("Download yt-dlp?",
            "This link is on a site like YouTube, Facebook or Vimeo. Guardrail uses yt-dlp, a free open-source downloader, to get the video. " +
            "It's downloaded once (about 18 MB) from yt-dlp's official GitHub releases and checked before use.\n\n" +
            "Only analyze videos you have the right to use.", "Download yt-dlp", "Not now");
        if (ok) { App.Settings.AllowYtDlp = true; App.Settings.Save(); }
        return ok;
    });

    /// <summary>Tells the views a review changed (documents attached, the camera vehicle named…).</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    public static void Retry(VideoReview review)
    {
        review.State = ReviewState.Waiting;
        review.Error = "";
        review.Report = null;
        DataStore.CrashReviews.Update(review);
        Signal.Release();
        Changed?.Invoke();
    }

    public static void Delete(VideoReview review)
    {
        if (Current == review) _cancel.Cancel();
        DataStore.CrashReviews.Delete(review);
        try { if (Directory.Exists(review.Folder)) Directory.Delete(review.Folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    /// <summary>Opens File Explorer with the video selected (or its folder, if the file is gone).</summary>
    public static void ShowInFolder(VideoReview review)
    {
        try
        {
            if (File.Exists(review.VideoPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{review.VideoPath}\"");
            else if (Path.GetDirectoryName(review.VideoPath) is { Length: > 0 } dir && Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            else
                Toaster.Show(review.IsWeb ? "This video hasn't been downloaded." : "The video file has been moved or deleted.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Toaster.Show($"Couldn't open File Explorer: {ex.Message}"); }
    }

    /// <summary>Asks, then removes a review from the list. Downloaded copies of web videos go with it; files on this PC are never touched.</summary>
    public static bool ConfirmAndDelete(VideoReview review)
    {
        string what = review.IsWeb
            ? "The report and the downloaded copy of the video are deleted. The video online isn't affected."
            : "The report is deleted. Your video file stays where it is.";
        if (!Views.MessageDialog.Confirm($"Remove {review.Number}?", $"{review.VideoName}\n\n{what}", "Remove", "Keep it", danger: true)) return false;
        Delete(review);
        Toaster.Show($"{review.Number} removed.");
        return true;
    }

    public static int WaitingCount => DataStore.CrashReviews.Items.Count(r => r.State == ReviewState.Waiting);

    // ----- Watch folder -----

    /// <summary>Starts (or stops) watching the folder in Settings. Existing videos not reviewed yet are queued too.</summary>
    public static void ApplyWatchFolder()
    {
        _watcher?.Dispose();
        _watcher = null;
        string folder = App.Settings.CrashWatchFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            _watcher.Created += (_, e) => WhenReady(e.FullPath);
            _watcher.Renamed += (_, e) => WhenReady(e.FullPath);
            _watcher.EnableRaisingEvents = true;
            foreach (var f in Directory.EnumerateFiles(folder)) WhenReady(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Toaster.Show($"Can't watch {folder}: {ex.Message}");
        }
    }

    /// <summary>Waits until a new file stops growing (it may still be copying), then queues it once.</summary>
    private static void WhenReady(string path)
    {
        if (!CrashAnalyzer.IsVideo(path)) return;
        Task.Run(async () =>
        {
            long last = -1;
            for (int i = 0; i < 900; i++)
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) return;
                    if (fi.Length > 0 && fi.Length == last && CanOpen(path)) break;
                    last = fi.Length;
                }
                catch (IOException) { }
                await Task.Delay(2000);
            }
            OnUi(() => { if (!AlreadyQueued(path)) Add(path, "Watch folder"); });
        });
    }

    private static bool CanOpen(string path)
    {
        try { using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool AlreadyQueued(string path)
    {
        var fi = new FileInfo(path);
        return DataStore.CrashReviews.Items.Any(r =>
            string.Equals(r.VideoPath, fi.FullName, StringComparison.OrdinalIgnoreCase) && r.VideoSize == fi.Length);
    }

    // ----- Accident Register -----

    private static void QueueAccidentVideos()
    {
        if (!App.Settings.CrashAnalyzeAccidents) return;
        foreach (var a in DataStore.FleetAccidents.Items.Where(a => a.HasFile).ToList())
        {
            string path = Path.Combine(DataStore.Folder, a.StoredFile);
            if (!CrashAnalyzer.IsVideo(path) || !File.Exists(path)) continue;
            if (DataStore.CrashReviews.Items.Any(r => r.AccidentId == a.Id && string.Equals(r.VideoPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                continue;
            Add(path, $"Accident {a.Number}", a.Id);
        }
    }

    /// <summary>Writes the result onto the linked accident, and sets preventability when the AI is confident.</summary>
    private static void ApplyToAccident(VideoReview review)
    {
        if (review.AccidentId is not { } id || review.Report is not { } rep) return;
        var accident = DataStore.FleetAccidents.Items.FirstOrDefault(a => a.Id == id);
        if (accident is null || accident.Notes.Contains(review.Number)) return;

        string line = $"Crash Review AI ({review.Number}): {review.Describe(rep.Headline)}";
        if (rep.Verdict != Verdicts.NoCollision) line += $" ({rep.Confidence}% confidence)";
        line += ".";
        if (App.Settings.CrashSetPreventability && accident.Preventability == FleetStatus.PendingReview && rep.Confidence >= 70)
        {
            if (rep.Verdict == Verdicts.Camera) { accident.Preventability = FleetStatus.Preventable; line += " Preventability set to Preventable automatically."; }
            else if (rep.Verdict == Verdicts.Other) { accident.Preventability = FleetStatus.NonPreventable; line += " Preventability set to Non-preventable automatically."; }
        }
        accident.Notes = accident.Notes.Length > 0 ? accident.Notes + Environment.NewLine + line : line;
        DataStore.FleetAccidents.Update(accident);
    }

    /// <summary>The newest finished review for an accident, if any.</summary>
    public static VideoReview? ForAccident(Guid accidentId) =>
        DataStore.CrashReviews.Items.Where(r => r.AccidentId == accidentId).OrderByDescending(r => r.Seq).FirstOrDefault();

    // ----- Worker -----

    private static void Worker()
    {
        try { WorkLoop(); }
        catch (TaskCanceledException) { /* app is closing */ }
        catch (OperationCanceledException) { }
    }

    private static void WorkLoop()
    {
        while (true)
        {
            Signal.Wait();
            while (true)
            {
                var next = OnUi(() => DataStore.CrashReviews.Items.Where(r => r.State == ReviewState.Waiting).OrderBy(r => r.Seq).FirstOrDefault());
                if (next is null) break;
                _cancel = new CancellationTokenSource();
                OnUi(() =>
                {
                    next.State = ReviewState.Analyzing;
                    next.Progress = 0;
                    next.Stage = "Starting";
                    Current = next;
                    DataStore.CrashReviews.Update(next);
                    Changed?.Invoke();
                });

                try
                {
                    var lastUi = DateTime.MinValue;
                    var progress = new InlineProgress<(string Stage, double Fraction)>(p =>
                    {
                        next.Stage = p.Stage;
                        next.Progress = p.Fraction;
                        if ((DateTime.UtcNow - lastUi).TotalMilliseconds < 300) return;
                        lastUi = DateTime.UtcNow;
                        OnUiAsync(() => Changed?.Invoke());
                    });
                    if (next.IsWeb && (next.VideoPath.Length == 0 || !File.Exists(next.VideoPath)))
                    {
                        string path = WebVideo.DownloadAsync(next.SourceUrl, next.Folder, progress, MayGetYtDlp, _cancel.Token).GetAwaiter().GetResult();
                        var info = new FileInfo(path);
                        OnUi(() =>
                        {
                            next.VideoPath = path;
                            next.VideoSize = info.Length;
                            next.VideoModified = info.LastWriteTimeUtc;
                            if (DataStore.CrashReviews.Items.Contains(next)) DataStore.CrashReviews.Update(next);
                        });
                    }
                    if (!File.Exists(next.VideoPath)) throw new FileNotFoundException("The video file has been moved or deleted.");
                    var report = CrashAnalyzer.Analyze(next.VideoPath, next.Folder, progress, _cancel.Token, next.CameraHint);
                    foreach (var key in report.KeyFrames) DataLock.ProtectFile(Path.Combine(next.Folder, key.FileName));
                    OnUi(() =>
                    {
                        Current = null;
                        if (!DataStore.CrashReviews.Items.Contains(next)) return;
                        next.Report = report;
                        next.State = ReviewState.Done;
                        next.AnalyzedAt = DateTime.Now;
                        DataStore.CrashReviews.Update(next);
                        ApplyToAccident(next);
                        Changed?.Invoke();
                        Toaster.Show(report.Verdict == Verdicts.NoCollision
                            ? $"{next.Number}: no collision found in {next.VideoName}."
                            : $"{next.Number}: {next.Describe(next.Case is { UsedDocuments: true } c ? c.Headline : report.Headline)} ({report.Confidence}% confidence).");
                    });
                }
                catch (OperationCanceledException)
                {
                    OnUi(() => { Current = null; Changed?.Invoke(); });
                }
                catch (Exception ex)
                {
                    OnUi(() =>
                    {
                        Current = null;
                        if (!DataStore.CrashReviews.Items.Contains(next)) return;
                        next.State = ReviewState.Failed;
                        next.Error = Friendly(ex);
                        DataStore.CrashReviews.Update(next);
                        Changed?.Invoke();
                    });
                }
            }
        }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        FileNotFoundException => "The video file has been moved or deleted.",
        System.Net.Http.HttpRequestException h => $"Couldn't reach that link: {h.Message}",
        InvalidDataException => ex.Message,
        OutOfMemoryException => "The computer ran out of memory on this video.",
        DllNotFoundException or TypeInitializationException => "The video engine couldn't start on this PC.",
        _ => ex.Message,
    };

    private static T OnUi<T>(Func<T> f)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) return f();
        return d.Invoke(f);
    }

    private static void OnUi(Action a) => OnUi(() => { a(); return 0; });

    private static void OnUiAsync(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
