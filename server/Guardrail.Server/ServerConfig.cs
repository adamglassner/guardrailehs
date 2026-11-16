namespace Guardrail.Server;

/// <summary>
/// Settings that need a restart, from environment variables (GR_*) or appsettings.json (the "Guardrail" section).
/// Everything has a default, so a plain start gives a single server with SQLite and files in the data folder.
/// </summary>
public sealed class ServerConfig
{
    public string DataFolder { get; init; } = "";
    public int Port { get; init; } = Sync.Protocol.DefaultPort;
    public int? HttpsPort { get; init; }
    public string? HttpsCertificate { get; init; }
    public string? HttpsCertificatePassword { get; init; }
    public string? BootstrapAdmin { get; init; }
    public string? BootstrapPassword { get; init; }
    /// <summary>Name for the first organization (default "My organization").</summary>
    public string? OrgName { get; init; }

    /// <summary>"sqlite" (default, one server) or "postgres" (any number of servers sharing one database).</summary>
    public string Database { get; init; } = "sqlite";
    /// <summary>PostgreSQL connection string, e.g. "Host=db;Database=guardrail;Username=guardrail;Password=…".</summary>
    public string? DatabaseConnection { get; init; }

    /// <summary>"local" (files in the data folder, or a shared folder) or "s3" (S3, MinIO, R2, Backblaze B2, Wasabi…).</summary>
    public string Storage { get; init; } = "local";
    /// <summary>Where "local" files go (default: data folder\files). Point every node at the same share to scale out.</summary>
    public string? FilesFolder { get; init; }
    public string? S3Endpoint { get; init; }
    public string? S3Region { get; init; }
    public string? S3Bucket { get; init; }
    public string? S3AccessKey { get; init; }
    public string? S3SecretKey { get; init; }
    public bool S3PathStyle { get; init; } = true;

    /// <summary>Trust X-Forwarded-For / -Proto from a reverse proxy or load balancer.</summary>
    public bool TrustProxy { get; init; }
    /// <summary>When set, /metrics needs "Authorization: Bearer &lt;token&gt;".</summary>
    public string? MetricsToken { get; init; }
    /// <summary>Shown on the Nodes page (default: the machine or container name).</summary>
    public string NodeName { get; init; } = Environment.MachineName;
    /// <summary>Requests per minute one address may make to the app API (0 = no limit).</summary>
    public int RateLimitPerMinute { get; init; } = 1200;

    public bool InContainer { get; init; }
    public bool AsService { get; init; }
    public bool InKubernetes { get; init; }

    public bool UsesPostgres => Database == "postgres";

    public static ServerConfig From(IConfiguration c)
    {
        string? Get(string env, string key) => Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : c[$"Guardrail:{key}"];
        bool Bool(string env, string key, bool def) => Get(env, key) is { } v ? v is "1" or "true" or "True" or "yes" : def;

        bool container = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        string data = Get("GR_DATA", "DataFolder") ?? (container ? "/data" : Path.Combine(AppContext.BaseDirectory, "data"));
        string Full(string p) => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));

        string db = (Get("GR_DB", "Database") ?? "sqlite").Trim().ToLowerInvariant();
        if (db is "postgresql" or "pg") db = "postgres";
        if (db is not ("sqlite" or "postgres")) throw new InvalidOperationException($"GR_DB must be \"sqlite\" or \"postgres\" (it's \"{db}\").");
        string? conn = Get("GR_DB_CONNECTION", "DatabaseConnection");
        if (db == "postgres" && string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("GR_DB is postgres but GR_DB_CONNECTION (the PostgreSQL connection string) isn't set.");

        string storage = (Get("GR_STORAGE", "Storage") ?? "local").Trim().ToLowerInvariant();
        if (storage is not ("local" or "s3")) throw new InvalidOperationException($"GR_STORAGE must be \"local\" or \"s3\" (it's \"{storage}\").");
        string? bucket = Get("GR_S3_BUCKET", "S3Bucket");
        if (storage == "s3" && string.IsNullOrWhiteSpace(bucket)) throw new InvalidOperationException("GR_STORAGE is s3 but GR_S3_BUCKET isn't set.");

        string dataFull = Full(data);
        return new ServerConfig
        {
            DataFolder = dataFull,
            Port = int.TryParse(Get("GR_PORT", "Port"), out int p) ? p : Sync.Protocol.DefaultPort,
            HttpsPort = int.TryParse(Get("GR_HTTPS_PORT", "HttpsPort"), out int hp) ? hp : null,
            HttpsCertificate = Get("GR_HTTPS_CERT", "HttpsCertificate"),
            HttpsCertificatePassword = Get("GR_HTTPS_CERT_PASSWORD", "HttpsCertificatePassword"),
            BootstrapAdmin = Get("GR_ADMIN_USERNAME", "AdminUsername"),
            BootstrapPassword = Get("GR_ADMIN_PASSWORD", "AdminPassword"),
            OrgName = Get("GR_ORG_NAME", "OrganizationName"),
            Database = db,
            DatabaseConnection = conn,
            Storage = storage,
            FilesFolder = Get("GR_FILES", "FilesFolder") is { Length: > 0 } f ? Full(f) : Path.Combine(dataFull, "files"),
            S3Endpoint = Get("GR_S3_ENDPOINT", "S3Endpoint"),
            S3Region = Get("GR_S3_REGION", "S3Region"),
            S3Bucket = bucket,
            S3AccessKey = Get("GR_S3_ACCESS_KEY", "S3AccessKey"),
            S3SecretKey = Get("GR_S3_SECRET_KEY", "S3SecretKey"),
            S3PathStyle = Bool("GR_S3_PATH_STYLE", "S3PathStyle", true),
            TrustProxy = Bool("GR_TRUST_PROXY", "TrustProxy", false),
            MetricsToken = Get("GR_METRICS_TOKEN", "MetricsToken"),
            NodeName = Get("GR_NODE_NAME", "NodeName") ?? Environment.MachineName,
            RateLimitPerMinute = int.TryParse(Get("GR_RATE_LIMIT", "RateLimitPerMinute"), out int rl) ? Math.Max(0, rl) : 1200,
            InContainer = container,
            InKubernetes = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") != null,
            AsService = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
                        || Microsoft.Extensions.Hosting.Systemd.SystemdHelpers.IsSystemdService(),
        };
    }

    public string HowItRuns => InKubernetes ? "Kubernetes" : InContainer ? "Docker" : AsService ? (OperatingSystem.IsWindows() ? "Windows service" : "systemd service") : "Standalone";

    public string DatabaseLabel => UsesPostgres ? "PostgreSQL" : "SQLite";
    public string StorageLabel => Storage == "s3" ? $"S3 ({S3Bucket}{(S3Endpoint is { Length: > 0 } e ? " at " + e : "")})" : "Folder";
}
