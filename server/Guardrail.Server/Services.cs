using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Guardrail.Server;

/// <summary>Password hashing and random tokens.</summary>
public static class Secrets
{
    /// <summary>PBKDF2-SHA256 work factor for new passwords (OWASP 2023 recommendation).</summary>
    public const int Iterations = 600_000;

    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static string HashPassword(string password, string salt, int iterations) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, 32));

    public static bool CheckPassword(string password, string hash, string salt, int iterations) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashPassword(password, salt, iterations)), Convert.FromBase64String(hash));

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>"7KQ4-M2XD-9PRT": easy to read out, no 0/O or 1/I.</summary>
    public static string NewInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 12).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    /// <summary>A password that's easy to read out ("plum-river-7342-cove") for admin resets.</summary>
    public static string NewTemporaryPassword()
    {
        string[] words = ["amber", "birch", "cedar", "delta", "ember", "frost", "grove", "harbor", "iris", "juniper", "kestrel", "lumen",
                          "maple", "nova", "orbit", "pine", "quartz", "river", "slate", "tide", "umber", "vale", "willow", "zephyr"];
        string W() => words[RandomNumberGenerator.GetInt32(words.Length)];
        return $"{W()}-{W()}-{RandomNumberGenerator.GetInt32(1000, 9999)}-{W()}";
    }
}

/// <summary>
/// Settings changed from the web panel, stored in the database so every server in a cluster shares them.
/// Reads are cached for a few seconds per server.
/// </summary>
public sealed class SettingsStore(Db db)
{
    private readonly ConcurrentDictionary<string, (string? Value, long At)> _cache = new();
    private const long CacheMs = 5000;

    public string ServerName { get => Get("server_name") ?? "GuardHawk Server"; set => Set("server_name", value); }
    public bool OpenSignup { get => Get("open_signup") == "1"; set => Set("open_signup", value ? "1" : "0"); }
    /// <summary>Role for people who sign up without an invite.</summary>
    public string SignupRole { get => Get("signup_role") is { } r && Sync.Protocol.Roles.IsValid(r) ? r : Sync.Protocol.Roles.Member; set => Set("signup_role", value); }
    /// <summary>0 = no limit.</summary>
    public int MaxUsers { get => int.TryParse(Get("max_users"), out int n) ? n : 0; set => Set("max_users", value.ToString()); }
    /// <summary>Syncing paused by an admin (survives restarts, applies to every server in the cluster).</summary>
    public bool Paused { get => Get("paused") == "1"; set => Set("paused", value ? "1" : "0"); }

    public string? Get(string key)
    {
        long now = Db.Now();
        if (_cache.TryGetValue(key, out var hit) && now - hit.At < CacheMs) return hit.Value;
        using var c = db.Open();
        var v = Db.Scalar(c, "SELECT value FROM settings WHERE key = @key", new { key }) as string;
        _cache[key] = (v, now);
        return v;
    }

    public void Set(string key, string value)
    {
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value", new { key, value });
        _cache[key] = (value, Db.Now());
    }
}

public sealed record ActivityEntry(long Id, DateTime At, string Kind, string? Org, string? Username, string Detail);

/// <summary>A history of what happened (sign-ups, sign-ins, admin actions), kept in the database (last 20,000).</summary>
public sealed class ActivityLog(Db db)
{
    private long _adds;

    public void Add(string kind, string? username, string detail, long? orgId = null)
    {
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO activity(at, kind, org_id, username, detail) VALUES(@at, @kind, @orgId, @username, @detail)",
            new { at = Db.Now(), kind, orgId, username, detail });
        if (Interlocked.Increment(ref _adds) % 200 == 0)
            Db.Exec(c, "DELETE FROM activity WHERE id < (SELECT MAX(id) FROM activity) - 20000");
    }

    public List<ActivityEntry> Recent(int count, string? username = null, long? orgId = null)
    {
        using var c = db.Open();
        string where = username != null ? "WHERE a.username = @username" : orgId != null ? "WHERE a.org_id = @orgId" : "";
        return Db.Query(c, $"""
            SELECT a.id, a.at, a.kind, o.name, a.username, a.detail FROM activity a LEFT JOIN orgs o ON o.id = a.org_id
            {where} ORDER BY a.id DESC LIMIT @count
            """,
            r => new ActivityEntry(Db.Long(r, 0), Db.Time(Db.Long(r, 1)), r.GetString(2), Db.Str(r, 3), Db.Str(r, 4), r.GetString(5)),
            username != null ? new { count, username = username.ToLowerInvariant() } : orgId != null ? new { count, orgId = orgId.Value } : (object)new { count });
    }
}

/// <summary>
/// Slows down password guessing: 10 failures for a name or address within 15 minutes locks it for 15 minutes.
/// Kept in the database so the limit holds across every server behind a load balancer.
/// </summary>
public sealed class LoginThrottle(Db db)
{
    private const int MaxFailures = 10;
    private const long WindowMs = 15 * 60_000;

    public bool IsLocked(params string[] keys)
    {
        using var c = db.Open();
        long now = Db.Now();
        return keys.Any(k => Db.Long(Db.Scalar(c, "SELECT locked_until FROM throttle WHERE key = @k", new { k })) > now);
    }

    public void Fail(params string[] keys)
    {
        using var c = db.Open();
        long now = Db.Now();
        foreach (var k in keys)
        {
            Db.Exec(c, """
                INSERT INTO throttle(key, failures, window_start, locked_until) VALUES(@k, 1, @now, 0)
                ON CONFLICT(key) DO UPDATE SET
                    failures = CASE WHEN throttle.window_start < @cutoff THEN 1 ELSE throttle.failures + 1 END,
                    window_start = CASE WHEN throttle.window_start < @cutoff THEN @now ELSE throttle.window_start END
                """, new { k, now, cutoff = now - WindowMs });
            Db.Exec(c, "UPDATE throttle SET locked_until = @until WHERE key = @k AND failures >= @max",
                new { k, until = now + WindowMs, max = MaxFailures });
        }
    }

    public void Succeed(params string[] keys)
    {
        using var c = db.Open();
        foreach (var k in keys) Db.Exec(c, "DELETE FROM throttle WHERE key = @k", new { k });
    }

    public void Cleanup()
    {
        using var c = db.Open();
        long now = Db.Now();
        Db.Exec(c, "DELETE FROM throttle WHERE locked_until < @now AND window_start < @cutoff", new { now, cutoff = now - WindowMs });
    }
}

public sealed record LogLine(DateTime At, string Level, string Category, string Message);

/// <summary>Keeps this server's last 2,000 log lines in memory for the web panel's Logs page.</summary>
public sealed class LogBuffer : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();
    public IEnumerable<LogLine> Lines => _lines.ToArray();

    public void Add(LogLine line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > 2000) _lines.TryDequeue(out _);
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(LogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (category.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics") || category.StartsWith("Microsoft.AspNetCore.Routing")) return;
            string msg = formatter(state, exception);
            if (exception != null) msg += " — " + exception.Message;
            buffer.Add(new LogLine(DateTime.UtcNow, logLevel.ToString(), category.Split('.').Last(), msg));
        }
    }
}

/// <summary>
/// This server's live numbers (requests and syncs per minute over the last hour) and whether syncing is paused
/// (a cluster-wide setting, re-read every few seconds).
/// </summary>
public sealed class ServiceState(SettingsStore settings, ServerConfig config)
{
    public string NodeId { get; } = $"{config.NodeName}-{Guid.NewGuid().ToString("N")[..6]}";
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public static string Version => typeof(ServiceState).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public bool Running => !settings.Paused;
    public void SetRunning(bool running) => settings.Paused = !running;

    /// <summary>Set while this server is shutting down (readiness fails so load balancers stop sending traffic).</summary>
    public volatile bool Stopping;

    private readonly long[] _requests = new long[60], _syncs = new long[60], _errors = new long[60];
    private long _minute = CurrentMinute();
    private readonly object _lock = new();
    public long TotalRequests, TotalSyncs, TotalErrors;
    private readonly ConcurrentDictionary<string, DateTime> _activeUsers = new();

    private static long CurrentMinute() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;

    private void Roll()
    {
        long now = CurrentMinute();
        if (now == _minute) return;
        for (long m = _minute + 1; m <= now && m <= _minute + 60; m++)
        {
            int i = (int)(m % 60);
            _requests[i] = 0; _syncs[i] = 0; _errors[i] = 0;
        }
        _minute = now;
    }

    public void CountRequest(bool error)
    {
        lock (_lock)
        {
            Roll();
            _requests[_minute % 60]++;
            if (error) _errors[_minute % 60]++;
        }
        Interlocked.Increment(ref TotalRequests);
        if (error) Interlocked.Increment(ref TotalErrors);
    }

    public void CountSync(string userKey)
    {
        lock (_lock)
        {
            Roll();
            _syncs[_minute % 60]++;
        }
        Interlocked.Increment(ref TotalSyncs);
        _activeUsers[userKey] = DateTime.UtcNow;
    }

    /// <summary>Oldest minute first.</summary>
    public (long[] Requests, long[] Syncs, long[] Errors) LastHour()
    {
        lock (_lock)
        {
            Roll();
            long[] Order(long[] a) => Enumerable.Range(1, 60).Select(k => a[(_minute + k) % 60]).ToArray();
            return (Order(_requests), Order(_syncs), Order(_errors));
        }
    }

    public long RequestsLastMinute
    {
        get { lock (_lock) { Roll(); return _requests[(_minute + 59) % 60]; } }
    }

    /// <summary>People who synced through this server in the last 15 minutes.</summary>
    public int OnlineUsers => _activeUsers.Count(kv => kv.Value > DateTime.UtcNow.AddMinutes(-15));
    public IEnumerable<string> OnlineUserKeys => _activeUsers.Where(kv => kv.Value > DateTime.UtcNow.AddMinutes(-15)).Select(kv => kv.Key);
}

public sealed record NodeInfo(string Id, string Name, string Host, string Version, string RunsAs, DateTime StartedAt, DateTime LastSeen,
    long Requests, long Errors, long Syncs, long RequestsLastMinute, long MemoryBytes, bool Online, bool Stopping, bool This);

/// <summary>
/// Every server writes a heartbeat to the nodes table every 15 seconds, so the web panel can show the whole cluster.
/// One server at a time (holding the "maintenance" lease) also tidies up expired sessions, throttles and old nodes.
/// </summary>
public sealed class Cluster(Db db, ServiceState state, ServerConfig config, LoginThrottle throttle, ILogger<Cluster> log) : BackgroundService
{
    public static readonly TimeSpan Beat = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        DateTime lastMaintenance = DateTime.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                Heartbeat();
                if (DateTime.UtcNow - lastMaintenance > TimeSpan.FromMinutes(10) && TryLease("maintenance", TimeSpan.FromMinutes(9)))
                {
                    lastMaintenance = DateTime.UtcNow;
                    Maintenance();
                }
            }
            catch (Exception e) { log.LogWarning("Heartbeat failed: {Message}", e.Message); }
            try { await Task.Delay(Beat, stop); } catch (OperationCanceledException) { }
        }
        try { Leave(); } catch { }
    }

    public void Heartbeat()
    {
        using var c = db.Open();
        Db.Exec(c, """
            INSERT INTO nodes(id, name, host, version, runs_as, started_at, last_seen, requests, errors, syncs, requests_last_minute, memory_bytes, stopping)
            VALUES(@id, @name, @host, @version, @runsAs, @started, @now, @requests, @errors, @syncs, @rpm, @memory, @stopping)
            ON CONFLICT(id) DO UPDATE SET last_seen = excluded.last_seen, requests = excluded.requests, errors = excluded.errors,
                syncs = excluded.syncs, requests_last_minute = excluded.requests_last_minute, memory_bytes = excluded.memory_bytes, stopping = excluded.stopping
            """, new
        {
            id = state.NodeId, name = config.NodeName, host = Environment.MachineName, version = ServiceState.Version, runsAs = config.HowItRuns,
            started = Db.ToMs(state.StartedAt), now = Db.Now(), requests = state.TotalRequests, errors = state.TotalErrors, syncs = state.TotalSyncs,
            rpm = state.RequestsLastMinute, memory = Process.GetCurrentProcess().WorkingSet64, stopping = state.Stopping,
        });
    }

    private void Leave()
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM nodes WHERE id = @id", new { id = state.NodeId });
    }

    public List<NodeInfo> Nodes()
    {
        using var c = db.Open();
        long online = Db.Now() - (long)(Beat.TotalMilliseconds * 3);
        return Db.Query(c, "SELECT id, name, host, version, runs_as, started_at, last_seen, requests, errors, syncs, requests_last_minute, memory_bytes, stopping FROM nodes ORDER BY started_at",
            r => new NodeInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), Db.Time(Db.Long(r, 5)), Db.Time(Db.Long(r, 6)),
                Db.Long(r, 7), Db.Long(r, 8), Db.Long(r, 9), Db.Long(r, 10), Db.Long(r, 11), Db.Long(r, 6) > online, Db.Flag(r, 12), r.GetString(0) == state.NodeId));
    }

    /// <summary>Takes (or renews) a named lease so only one server in the cluster does a job.</summary>
    public bool TryLease(string name, TimeSpan length)
    {
        using var c = db.Open();
        long now = Db.Now();
        Db.Exec(c, """
            INSERT INTO leases(name, holder, until_at) VALUES(@name, @holder, @until)
            ON CONFLICT(name) DO UPDATE SET holder = excluded.holder, until_at = excluded.until_at
            WHERE leases.until_at < @now OR leases.holder = excluded.holder
            """, new { name, holder = state.NodeId, until = now + (long)length.TotalMilliseconds, now });
        return Db.Scalar(c, "SELECT holder FROM leases WHERE name = @name", new { name }) as string == state.NodeId;
    }

    private void Maintenance()
    {
        using (var c = db.Open())
        {
            long now = Db.Now();
            Db.Exec(c, "DELETE FROM admin_sessions WHERE expires_at < @now", new { now });
            Db.Exec(c, "DELETE FROM nodes WHERE last_seen < @cutoff", new { cutoff = now - 24 * 3600_000L });
            Db.Exec(c, "DELETE FROM invites WHERE expires_at IS NOT NULL AND expires_at < @cutoff", new { cutoff = now - 90L * 24 * 3600_000 });
        }
        throttle.Cleanup();
    }
}
