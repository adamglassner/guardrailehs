using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Guardrail.CrashAi;

namespace Guardrail.Services;

/// <summary>
/// Gets a video from a web link for Crash Review AI:
/// 1) share links (Dropbox, Google Drive, OneDrive / SharePoint) are turned into direct downloads;
/// 2) a link that serves a video is downloaded directly;
/// 3) a web page with a video in it (og:video, &lt;video&gt;, &lt;source&gt;) has that video downloaded;
/// 4) anything else (YouTube, Facebook, X, Vimeo and ~1,800 other sites) goes through yt-dlp
///    (free, open source), which Guardrail downloads from yt-dlp's official GitHub releases the first time, with permission.
/// </summary>
public static partial class WebVideo
{
    private static readonly HttpClient Http = CreateClient();
    public static readonly string ToolsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardrail", "tools");
    public static string YtDlpPath => Path.Combine(ToolsFolder, "yt-dlp.exe");
    private const string YtDlpRelease = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    private const long MaxBytes = 4L * 1024 * 1024 * 1024; // 4 GB

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) GuardrailEHS/1.0");
        return c;
    }

    /// <summary>True for an http(s) link.</summary>
    public static bool LooksLikeUrl(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>Turns share links into direct-download links where the service allows it.</summary>
    public static string Normalize(string url)
    {
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        string host = u.Host.ToLowerInvariant();

        if (host.EndsWith("dropbox.com"))
        {
            string q = Regex.Replace(u.Query, @"([?&])dl=0", "$1dl=1");
            if (!Regex.IsMatch(q, @"[?&]dl=1")) q = (q.Length > 0 ? q + "&" : "?") + "dl=1";
            return new UriBuilder(u) { Query = q.TrimStart('?') }.Uri.ToString();
        }
        if (host == "drive.google.com")
        {
            var m = Regex.Match(u.AbsolutePath, @"/file/d/([A-Za-z0-9_-]+)");
            string? id = m.Success ? m.Groups[1].Value : Regex.Match(u.Query, @"[?&]id=([A-Za-z0-9_-]+)") is { Success: true } q ? q.Groups[1].Value : null;
            if (id != null) return $"https://drive.google.com/uc?export=download&id={id}";
        }
        if (host == "1drv.ms" || host.EndsWith("onedrive.live.com") || host.EndsWith("sharepoint.com"))
        {
            if (!Regex.IsMatch(u.Query, @"[?&]download=1")) return url + (u.Query.Length > 0 ? "&" : "?") + "download=1";
        }
        return url;
    }

    /// <summary>Short display name for a link: "youtube.com · watch?v=abc".</summary>
    public static string DisplayName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        string host = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
        string tail = u.Segments.Length > 0 ? Uri.UnescapeDataString(u.Segments[^1].Trim('/')) : "";
        if (tail.Length == 0 || tail == "watch") tail = u.Query.TrimStart('?');
        if (tail.Length > 48) tail = tail[..48] + "…";
        return tail.Length > 0 ? $"{host} · {tail}" : host;
    }

    /// <summary>Finds a video address inside a web page (Open Graph tags, &lt;video&gt; or &lt;source&gt;), or null.</summary>
    public static string? FindVideoInHtml(string html, Uri page)
    {
        var patterns = new[]
        {
            @"<meta[^>]+property\s*=\s*[""']og:video(?::secure_url|:url)?[""'][^>]*content\s*=\s*[""']([^""']+)[""']",
            @"<meta[^>]+content\s*=\s*[""']([^""']+)[""'][^>]*property\s*=\s*[""']og:video(?::secure_url|:url)?[""']",
            @"<video[^>]+src\s*=\s*[""']([^""']+)[""']",
            @"<source[^>]+src\s*=\s*[""']([^""']+\.(?:mp4|mov|webm|m4v)(?:\?[^""']*)?)[""']",
            @"<source[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*type\s*=\s*[""']video/",
        };
        foreach (var p in patterns)
        {
            var m = Regex.Match(html, p, RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            string raw = WebUtility.HtmlDecode(m.Groups[1].Value);
            if (raw.StartsWith("blob:") || raw.StartsWith("data:")) continue;
            if (Uri.TryCreate(page, raw, out var abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps)) return abs.ToString();
        }
        return null;
    }

    /// <summary>
    /// Downloads the video at <paramref name="url"/> into <paramref name="folder"/> and returns the file's path.
    /// <paramref name="mayGetYtDlp"/> is asked (once) before yt-dlp is downloaded.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string folder, IProgress<(string Stage, double Fraction)>? progress,
        Func<bool> mayGetYtDlp, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        string link = Normalize(url);
        progress?.Report(("Connecting", 0));

        // 1–3: direct video, or a page with a video in it.
        try
        {
            var direct = await TryDirectAsync(link, folder, progress, cancel, depth: 0);
            if (direct != null) return direct;
        }
        catch (HttpRequestException) { /* let yt-dlp have a go */ }

        // 4: streaming sites.
        if (!File.Exists(YtDlpPath))
        {
            if (!mayGetYtDlp()) throw new InvalidDataException("This link needs yt-dlp to download, and permission to get it wasn't given.");
            progress?.Report(("Getting yt-dlp (one time)", 0));
            await GetYtDlpAsync(cancel);
        }
        else if (DateTime.UtcNow - File.GetLastWriteTimeUtc(YtDlpPath) > TimeSpan.FromDays(21))
        {
            progress?.Report(("Updating yt-dlp", 0));
            await RunAsync(YtDlpPath, ["-U"], null, cancel, TimeSpan.FromMinutes(2)); // sites change often; best effort
        }
        return await YtDlpAsync(link, folder, progress, cancel);
    }

    private static async Task<string?> TryDirectAsync(string link, string folder, IProgress<(string, double)>? progress, CancellationToken cancel, int depth)
    {
        using var response = await Http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode) return null;
        string type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        string? fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        fileName = fileName?.Trim('"');
        string ext = Path.GetExtension(fileName ?? response.RequestMessage?.RequestUri?.AbsolutePath ?? "").ToLowerInvariant();

        bool isVideo = type.StartsWith("video/") || (type is "application/octet-stream" or "binary/octet-stream" or "application/mp4" && CrashAnalyzer.IsVideo("x" + ext));
        if (isVideo)
        {
            if (!CrashAnalyzer.IsVideo("x" + ext)) ext = type switch { "video/webm" => ".webm", "video/quicktime" => ".mov", "video/x-matroska" => ".mkv", _ => ".mp4" };
            string target = Path.Combine(folder, "video" + ext);
            long? total = response.Content.Headers.ContentLength;
            if (total > MaxBytes) throw new InvalidDataException("That video is larger than 4 GB.");
            await using (var src = await response.Content.ReadAsStreamAsync(cancel))
            await using (var dst = File.Create(target + ".part"))
            {
                var buffer = new byte[1 << 16];
                long done = 0; int n;
                while ((n = await src.ReadAsync(buffer, cancel)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                    done += n;
                    if (done > MaxBytes) throw new InvalidDataException("That video is larger than 4 GB.");
                    if (total > 0) progress?.Report(("Downloading the video", (double)done / total.Value));
                    else progress?.Report(($"Downloading the video ({done / 1048576.0:0} MB)", 0));
                }
            }
            File.Move(target + ".part", target, overwrite: true);
            return target;
        }

        if (type.Contains("html") && depth == 0)
        {
            string html = await response.Content.ReadAsStringAsync(cancel);
            if (html.Length > 4_000_000) html = html[..4_000_000];
            var page = response.RequestMessage?.RequestUri ?? new Uri(link);
            if (FindVideoInHtml(html, page) is { } inner) return await TryDirectAsync(inner, folder, progress, cancel, depth + 1);
        }
        return null;
    }

    /// <summary>Downloads yt-dlp.exe from its official GitHub release and checks it against the published SHA-256.</summary>
    private static async Task GetYtDlpAsync(CancellationToken cancel)
    {
        Directory.CreateDirectory(ToolsFolder);
        string sums = await Http.GetStringAsync(YtDlpRelease + "SHA2-256SUMS", cancel);
        var expected = Regex.Match(sums, @"^([0-9a-fA-F]{64})\s+\*?yt-dlp\.exe\s*$", RegexOptions.Multiline);
        if (!expected.Success) throw new InvalidDataException("Couldn't verify yt-dlp (no checksum published).");
        var bytes = await Http.GetByteArrayAsync(YtDlpRelease + "yt-dlp.exe", cancel);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("yt-dlp failed its checksum, so it wasn't used.");
        await File.WriteAllBytesAsync(YtDlpPath + ".tmp", bytes, cancel);
        File.Move(YtDlpPath + ".tmp", YtDlpPath, overwrite: true);
    }

    [GeneratedRegex(@"\[download\]\s+([\d.]+)%")]
    private static partial Regex PercentRegex();

    private static async Task<string> YtDlpAsync(string link, string folder, IProgress<(string, double)>? progress, CancellationToken cancel)
    {
        foreach (var old in Directory.GetFiles(folder, "video.*")) File.Delete(old);
        // Video only (no audio needed), at most 1080p, as one file so no separate merge tool is required.
        var args = new List<string>
        {
            "--no-playlist", "--no-part", "--no-mtime", "--newline", "--no-warnings",
            "-f", "bv*[height<=1080][ext=mp4]/b[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]/b",
            "-o", Path.Combine(folder, "video.%(ext)s"), link,
        };
        var (code, error) = await RunAsync(YtDlpPath, args, line =>
        {
            var m = PercentRegex().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                progress?.Report(("Downloading the video", pct / 100));
        }, cancel, TimeSpan.FromHours(2));
        var file = Directory.GetFiles(folder, "video.*").FirstOrDefault(f => CrashAnalyzer.IsVideo(f));
        if (code != 0 || file is null)
        {
            string reason = error.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR")) ?? error.Trim();
            reason = Regex.Replace(reason, @"^ERROR:\s*(\[[^\]]+\]\s*)?", "");
            throw new InvalidDataException(reason.Length > 0 ? $"Couldn't download the video: {reason}" : "Couldn't download a video from that link.");
        }
        return file;
    }

    private static async Task<(int Code, string Error)> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken cancel, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var error = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timer.CancelAfter(timeout);
        try { await p.WaitForExitAsync(timer.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancel.ThrowIfCancellationRequested();
            return (-1, "Timed out.");
        }
        return (p.ExitCode, error.ToString());
    }
}
