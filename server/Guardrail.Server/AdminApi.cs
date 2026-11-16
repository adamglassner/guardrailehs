using System.Diagnostics;
using System.Runtime.InteropServices;
using Guardrail.Sync;

namespace Guardrail.Server;

public sealed record SetupRequest(string Username, string Password, string? OrgName);
public sealed record AdminLoginRequest(string Username, string Password);
public sealed record AdminPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record NewAdminRequest(string Username, string Password);
public sealed record InviteRequest(long? OrgId, string? NewOrgName, string? Role, string? Username, string? Note, int Uses = 1, int? Days = 14);
public sealed record SettingsRequest(string? ServerName, bool? OpenSignup, int? MaxUsers, string? SignupRole);
public sealed record RoleRequest(string Role);
public sealed record ProfileRequest(string? DisplayName, string? Email);
public sealed record NewUserRequest(long? OrgId, string Username, string? DisplayName, string? Email, string? Role);
public sealed record OrgRequest(string Name);

/// <summary>The web panel's API: /admin/api/… (cookie sign-in; changes also need the X-Guardrail header).</summary>
public static class AdminApi
{
    public static void Map(WebApplication app)
    {
        var a = app.MapGroup("/admin/api");

        a.MapGet("/status", (AdminAuth auth, HttpContext http) =>
            new { needsSetup = auth.NeedsSetup(), signedIn = auth.SessionUser(http.Request.Cookies[AdminAuth.Cookie]), version = ServiceState.Version });

        a.MapPost("/setup", (SetupRequest r, AdminAuth auth, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            if (!auth.NeedsSetup()) throw new ApiProblem(409, "already_set_up", "An admin account already exists. Sign in instead.");
            auth.Create(r.Username, r.Password);
            long org = accounts.DefaultOrgId();
            if (!string.IsNullOrWhiteSpace(r.OrgName)) accounts.RenameOrg(org, r.OrgName);
            log.Add("admin", null, $"Admin account \"{r.Username.Trim().ToLowerInvariant()}\" created");
            SetCookie(http, auth.NewSession(r.Username));
            return Results.Ok(new { ok = true });
        });

        a.MapPost("/login", (AdminLoginRequest r, AdminAuth auth, LoginThrottle t, ActivityLog log, HttpContext http) =>
        {
            string ip = ClientApi.Ip(http);
            if (t.IsLocked("admin:" + ip)) throw new ApiProblem(429, "too_many_attempts", "Too many tries. Wait 15 minutes and try again.");
            if (!auth.Check(r.Username, r.Password))
            {
                t.Fail("admin:" + ip);
                log.Add("admin", null, $"Failed admin sign-in for \"{r.Username}\" from {ip}");
                throw new ApiProblem(401, "bad_login", "Wrong admin name or password.");
            }
            t.Succeed("admin:" + ip);
            SetCookie(http, auth.NewSession(r.Username));
            return Results.Ok(new { ok = true });
        });

        a.MapPost("/logout", (AdminAuth auth, HttpContext http) =>
        {
            auth.EndSession(http.Request.Cookies[AdminAuth.Cookie]);
            http.Response.Cookies.Delete(AdminAuth.Cookie);
            return Results.Ok(new { ok = true });
        });

        // Everything below needs an admin sign-in (checked by the middleware in Program.cs)

        a.MapPost("/password", (AdminPasswordRequest r, AdminAuth auth, HttpContext http) =>
        {
            string admin = Admin(http);
            if (!auth.Check(admin, r.CurrentPassword)) throw new ApiProblem(403, "bad_password", "Your current password isn't right.");
            auth.Create(admin, r.NewPassword);
            return Results.Ok(new { ok = true });
        });

        a.MapGet("/admins", (AdminAuth auth) => auth.List());
        a.MapPost("/admins", (NewAdminRequest r, AdminAuth auth, ActivityLog log, HttpContext http) =>
        {
            if (auth.List().Contains((r.Username ?? "").Trim().ToLowerInvariant())) throw new ApiProblem(409, "taken", "There's already an admin with that name.");
            auth.Create(r.Username!, r.Password);
            log.Add("admin", null, $"{Admin(http)} added admin \"{r.Username!.Trim().ToLowerInvariant()}\"");
            return Results.Ok(new { ok = true });
        });
        a.MapDelete("/admins/{name}", (string name, AdminAuth auth, ActivityLog log, HttpContext http) =>
        {
            if (name == Admin(http)) throw new ApiProblem(400, "self", "You can't remove yourself.");
            auth.Delete(name);
            log.Add("admin", null, $"{Admin(http)} removed admin \"{name}\"");
            return Results.Ok(new { ok = true });
        });

        a.MapGet("/overview", (ServiceState state, Accounts accounts, SyncStore sync, SettingsStore settings, ActivityLog log, Db db,
            BlobStore blobs, Cluster cluster, ServerConfig config) =>
        {
            var users = accounts.List();
            var totals = sync.Totals();
            var (requests, syncs, errors) = state.LastHour();
            var nodes = cluster.Nodes();
            var orgs = accounts.Orgs();
            return new
            {
                serverName = settings.ServerName,
                running = state.Running,
                version = ServiceState.Version,
                startedAt = state.StartedAt,
                uptimeSeconds = (long)(DateTime.UtcNow - state.StartedAt).TotalSeconds,
                runsAs = config.HowItRuns,
                os = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                dataFolder = config.DataFolder,
                port = config.Port,
                httpsPort = config.HttpsPort,
                database = config.DatabaseLabel,
                databaseWhere = db.Describe(),
                databaseVersion = db.EngineVersion(),
                storage = config.StorageLabel,
                storageWhere = blobs.Describe,
                openSignup = settings.OpenSignup,
                orgs = orgs.Count,
                orgName = orgs.FirstOrDefault()?.Name ?? config.OrgName ?? "My organization",
                users = users.Count,
                disabledUsers = users.Count(u => u.Disabled),
                onlineUsers = state.OnlineUsers,
                activeThisWeek = users.Count(u => u.LastSeenAt > DateTime.UtcNow.AddDays(-7)),
                devices = users.Sum(u => u.Devices),
                records = totals.Records,
                files = totals.Files,
                fileBytes = totals.FileBytes,
                databaseBytes = db.SizeBytes(),
                freeDiskBytes = blobs.FreeBytes,
                memoryBytes = Process.GetCurrentProcess().WorkingSet64,
                nodeId = state.NodeId,
                nodesOnline = nodes.Count(n => n.Online),
                clusterRequestsPerMinute = nodes.Where(n => n.Online).Sum(n => n.RequestsLastMinute),
                totalRequests = state.TotalRequests,
                totalSyncs = state.TotalSyncs,
                requestsPerMinute = requests,
                syncsPerMinute = syncs,
                errorsPerMinute = errors,
                recentActivity = log.Recent(12),
            };
        });

        a.MapGet("/nodes", (Cluster cluster) => cluster.Nodes());

        // ----- Service control -----

        a.MapPost("/service/stop", (ServiceState state, ActivityLog log, HttpContext http) =>
        {
            state.SetRunning(false);
            log.Add("service", null, $"Syncing paused by {Admin(http)}");
            return Results.Ok(new { running = false });
        });

        a.MapPost("/service/start", (ServiceState state, ActivityLog log, HttpContext http) =>
        {
            state.SetRunning(true);
            log.Add("service", null, $"Syncing started by {Admin(http)}");
            return Results.Ok(new { running = true });
        });

        a.MapPost("/service/restart", (ServerControl control, ActivityLog log, ServerConfig config, HttpContext http) =>
        {
            log.Add("service", null, $"Server restart requested by {Admin(http)} ({config.NodeName})");
            control.RestartSoon();
            return Results.Ok(new { restarting = true });
        });

        a.MapPost("/service/shutdown", (ServerControl control, ActivityLog log, ServerConfig config, HttpContext http) =>
        {
            log.Add("service", null, $"Server shut down by {Admin(http)} ({config.NodeName})");
            control.ShutdownSoon();
            return Results.Ok(new { stopping = true, comesBack = control.ComesBackByItself, how = config.HowItRuns });
        });

        // ----- Organizations -----

        a.MapGet("/orgs", (Accounts accounts) => accounts.Orgs());
        a.MapPut("/orgs/{id:long}", (long id, OrgRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            accounts.RenameOrg(id, r.Name);
            log.Add("admin", null, $"{Admin(http)} renamed an organization to \"{r.Name.Trim()}\"", id);
            return Results.Ok(new { ok = true });
        });
        a.MapPost("/orgs/{id:long}/erase", async (long id, SyncStore sync, BlobStore blobs, ActivityLog log, HttpContext http) =>
        {
            await sync.EraseData(id, blobs);
            log.Add("admin", null, $"{Admin(http)} erased the organization's synced data", id);
            return Results.Ok(new { ok = true });
        });
        a.MapDelete("/orgs/{id:long}", (long id, Accounts accounts, BlobStore blobs, ActivityLog log, HttpContext http) =>
        {
            string? name = accounts.Orgs().FirstOrDefault(o => o.Id == id)?.Name ?? throw new ApiProblem(404, "not_found", "No such organization.");
            accounts.DeleteOrg(id, blobs);
            log.Add("admin", null, $"{Admin(http)} deleted the organization \"{name}\" and everything in it");
            return Results.Ok(new { ok = true });
        });

        // ----- Users -----

        a.MapGet("/users", (Accounts accounts) => accounts.List());
        a.MapPost("/users", (NewUserRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            long org = r.OrgId ?? accounts.DefaultOrgId();
            var (user, password) = accounts.CreateUser(org, r.Username, r.DisplayName, r.Email, r.Role ?? Protocol.Roles.Member);
            log.Add("admin", user.Username, $"{Admin(http)} created the account {user.Username} ({Protocol.Roles.Label(user.Role)})", org);
            return new { user, password };
        });
        a.MapGet("/users/{id:long}", (long id, Accounts accounts, ActivityLog log) =>
        {
            var user = accounts.Get(id);
            return new { user, devices = accounts.AllDevices(id), activity = log.Recent(30, user.Username) };
        });
        a.MapPost("/users/{id:long}/disable", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "turned off", () => accounts.SetDisabled(id, true)));
        a.MapPost("/users/{id:long}/enable", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "turned on", () => accounts.SetDisabled(id, false)));
        a.MapPost("/users/{id:long}/signout", (long id, Accounts accounts, ActivityLog log, HttpContext http) => UserAction(id, accounts, log, http, "signed out everywhere", () => accounts.SignOutEverywhere(id)));
        a.MapPut("/users/{id:long}/role", (long id, RoleRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
            UserAction(id, accounts, log, http, $"made {Protocol.Roles.Label(r.Role)}", () => accounts.SetRole(id, r.Role)));
        a.MapPut("/users/{id:long}/profile", (long id, ProfileRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
            UserAction(id, accounts, log, http, "had their name or email changed", () => accounts.SetProfile(id, r.DisplayName, r.Email)));
        a.MapPost("/users/{id:long}/reset-password", (long id, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            string name = accounts.NameOf(id) ?? throw new ApiProblem(404, "not_found", "No such user.");
            string password = accounts.ResetPassword(id);
            log.Add("password", name, $"{Admin(http)} reset {name}'s password (signed out everywhere)", accounts.OrgOf(id));
            return new { password };
        });
        a.MapDelete("/users/{id:long}", (long id, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            string name = accounts.NameOf(id) ?? throw new ApiProblem(404, "not_found", "No such user.");
            long org = accounts.OrgOf(id);
            accounts.Delete(id);
            log.Add("admin", name, $"{name} was deleted by {Admin(http)} (records they made stay)", org);
            return Results.Ok(new { ok = true });
        });
        a.MapGet("/devices", (Accounts accounts) => accounts.AllDevices());
        a.MapDelete("/devices/{id}", (string id, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            accounts.RevokeDevice(id);
            log.Add("admin", null, $"{Admin(http)} signed out device {id}");
            return Results.Ok(new { ok = true });
        });

        // ----- Invites -----

        a.MapGet("/invites", (Accounts accounts) => accounts.Invites());
        a.MapPost("/invites", (InviteRequest r, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            var invite = accounts.CreateInvite(r.OrgId, r.NewOrgName, r.Role ?? Protocol.Roles.Member, r.Username, r.Note, r.Uses, r.Days);
            log.Add("invite", invite.Username, $"{Admin(http)} created invite {invite.Code}" + (invite.NewOrgName != null ? $" for a new organization \"{invite.NewOrgName}\"" : "")
                + (invite.Username != null ? $" for {invite.Username}" : ""), invite.OrgId);
            return invite;
        });
        a.MapDelete("/invites/{code}", (string code, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            accounts.DeleteInvite(code);
            log.Add("invite", null, $"{Admin(http)} deleted invite {code}");
            return Results.Ok(new { ok = true });
        });

        // ----- Data (read-only) -----

        a.MapGet("/data/collections", (long? org, SyncStore sync, Accounts accounts) => sync.Collections(org ?? accounts.DefaultOrgId()));
        a.MapGet("/data/records", (long? org, string collection, string? q, int? offset, SyncStore sync, Accounts accounts) =>
        {
            var (rows, total) = sync.Records(org ?? accounts.DefaultOrgId(), collection, q, offset ?? 0, 50);
            return new { rows, total };
        });
        a.MapGet("/data/record", (long? org, string collection, string id, SyncStore sync, Accounts accounts) =>
            sync.Record(org ?? accounts.DefaultOrgId(), collection, id) ?? throw new ApiProblem(404, "not_found", "No such record."));
        a.MapGet("/data/export", async (long? org, SyncStore sync, Accounts accounts, ActivityLog log, HttpContext http) =>
        {
            long id = org ?? accounts.DefaultOrgId();
            log.Add("admin", null, $"{Admin(http)} exported all records", id);
            http.Response.ContentType = "application/x-ndjson";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"guardrail-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl\"";
            await sync.ExportJson(id, http.Response.Body, http.RequestAborted);
        });

        // ----- Settings, logs, backup -----

        a.MapGet("/settings", (SettingsStore s) => SettingsView(s));
        a.MapPut("/settings", (SettingsRequest r, SettingsStore s, ActivityLog log, HttpContext http) =>
        {
            if (r.ServerName != null)
            {
                string name = r.ServerName.Trim();
                if (name.Length is < 1 or > 60) throw new ApiProblem(400, "bad_request", "The server name can be 1 to 60 characters.");
                s.ServerName = name;
            }
            if (r.OpenSignup != null) s.OpenSignup = r.OpenSignup.Value;
            if (r.MaxUsers != null) s.MaxUsers = Math.Max(0, r.MaxUsers.Value);
            if (r.SignupRole != null)
            {
                if (!Protocol.Roles.IsValid(r.SignupRole)) throw new ApiProblem(400, "bad_role", "Roles are admin, manager, member or viewer.");
                s.SignupRole = r.SignupRole;
            }
            log.Add("settings", null, $"Settings changed by {Admin(http)}");
            return SettingsView(s);
        });

        a.MapGet("/logs", (LogBuffer logs) => logs.Lines.Reverse().Take(1000));
        a.MapGet("/activity", (ActivityLog log) => log.Recent(300));

        a.MapGet("/backup", (Db db, ActivityLog log, HttpContext http) =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "guardrail-backups");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder)) try { File.Delete(old); } catch { }
            string file = db.BackupTo(folder);
            log.Add("admin", null, $"{Admin(http)} downloaded a database backup");
            return Results.File(file, "application/octet-stream", Path.GetFileName(file));
        });
    }

    private static object SettingsView(SettingsStore s) => new { serverName = s.ServerName, openSignup = s.OpenSignup, maxUsers = s.MaxUsers, signupRole = s.SignupRole };

    private static IResult UserAction(long id, Accounts accounts, ActivityLog log, HttpContext http, string what, Action action)
    {
        string name = accounts.NameOf(id) ?? throw new ApiProblem(404, "not_found", "No such user.");
        action();
        log.Add("admin", name, $"{name} {what} by {Admin(http)}", accounts.OrgOf(id));
        return Results.Ok(new { ok = true });
    }

    public static string Admin(HttpContext http) => http.Items["admin"] as string ?? throw new ApiProblem(401, "unauthorized", "Sign in to the admin panel.");

    private static void SetCookie(HttpContext http, string token) =>
        http.Response.Cookies.Append(AdminAuth.Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(7),
            Path = "/",
        });
}

/// <summary>Restarts or shuts down this server process in the way that fits how it was started.</summary>
public sealed class ServerControl(ServerConfig config, IHostApplicationLifetime lifetime, ServiceState state, ILogger<ServerControl> log)
{
    /// <summary>Exit code that service managers treat as a failure, so they start the server again (restart).</summary>
    public const int RestartExitCode = 75;

    /// <summary>Set when a hand-started server should start a fresh copy of itself after shutting down (Program.cs).</summary>
    public static bool RelaunchAfterExit { get; private set; }
    public static int ExitCode { get; private set; }

    /// <summary>After a shut down, does something start it again on its own? (Kubernetes always does.)</summary>
    public bool ComesBackByItself => config.InKubernetes;

    public void RestartSoon() => _ = Task.Run(async () =>
    {
        state.Stopping = true;
        await Task.Delay(800); // let the response go out
        log.LogWarning("Restarting ({How})", config.HowItRuns);
        if (config.InContainer || config.AsService)
        {
            // Docker's restart policy, systemd's Restart=on-failure and the Windows service's recovery settings bring it back.
            // Exiting without a clean stop is what makes them treat it as a failure and restart it.
            Environment.Exit(RestartExitCode);
        }
        else
        {
            RelaunchAfterExit = true;
            lifetime.StopApplication();
        }
    });

    /// <summary>Stops this server. A service or container stays stopped (exit code 0) until it's started again.</summary>
    public void ShutdownSoon() => _ = Task.Run(async () =>
    {
        state.Stopping = true;
        await Task.Delay(800);
        log.LogWarning("Shutting down from the web panel ({How})", config.HowItRuns);
        ExitCode = 0;
        lifetime.StopApplication();
    });

    /// <summary>Called once the old copy has stopped listening.</summary>
    public static void Relaunch()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Insert(0, typeof(ServerControl).Assembly.Location);
        Process.Start(start);
    }
}
