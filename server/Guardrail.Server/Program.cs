using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Guardrail.Server;
using Guardrail.Sync;
using Microsoft.AspNetCore.HttpOverrides;

// Guardrail Server: the API the Guardrail apps sync with (/api/v1), a web panel to run it (/),
// health checks for load balancers (/healthz, /readyz) and Prometheus metrics (/metrics).

// `--healthcheck`: used by Docker's HEALTHCHECK (the image has no curl). Exit 0 when this server is ready.
if (args.Contains("--healthcheck"))
{
    int port = int.TryParse(Environment.GetEnvironmentVariable("GR_PORT"), out int hp) ? hp : Protocol.DefaultPort;
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        return (await http.GetAsync($"http://127.0.0.1:{port}/readyz")).IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}

// Find wwwroot next to the program wherever it's started from (unless a host, like the test host, sets its own)
bool rootGiven = args.Any(a => a.StartsWith("--contentRoot", StringComparison.OrdinalIgnoreCase)) || Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT") != null;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = rootGiven ? null : AppContext.BaseDirectory });
builder.Host.UseWindowsService(o => o.ServiceName = "GuardrailServer");
builder.Host.UseSystemd();

var config = ServerConfig.From(builder.Configuration);
Directory.CreateDirectory(config.DataFolder);

builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(config.Port);
    if (config.HttpsPort is int hp && !string.IsNullOrEmpty(config.HttpsCertificate))
        k.ListenAnyIP(hp, l => l.UseHttps(config.HttpsCertificate, config.HttpsCertificatePassword));
    k.Limits.MaxRequestBodySize = Protocol.MaxFileBytes + 1024 * 1024;
    k.AddServerHeader = false;
});
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(20));

var logBuffer = new LogBuffer();
builder.Logging.AddProvider(logBuffer);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(logBuffer);
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton(_ => BlobStore.Create(config));
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton<ServiceState>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<Accounts>();
builder.Services.AddSingleton<SyncStore>();
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddSingleton<ServerControl>();
builder.Services.AddSingleton<Cluster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Cluster>());
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Per-address request limit on the app API (each server counts its own; a load balancer spreads the load).
if (config.RateLimitPerMinute > 0)
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = 429;
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            http.Request.Path.StartsWithSegments("/api")
                ? RateLimitPartition.GetFixedWindowLimiter(ClientApi.Ip(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = config.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1) })
                : RateLimitPartition.GetNoLimiter("other"));
        o.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.StatusCode = 429;
            await ctx.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("rate_limited", "Too many requests from this address. Slow down and try again in a minute."), ct);
        };
    });

var app = builder.Build();
var log = app.Services.GetRequiredService<ILogger<Program>>();

if (config.TrustProxy)
{
    var fwd = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
    fwd.KnownNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

// Errors come back as { error, message } that the apps and the panel can show
app.Use(async (http, next) =>
{
    var state = http.RequestServices.GetRequiredService<ServiceState>();
    bool isApi = http.Request.Path.StartsWithSegments("/api");
    try
    {
        await next(http);
        if (isApi) state.CountRequest(http.Response.StatusCode >= 500);
    }
    catch (ApiProblem p)
    {
        if (isApi) state.CountRequest(false);
        await Problem(http, p.Status, p.Error, p.Message);
    }
    catch (BadHttpRequestException e)
    {
        if (isApi) state.CountRequest(false);
        await Problem(http, e.StatusCode, "bad_request", "The request couldn't be read.");
    }
    catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { }
    catch (Exception e) when (!http.Response.HasStarted)
    {
        if (isApi) state.CountRequest(true);
        log.LogError(e, "Error handling {Method} {Path}", http.Request.Method, http.Request.Path);
        await Problem(http, 500, "server_error", "Something went wrong on the server. The server's Logs page has details.");
    }
});

if (config.RateLimitPerMinute > 0) app.UseRateLimiter();

// App API: paused check and bearer sign-in
app.Use(async (http, next) =>
{
    var path = http.Request.Path;
    if (path.StartsWithSegments("/api/v1") && !path.StartsWithSegments("/api/v1/info"))
    {
        if (!http.RequestServices.GetRequiredService<ServiceState>().Running)
            throw new ApiProblem(503, "paused", "Syncing is paused on this Guardrail Server right now. Your data is safe on this device and syncs when it's started again.");
        string? auth = http.Request.Headers.Authorization;
        if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var user = http.RequestServices.GetRequiredService<Accounts>().Authenticate(auth[7..].Trim(), ClientApi.Ip(http));
            if (user != null) http.Items["user"] = user;
        }
    }
    await next(http);
});

// Web panel API: admin cookie, plus a custom header on changes (blocks cross-site form posts)
app.Use(async (http, next) =>
{
    var path = http.Request.Path;
    if (path.StartsWithSegments("/admin/api"))
    {
        bool open = path.StartsWithSegments("/admin/api/status") || path.StartsWithSegments("/admin/api/setup") || path.StartsWithSegments("/admin/api/login");
        if (!HttpMethods.IsGet(http.Request.Method) && http.Request.Headers["X-Guardrail"] != "admin")
            throw new ApiProblem(400, "bad_request", "Missing X-Guardrail header.");
        string? admin = http.RequestServices.GetRequiredService<AdminAuth>().SessionUser(http.Request.Cookies[AdminAuth.Cookie]);
        if (admin != null) http.Items["admin"] = admin;
        else if (!open) throw new ApiProblem(401, "unauthorized", "Sign in to the admin panel.");
    }
    await next(http);
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

// ----- Health and metrics (for load balancers, Kubernetes, Docker and Prometheus) -----

// Alive: the process answers.
app.MapGet("/healthz", () => Results.Text("ok"));
// Ready: the database and file storage answer and this server isn't shutting down.
app.MapGet("/readyz", async (Db db, BlobStore blobs, ServiceState state) =>
{
    if (state.Stopping) return Results.Text("stopping", statusCode: 503);
    if (!db.Ping()) return Results.Text("database unavailable", statusCode: 503);
    if (!await blobs.Check()) return Results.Text("file storage unavailable", statusCode: 503);
    return Results.Text("ready");
});
app.MapGet("/metrics", (HttpContext http, ServiceState state, ServerConfig cfg, Accounts accounts, SyncStore sync) =>
{
    if (!string.IsNullOrEmpty(cfg.MetricsToken) && http.Request.Headers.Authorization != "Bearer " + cfg.MetricsToken)
        return Results.Text("unauthorized", statusCode: 401);
    var totals = sync.Totals();
    var sb = new StringBuilder();
    void Metric(string name, string type, string help, double value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        sb.Append(name).Append("{node=\"").Append(cfg.NodeName).Append("\"} ").Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }
    Metric("guardrail_requests_total", "counter", "App API requests handled by this server.", state.TotalRequests);
    Metric("guardrail_request_errors_total", "counter", "App API requests that failed with a server error.", state.TotalErrors);
    Metric("guardrail_syncs_total", "counter", "Sync calls (push or pull) handled by this server.", state.TotalSyncs);
    Metric("guardrail_online_users", "gauge", "People who synced through this server in the last 15 minutes.", state.OnlineUsers);
    Metric("guardrail_sync_running", "gauge", "1 when syncing is on, 0 when paused from the web panel.", state.Running ? 1 : 0);
    Metric("guardrail_records", "gauge", "Records stored (all organizations).", totals.Records);
    Metric("guardrail_files", "gauge", "Files stored (all organizations).", totals.Files);
    Metric("guardrail_file_bytes", "gauge", "Bytes of files stored.", totals.FileBytes);
    Metric("guardrail_memory_bytes", "gauge", "Working set of this server process.", System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);
    Metric("guardrail_uptime_seconds", "gauge", "Seconds since this server started.", (DateTime.UtcNow - state.StartedAt).TotalSeconds);
    return Results.Text(sb.ToString(), "text/plain; version=0.0.4");
});

ClientApi.Map(app);
AdminApi.Map(app);
app.MapFallback(async http =>
{
    if (http.Request.Path.StartsWithSegments("/api") || http.Request.Path.StartsWithSegments("/admin/api"))
        await Problem(http, 404, "not_found", "No such endpoint.");
    else
        await http.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

// Make sure the database, the first organization and (with S3) the bucket exist before taking traffic
app.Services.GetRequiredService<Accounts>().DefaultOrgId();
if (app.Services.GetRequiredService<BlobStore>() is S3BlobStore s3) await s3.EnsureBucket(log);

// First admin from GR_ADMIN_USERNAME / GR_ADMIN_PASSWORD (otherwise the web panel asks)
var admins = app.Services.GetRequiredService<AdminAuth>();
if (!string.IsNullOrEmpty(config.BootstrapAdmin) && !string.IsNullOrEmpty(config.BootstrapPassword) && admins.NeedsSetup())
{
    admins.Create(config.BootstrapAdmin, config.BootstrapPassword);
    log.LogInformation("Created admin account {Admin} from the environment", config.BootstrapAdmin);
}

var serviceState = app.Services.GetRequiredService<ServiceState>();
app.Lifetime.ApplicationStarted.Register(() =>
{
    log.LogInformation("Guardrail Server {Version} ({How}, node {Node}) — {Db} database, files in {Storage}",
        ServiceState.Version, config.HowItRuns, serviceState.NodeId, config.DatabaseLabel, app.Services.GetRequiredService<BlobStore>().Describe);
    log.LogInformation("Web panel and sync: http://localhost:{Port}/{Https}", config.Port, config.HttpsPort is int p ? $" and https on port {p}" : "");
    if (admins.NeedsSetup()) log.LogWarning("No admin yet: open the web panel to create one");
});
// Fail readiness first so a load balancer stops sending new requests while in-flight ones finish
app.Lifetime.ApplicationStopping.Register(() => serviceState.Stopping = true);

await app.RunAsync();

if (ServerControl.RelaunchAfterExit) ServerControl.Relaunch();
return ServerControl.ExitCode;

static Task Problem(HttpContext http, int status, string error, string message)
{
    if (http.Response.HasStarted) return Task.CompletedTask;
    http.Response.Clear();
    http.Response.StatusCode = status;
    return http.Response.WriteAsJsonAsync(new ErrorResponse(error, message));
}

public partial class Program { }
