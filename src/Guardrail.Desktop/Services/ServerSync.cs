using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Guardrail.Data;
using Guardrail.Sync;

namespace Guardrail.Services;

/// <summary>What's saved about the connection (sync.json in the data folder; the sign-in token is DPAPI-protected).</summary>
public sealed class ServerConnection
{
    public string Url { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string OrgName { get; set; } = "";
    /// <summary>The last server sequence number this PC has seen.</summary>
    public long LastSeq { get; set; }
    public DateTime? LastSync { get; set; }
    /// <summary>Changes made here that the server hasn't taken yet: "collection|id" → deleted-at (ms) or 0.</summary>
    public Dictionary<string, long> Outbox { get; set; } = [];
    /// <summary>Library files already uploaded: "library/&lt;id&gt;/&lt;name&gt;" → size.</summary>
    public Dictionary<string, long> UploadedFiles { get; set; } = [];
}

/// <summary>
/// Optional sync with Guardrail Server. Guardrail stays local-first: every change is saved on this PC straight away
/// and queued; the queue is sent and the server's changes fetched every minute, a few seconds after an edit, and on
/// "Sync now". Without a connection nothing here runs and Guardrail works exactly as before.
/// </summary>
public static class ServerSync
{
    private static readonly string StateFile = Path.Combine(DataStore.Folder, "sync.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object OutboxLock = new();
    private static ServerConnection? _conn;
    private static System.Threading.Timer? _timer, _debounce;
    private static bool _applyingRemote;
    private static bool _started;

    /// <summary>Raised (on the UI thread) when the status changes: connected, syncing, synced, offline, error.</summary>
    public static event Action? Changed;

    public static ServerConnection? Connection => _conn;
    public static bool IsConnected => _conn != null;
    public static bool IsSyncing { get; private set; }
    public static string? LastError { get; private set; }
    public static bool Offline { get; private set; }
    public static bool ReadOnly => _conn?.Role == Protocol.Roles.Viewer;
    public static int Pending { get { lock (OutboxLock) return _conn?.Outbox.Count ?? 0; } }

    /// <summary>Called once at startup (after the data is unlocked).</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        _conn = Load();
        SyncHooks.LocalChange += OnLocalChange;
        if (_conn != null) StartTimer();
    }

    private static void StartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = SyncNowAsync(quiet: true), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60));
    }

    private static void OnLocalChange(string collection, Guid id, bool deleted)
    {
        if (_conn == null || _applyingRemote) return;
        lock (OutboxLock) _conn.Outbox[$"{collection}|{id}"] = deleted ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : 0;
        Save();
        // Send a few seconds after the last edit
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => _ = SyncNowAsync(quiet: true), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        Notify();
    }

    // ----- Connecting -----

    public static SyncApiClient Client(string url, string? token = null) => new(url, token: token);

    /// <summary>Saves a new connection. <paramref name="keepLocal"/>: send this PC's records up; otherwise replace them with the server's.</summary>
    public static async Task ConnectAsync(string url, ServerInfo info, SessionResponse session, bool keepLocal)
    {
        _conn = new ServerConnection
        {
            Url = url, ServerName = info.Name, ProtectedToken = Protect(session.Token), DeviceId = session.DeviceId,
            Username = session.User.Username, DisplayName = session.User.DisplayName, Role = session.User.Role, OrgName = session.Org.Name,
        };
        Run(() =>
        {
            _applyingRemote = true;
            try
            {
                if (!keepLocal)
                    foreach (var store in SyncHooks.Stores) store.Clear();
            }
            finally { _applyingRemote = false; }
        });
        if (keepLocal && session.User.Role != Protocol.Roles.Viewer)
            lock (OutboxLock)
                foreach (var store in SyncHooks.Stores)
                    foreach (var id in store.Ids) _conn.Outbox[$"{store.Name}|{id}"] = 0;
        Save();
        LastError = null;
        Notify();
        StartTimer();
        await SyncNowAsync(quiet: false);
    }

    public static async Task DisconnectAsync()
    {
        var conn = _conn;
        if (conn == null) return;
        try
        {
            using var api = Client(conn.Url, Unprotect(conn.ProtectedToken));
            await api.LogoutAsync();
        }
        catch (SyncException) { /* offline: the admin can sign the device out from the web panel */ }
        _timer?.Dispose();
        _timer = null;
        _conn = null;
        LastError = null;
        Offline = false;
        try { File.Delete(StateFile); } catch (IOException) { }
        Notify();
    }

    // ----- Syncing -----

    /// <summary>Sends this PC's changes and fetches everyone else's. Returns a problem to show, or null.</summary>
    public static async Task<string?> SyncNowAsync(bool quiet)
    {
        if (_conn == null || !DataLock.IsOpen) return null;
        if (!await Gate.WaitAsync(quiet ? 0 : 60_000)) return null;
        var conn = _conn;
        IsSyncing = true;
        Notify();
        try
        {
            using var api = Client(conn.Url, Unprotect(conn.ProtectedToken));
            await PushAsync(api, conn);
            await PullAsync(api, conn);
            conn.LastSync = DateTime.Now;
            LastError = null;
            Offline = false;
            return null;
        }
        catch (SyncException e)
        {
            Offline = e.Offline;
            LastError = e.SignedOut ? "You've been signed out of GuardHawk Server. Connect again to keep syncing." : e.Message;
            return LastError;
        }
        catch (Exception e) when (e is IOException or JsonException or CryptographicException or InvalidOperationException)
        {
            LastError = "Syncing failed: " + e.Message;
            return LastError;
        }
        finally
        {
            Save();
            IsSyncing = false;
            Gate.Release();
            Notify();
        }
    }

    private static async Task PushAsync(SyncApiClient api, ServerConnection conn)
    {
        List<KeyValuePair<string, long>> queued;
        lock (OutboxLock) queued = conn.Outbox.ToList();
        if (queued.Count == 0) return;
        if (conn.Role == Protocol.Roles.Viewer)
        {
            lock (OutboxLock) conn.Outbox.Clear(); // read-only accounts: changes stay on this PC
            return;
        }

        foreach (var batch in queued.Chunk(Protocol.MaxPushBatch))
        {
            var changes = new List<PushChange>();
            var sent = new List<(string Key, long Stamp)>();
            foreach (var (key, deletedAt) in batch)
            {
                string[] parts = key.Split('|');
                if (parts.Length != 2 || !Guid.TryParse(parts[1], out var id) || SyncHooks.Find(parts[0]) is not { } store) continue;
                string? json = deletedAt == 0 ? Run(() => store.Serialize(id)) : null;
                if (json == null)
                    changes.Add(new PushChange(store.Name, id.ToString(), deletedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : deletedAt, true, null));
                else
                {
                    if (store.Name == "library") await UploadLibraryFileAsync(api, conn, json);
                    changes.Add(new PushChange(store.Name, id.ToString(), Run(() => store.ModifiedMs(id)), false, json));
                }
                sent.Add((key, deletedAt));
            }
            if (changes.Count == 0) continue;
            var result = await api.PushAsync(new PushRequest(changes));
            Run(() =>
            {
                _applyingRemote = true;
                try
                {
                    foreach (var r in result.Results.Where(r => r.Status == Protocol.Statuses.Ok && r.Number > 0))
                        if (SyncHooks.Find(r.Collection) is { } store && Guid.TryParse(r.Id, out var id)) store.SetNumber(id, r.Number);
                }
                finally { _applyingRemote = false; }
            });
            lock (OutboxLock)
                foreach (var (key, stamp) in sent)
                    if (conn.Outbox.TryGetValue(key, out long now) && now == stamp) conn.Outbox.Remove(key); // unless edited again meanwhile
            if (result.Results.Any(r => r.Status == Protocol.Statuses.Forbidden)) conn.Role = Protocol.Roles.Viewer;
        }
    }

    private static async Task PullAsync(SyncApiClient api, ServerConnection conn)
    {
        while (true)
        {
            var page = await api.ChangesAsync(conn.LastSeq);
            if (page.Changes.Count > 0)
            {
                HashSet<string> pending;
                lock (OutboxLock) pending = conn.Outbox.Keys.ToHashSet();
                var byCollection = page.Changes
                    .Where(c => Guid.TryParse(c.Id, out _) && !pending.Contains($"{c.Collection}|{Guid.Parse(c.Id)}")) // local edits not sent yet win until they're pushed
                    .GroupBy(c => c.Collection);
                var libraryRecords = new List<string>();
                Run(() =>
                {
                    _applyingRemote = true;
                    try
                    {
                        foreach (var group in byCollection)
                        {
                            if (SyncHooks.Find(group.Key) is not { } store) continue; // a type this version doesn't have
                            store.ApplyRemote(group.Select(c => (Guid.Parse(c.Id), c.Deleted ? null : c.Payload)).ToList());
                            if (group.Key == "library") libraryRecords.AddRange(group.Where(c => !c.Deleted && c.Payload != null).Select(c => c.Payload!));
                        }
                    }
                    finally { _applyingRemote = false; }
                });
                foreach (var json in libraryRecords) await DownloadLibraryFileAsync(api, json);
                conn.LastSeq = page.Changes[^1].Seq;
            }
            else conn.LastSeq = Math.Max(conn.LastSeq, page.Latest);
            if (!page.More) break;
        }
    }

    // ----- Library files -----

    private static string? FileKey(string json, out string localPath)
    {
        localPath = "";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("StoredFile", out var sf) || sf.GetString() is not { Length: > 0 } stored) return null;
        string id = root.GetProperty("Id").GetGuid().ToString("N");
        localPath = Path.Combine(DataStore.Folder, stored);
        string key = $"library/{id}/{Path.GetFileName(stored)}";
        return Protocol.IsValidFileName(key) ? key : null;
    }

    private static async Task UploadLibraryFileAsync(SyncApiClient api, ServerConnection conn, string json)
    {
        if (FileKey(json, out string path) is not { } key || !File.Exists(path)) return;
        long size = new FileInfo(path).Length;
        if (conn.UploadedFiles.TryGetValue(key, out long done) && done == size) return;
        byte[] bytes = DataLock.ReadBytes(path); // decrypted: other PCs have their own key
        await api.UploadFileAsync(key, new MemoryStream(bytes));
        conn.UploadedFiles[key] = size;
    }

    private static async Task DownloadLibraryFileAsync(SyncApiClient api, string json)
    {
        if (FileKey(json, out string path) is not { } key || File.Exists(path)) return;
        var buffer = new MemoryStream();
        if (!await api.DownloadFileAsync(key, buffer)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DataLock.WriteBytes(path, buffer.ToArray()); // encrypted when this PC's lock is on
    }

    // ----- Helpers -----

    private static T Run<T>(Func<T> f) => Application.Current?.Dispatcher is { } d && !d.CheckAccess() ? d.Invoke(f) : f();
    private static void Run(Action a) => Run(() => { a(); return 0; });

    private static void Notify()
    {
        if (Application.Current?.Dispatcher is { } d) d.BeginInvoke(() => Changed?.Invoke());
    }

    private static string Protect(string token) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes("Guardrail.Server"), DataProtectionScope.CurrentUser));

    private static string? Unprotect(string value)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Encoding.UTF8.GetBytes("Guardrail.Server"), DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }

    private static ServerConnection? Load()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<ServerConnection>(DataLock.ReadText(StateFile), Json) : null; }
        catch (Exception e) when (e is IOException or JsonException or CryptographicException or LockedException) { return null; }
    }

    private static void Save()
    {
        var conn = _conn;
        if (conn == null) return;
        try
        {
            string text;
            lock (OutboxLock) text = JsonSerializer.Serialize(conn, Json);
            DataLock.WriteText(StateFile, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or LockedException) { }
    }
}
