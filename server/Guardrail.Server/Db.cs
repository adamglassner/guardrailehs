using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Guardrail.Server;

/// <summary>
/// The database: SQLite (guardrail.db in the data folder) for a single server, or PostgreSQL so any number of servers
/// can share it. SQL is written once: parameters are @name, times are Unix milliseconds, flags are 0/1 integers,
/// and the few type names that differ are filled in by <see cref="Schema"/>.
/// </summary>
public sealed class Db
{
    private readonly ServerConfig _config;
    private readonly string _connectionString;
    private readonly NpgsqlDataSource? _pg;

    public bool IsPostgres => _pg != null;
    /// <summary>The SQLite file (empty for PostgreSQL).</summary>
    public string Path { get; } = "";

    public Db(ServerConfig config)
    {
        _config = config;
        if (config.UsesPostgres)
        {
            _connectionString = config.DatabaseConnection!;
            _pg = NpgsqlDataSource.Create(_connectionString);
        }
        else
        {
            Path = System.IO.Path.Combine(config.DataFolder, "guardrail.db");
            System.IO.Directory.CreateDirectory(config.DataFolder);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Cache = SqliteCacheMode.Default, DefaultTimeout = 30, Pooling = true }.ToString();
        }
        Migrate();
    }

    public DbConnection Open()
    {
        if (_pg != null) return _pg.OpenConnection();
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 30000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>
    /// Starts a write transaction. On SQLite it takes the write lock up front (BEGIN IMMEDIATE) so two writers never
    /// deadlock upgrading read locks.
    /// </summary>
    public DbTransaction Begin(DbConnection c) =>
        c is SqliteConnection s ? s.BeginTransaction(deferred: false) : c.BeginTransaction(IsolationLevel.ReadCommitted);

    // ----- Schema -----

    /// <summary>Schema versions in order. Never edit one that has shipped: add a new one.</summary>
    private static readonly string[] Migrations =
    [
        // 1: first version
        """
        CREATE TABLE orgs (
            id {id},
            name TEXT NOT NULL,
            seq {int} NOT NULL DEFAULT 0,
            created_at {int} NOT NULL
        );
        CREATE TABLE users (
            id {id},
            org_id {int} NOT NULL REFERENCES orgs(id) ON DELETE CASCADE,
            username TEXT NOT NULL,
            username_key TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            email TEXT,
            role TEXT NOT NULL,
            pw_hash TEXT NOT NULL, pw_salt TEXT NOT NULL, pw_iterations {int} NOT NULL,
            disabled {int} NOT NULL DEFAULT 0,
            created_at {int} NOT NULL,
            last_seen_at {int}
        );
        CREATE INDEX users_by_org ON users(org_id);
        CREATE TABLE sessions (
            id TEXT PRIMARY KEY,
            token_hash TEXT NOT NULL UNIQUE,
            user_id {int} NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            device_name TEXT NOT NULL, platform TEXT NOT NULL, app_version TEXT NOT NULL,
            ip TEXT, created_at {int} NOT NULL, last_seen_at {int} NOT NULL
        );
        CREATE INDEX sessions_by_user ON sessions(user_id);
        CREATE TABLE records (
            org_id {int} NOT NULL REFERENCES orgs(id) ON DELETE CASCADE,
            collection TEXT NOT NULL,
            id TEXT NOT NULL,
            seq {int} NOT NULL,
            number {int} NOT NULL DEFAULT 0,
            modified {int} NOT NULL,
            deleted {int} NOT NULL DEFAULT 0,
            payload TEXT,
            updated_by TEXT,
            updated_at {int} NOT NULL,
            PRIMARY KEY (org_id, collection, id)
        );
        CREATE INDEX records_by_seq ON records(org_id, seq);
        CREATE TABLE counters (
            org_id {int} NOT NULL REFERENCES orgs(id) ON DELETE CASCADE,
            collection TEXT NOT NULL,
            value {int} NOT NULL,
            PRIMARY KEY (org_id, collection)
        );
        CREATE TABLE files (
            org_id {int} NOT NULL REFERENCES orgs(id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            size {int} NOT NULL,
            uploaded_by TEXT,
            uploaded_at {int} NOT NULL,
            PRIMARY KEY (org_id, name)
        );
        CREATE TABLE invites (
            code TEXT PRIMARY KEY,
            org_id {int} REFERENCES orgs(id) ON DELETE CASCADE,
            new_org_name TEXT,
            role TEXT NOT NULL,
            username TEXT, note TEXT,
            uses_left {int} NOT NULL,
            created_at {int} NOT NULL, expires_at {int},
            used_by TEXT
        );
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE admins (
            username TEXT PRIMARY KEY,
            hash TEXT NOT NULL, salt TEXT NOT NULL, iterations {int} NOT NULL, created_at {int} NOT NULL
        );
        CREATE TABLE admin_sessions (
            token_hash TEXT PRIMARY KEY, username TEXT NOT NULL, expires_at {int} NOT NULL
        );
        CREATE TABLE activity (
            id {id},
            at {int} NOT NULL,
            kind TEXT NOT NULL,
            org_id {int},
            username TEXT,
            detail TEXT NOT NULL
        );
        CREATE TABLE throttle (
            key TEXT PRIMARY KEY,
            failures {int} NOT NULL,
            window_start {int} NOT NULL,
            locked_until {int} NOT NULL
        );
        CREATE TABLE nodes (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL, host TEXT NOT NULL, version TEXT NOT NULL, runs_as TEXT NOT NULL,
            started_at {int} NOT NULL, last_seen {int} NOT NULL,
            requests {int} NOT NULL, errors {int} NOT NULL, syncs {int} NOT NULL,
            requests_last_minute {int} NOT NULL, memory_bytes {int} NOT NULL,
            stopping {int} NOT NULL DEFAULT 0
        );
        CREATE TABLE leases (
            name TEXT PRIMARY KEY, holder TEXT NOT NULL, until_at {int} NOT NULL
        );
        """,
    ];

    private string Schema(string sql) => sql
        .Replace("{id}", IsPostgres ? "BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY" : "INTEGER PRIMARY KEY AUTOINCREMENT")
        .Replace("{int}", IsPostgres ? "BIGINT" : "INTEGER");

    private void Migrate()
    {
        using var c = Open();
        if (!IsPostgres) Exec(c, "PRAGMA journal_mode = WAL;");
        // Several servers can start at once against PostgreSQL: only one migrates, the others wait for it.
        if (IsPostgres) Exec(c, "SELECT pg_advisory_lock(7204511)");
        try
        {
            Exec(c, "CREATE TABLE IF NOT EXISTS schema_version (version BIGINT NOT NULL)");
            long version = Long(Scalar(c, "SELECT MAX(version) FROM schema_version"));
            for (int v = (int)version; v < Migrations.Length; v++)
            {
                using var tx = Begin(c);
                foreach (var statement in Schema(Migrations[v]).Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
                    Exec(c, statement, tx: tx);
                Exec(c, "INSERT INTO schema_version(version) VALUES(@v)", new { v = v + 1 }, tx);
                tx.Commit();
            }
            // The first organization, created here (under the lock) so servers starting together don't each make one.
            if (Long(Scalar(c, "SELECT COUNT(*) FROM orgs")) == 0)
                Exec(c, "INSERT INTO orgs(name, seq, created_at) VALUES(@name, 0, @now)",
                    new { name = _config.OrgName is { Length: > 0 } n ? n.Trim() : "My organization", now = Now() });
        }
        finally
        {
            if (IsPostgres) Exec(c, "SELECT pg_advisory_unlock(7204511)");
        }
    }

    public int SchemaVersion => Migrations.Length;

    // ----- Helpers -----

    public static int Exec(DbConnection c, string sql, object? args = null, DbTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(DbConnection c, string sql, object? args = null, DbTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public static List<T> Query<T>(DbConnection c, string sql, Func<DbDataReader, T> map, object? args = null, DbTransaction? tx = null)
    {
        using var cmd = Command(c, sql, args, tx);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    /// <summary>
    /// Parameters come from an anonymous object: new { id = 1 } binds @id. Booleans become 0/1, DateTimes Unix ms.
    /// Don't write "@x IS NULL OR …": PostgreSQL can't tell the type of a null parameter there. Build the SQL instead.
    /// </summary>
    public static DbCommand Command(DbConnection c, string sql, object? args, DbTransaction? tx)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        if (args != null)
            foreach (var p in args.GetType().GetProperties())
            {
                var param = cmd.CreateParameter();
                param.ParameterName = "@" + p.Name;
                object? value = p.GetValue(args);
                param.Value = value switch
                {
                    null => DBNull.Value,
                    bool b => b ? 1L : 0L,
                    int i => (long)i,
                    DateTime d => ToMs(d),
                    _ => value,
                };
                cmd.Parameters.Add(param);
            }
        return cmd;
    }

    public static long Long(object? v) => v == null || v is DBNull ? 0 : Convert.ToInt64(v);
    public static long Long(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
    public static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static bool Flag(DbDataReader r, int i) => Long(r, i) != 0;

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static long ToMs(DateTime d) => new DateTimeOffset(d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime()).ToUnixTimeMilliseconds();
    public static DateTime Time(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    public static DateTime? Time(DbDataReader r, int i) => r.IsDBNull(i) ? null : Time(Long(r, i));

    /// <summary>Whether the database answers (for /readyz).</summary>
    public bool Ping()
    {
        try
        {
            using var c = Open();
            return Long(Scalar(c, "SELECT 1")) == 1;
        }
        catch { return false; }
    }

    /// <summary>Size on disk: the SQLite file, or the PostgreSQL database.</summary>
    public long SizeBytes()
    {
        try
        {
            if (!IsPostgres) return new[] { Path, Path + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            using var c = Open();
            return Long(Scalar(c, "SELECT pg_database_size(current_database())"));
        }
        catch { return -1; }
    }

    /// <summary>A consistent copy of the SQLite database (the web panel's "Download backup").</summary>
    public string BackupTo(string folder)
    {
        if (IsPostgres) throw new ApiProblem(400, "not_sqlite", "This server uses PostgreSQL: back it up with pg_dump, or use Export all data.");
        string file = System.IO.Path.Combine(folder, $"guardrail-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        using var c = Open();
        Exec(c, "VACUUM INTO @file", new { file });
        return file;
    }

    /// <summary>"PostgreSQL 16.4 at db:5432/guardrail" or the SQLite file path (passwords left out).</summary>
    public string Describe()
    {
        if (!IsPostgres) return Path;
        try
        {
            var b = new NpgsqlConnectionStringBuilder(_connectionString);
            return $"{b.Host}:{b.Port}/{b.Database}";
        }
        catch { return "PostgreSQL"; }
    }

    public string EngineVersion()
    {
        try
        {
            using var c = Open();
            return IsPostgres ? "PostgreSQL " + (Scalar(c, "SHOW server_version") as string ?? "") : "SQLite " + (Scalar(c, "SELECT sqlite_version()") as string ?? "");
        }
        catch { return _config.DatabaseLabel; }
    }
}

/// <summary>An error that goes back to the caller as { error, message } with an HTTP status.</summary>
public sealed class ApiProblem(int status, string error, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Error { get; } = error;
}
