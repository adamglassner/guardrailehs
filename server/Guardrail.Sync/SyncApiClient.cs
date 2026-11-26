using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guardrail.Sync;

/// <summary>A problem reported by the server (or the network), with a message that can be shown as is.</summary>
public sealed class SyncException(int status, string error, string message) : Exception(message)
{
    /// <summary>HTTP status, or 0 when the server couldn't be reached.</summary>
    public int Status { get; } = status;
    /// <summary>Machine-readable code, e.g. "unauthorized", "paused", "offline".</summary>
    public string Error { get; } = error;
    public bool SignedOut => Status == 401;
    public bool Offline => Status == 0 || Error is "paused" || Status is 502 or 503 or 504;
}

/// <summary>Talks to Guardrail Server's /api/v1 (used by the desktop app and the tests).</summary>
public sealed class SyncApiClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    public string BaseUrl { get; }
    public string? Token { get; set; }

    public SyncApiClient(string baseUrl, HttpMessageHandler? handler = null, string? token = null)
    {
        BaseUrl = NormalizeUrl(baseUrl);
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromMinutes(10);
        Token = token;
    }

    /// <summary>"guardrail.local" → "http://guardrail.local:5080/"; keeps https:// and explicit ports.</summary>
    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) throw new SyncException(0, "bad_address", "Enter the server's address, for example http://192.168.1.20:5080");
        if (!url.Contains("://")) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new SyncException(0, "bad_address", "That doesn't look like a server address. Try something like http://192.168.1.20:5080");
        var b = new UriBuilder(u) { Path = u.AbsolutePath.TrimEnd('/') + "/", Query = "", Fragment = "" };
        if (u.IsDefaultPort && u.Scheme == "http" && !url.Contains($"{u.Host}:")) b.Port = Protocol.DefaultPort;
        return b.Uri.ToString();
    }

    public Task<ServerInfo> InfoAsync(CancellationToken ct = default) => Send<ServerInfo>(HttpMethod.Get, "api/v1/info", null, ct);
    public Task<SessionResponse> LoginAsync(LoginRequest r, CancellationToken ct = default) => Send<SessionResponse>(HttpMethod.Post, "api/v1/auth/login", r, ct);
    public Task<SessionResponse> RegisterAsync(RegisterRequest r, CancellationToken ct = default) => Send<SessionResponse>(HttpMethod.Post, "api/v1/auth/register", r, ct);
    public Task LogoutAsync(CancellationToken ct = default) => Send<JsonElement>(HttpMethod.Post, "api/v1/auth/logout", new { }, ct);
    public Task ChangePasswordAsync(ChangePasswordRequest r, CancellationToken ct = default) => Send<JsonElement>(HttpMethod.Post, "api/v1/auth/password", r, ct);
    public Task<AccountResponse> AccountAsync(CancellationToken ct = default) => Send<AccountResponse>(HttpMethod.Get, "api/v1/account", null, ct);
    public Task<List<DirectoryUser>> DirectoryAsync(CancellationToken ct = default) => Send<List<DirectoryUser>>(HttpMethod.Get, "api/v1/directory", null, ct);
    public Task<ChangesResponse> ChangesAsync(long since, CancellationToken ct = default) => Send<ChangesResponse>(HttpMethod.Get, $"api/v1/sync/changes?since={since}", null, ct);
    public Task<PushResponse> PushAsync(PushRequest r, CancellationToken ct = default) => Send<PushResponse>(HttpMethod.Post, "api/v1/sync/push", r, ct);

    public async Task UploadFileAsync(string name, Stream content, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Put, FilePath(name));
        req.Content = new StreamContent(content);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var res = await SendRaw(req, ct);
        await Check(res, ct);
    }

    /// <summary>Downloads a file to <paramref name="destination"/>. False when the server doesn't have it.</summary>
    public async Task<bool> DownloadFileAsync(string name, Stream destination, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, FilePath(name));
        using var res = await SendRaw(req, ct, HttpCompletionOption.ResponseHeadersRead);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;
        await Check(res, ct);
        await res.Content.CopyToAsync(destination, ct);
        return true;
    }

    public async Task DeleteFileAsync(string name, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Delete, FilePath(name));
        using var res = await SendRaw(req, ct);
        if (res.StatusCode != HttpStatusCode.NotFound) await Check(res, ct);
    }

    private static string FilePath(string name) => "api/v1/files/" + string.Join('/', name.Split('/').Select(Uri.EscapeDataString));

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        if (Token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return req;
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = Request(method, path);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        using var res = await SendRaw(req, ct);
        await Check(res, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<HttpResponseMessage> SendRaw(HttpRequestMessage req, CancellationToken ct, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        try { return await _http.SendAsync(req, option, ct); }
        catch (HttpRequestException e) { throw new SyncException(0, "offline", $"Can't reach the server at {BaseUrl} ({e.Message})."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new SyncException(0, "offline", $"The server at {BaseUrl} didn't answer in time."); }
    }

    private static async Task Check(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        ErrorResponse? err = null;
        try { err = await res.Content.ReadFromJsonAsync<ErrorResponse>(Json, ct); } catch { }
        throw new SyncException((int)res.StatusCode, err?.Error ?? "http_" + (int)res.StatusCode,
            err?.Message ?? $"The server answered {(int)res.StatusCode} {res.ReasonPhrase}. Is that a GuardHawk Server?");
    }

    public void Dispose() => _http.Dispose();
}
