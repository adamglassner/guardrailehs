using Guardrail.Sync;

namespace Guardrail.Server;

/// <summary>The API the Guardrail apps use: /api/v1/… (see server/PROTOCOL.md).</summary>
public static class ClientApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/info", (SettingsStore s, ServiceState state) =>
            new ServerInfo("Guardrail Server", s.ServerName, ServiceState.Version, Protocol.Version, s.OpenSignup, state.Running));

        // ----- Accounts -----

        api.MapPost("/auth/register", (RegisterRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http);
            if (t.IsLocked("signup:" + ip)) throw TooMany();
            try { return a.Register(r, ip); }
            catch (ApiProblem p) when (p.Error is "bad_invite" or "invite_required") { t.Fail("signup:" + ip); throw; }
        });

        api.MapPost("/auth/login", (LoginRequest r, Accounts a, LoginThrottle t, HttpContext http) =>
        {
            string ip = Ip(http), user = "user:" + (r.Username ?? "").Trim().ToLowerInvariant();
            if (t.IsLocked(user, "ip:" + ip)) throw TooMany();
            try
            {
                var session = a.Login(r, ip);
                t.Succeed(user);
                return session;
            }
            catch (ApiProblem p) when (p.Status == 401) { t.Fail(user, "ip:" + ip); throw; }
        });

        api.MapPost("/auth/password", (ChangePasswordRequest r, Accounts a, HttpContext http) =>
        {
            a.ChangePassword(User(http), r);
            return Results.Ok(new { ok = true });
        });

        api.MapPost("/auth/logout", (Accounts a, HttpContext http) =>
        {
            a.Logout(User(http));
            return Results.Ok(new { ok = true });
        });

        api.MapGet("/account", (Accounts a, HttpContext http) => a.Account(User(http)));
        api.MapGet("/directory", (Accounts a, HttpContext http) => a.Directory(User(http)));

        api.MapDelete("/account/devices/{id}", (string id, Accounts a, HttpContext http) =>
            a.SignOutDevice(User(http), id) ? Results.Ok(new { ok = true }) : throw new ApiProblem(404, "not_found", "No such device."));

        // ----- Sync -----

        api.MapGet("/sync/changes", (long? since, SyncStore sync, ServiceState state, HttpContext http) =>
        {
            var u = User(http);
            state.CountSync(u.Key);
            return sync.Changes(u.OrgId, since ?? 0);
        });

        api.MapPost("/sync/push", (PushRequest r, SyncStore sync, ServiceState state, HttpContext http) =>
        {
            var u = User(http);
            state.CountSync(u.Key);
            return sync.Push(u, r);
        });

        // ----- Files -----

        api.MapPut("/files/{**name}", async (string name, SyncStore sync, BlobStore blobs, HttpContext http) =>
        {
            var u = User(http);
            if (!Protocol.Roles.CanWrite(u.Role)) throw new ApiProblem(403, "forbidden", "Your account is read-only.");
            name = Uri.UnescapeDataString(name);
            if (!Protocol.IsValidId(name)) throw new ApiProblem(400, "bad_request", "Invalid file name.");

            // Spool to a temp file first: checks the size limit and gives object storage a known length.
            string temp = Path.Combine(Path.GetTempPath(), "guardrail-upload-" + Guid.NewGuid().ToString("N"));
            try
            {
                long size = 0;
                await using (var file = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
                    {
                        size += n;
                        if (size > Protocol.MaxFileBytes)
                            throw new ApiProblem(413, "too_large", $"Files can be up to {Protocol.MaxFileBytes / 1024 / 1024} MB.");
                        await file.WriteAsync(buffer.AsMemory(0, n), http.RequestAborted);
                    }
                }
                await blobs.Put($"{u.OrgId}/{name}", temp, http.RequestAborted);
                sync.FileSaved(u.OrgId, name, size, u.Username);
                return Results.Ok(new { ok = true, size });
            }
            finally { try { File.Delete(temp); } catch { } }
        });

        api.MapMethods("/files/{**name}", ["GET", "HEAD"], async (string name, BlobStore blobs, HttpContext http) =>
        {
            var u = User(http);
            name = Uri.UnescapeDataString(name);
            if (!Protocol.IsValidId(name)) throw new ApiProblem(400, "bad_request", "Invalid file name.");
            var stream = await blobs.Get($"{u.OrgId}/{name}", http.RequestAborted) ?? throw new ApiProblem(404, "not_found", "No such file.");
            return Results.Stream(stream, "application/octet-stream", enableRangeProcessing: stream.CanSeek);
        });

        api.MapDelete("/files/{**name}", async (string name, SyncStore sync, BlobStore blobs, HttpContext http) =>
        {
            var u = User(http);
            if (!Protocol.Roles.CanWrite(u.Role)) throw new ApiProblem(403, "forbidden", "Your account is read-only.");
            name = Uri.UnescapeDataString(name);
            if (!Protocol.IsValidId(name)) throw new ApiProblem(400, "bad_request", "Invalid file name.");
            await blobs.Delete($"{u.OrgId}/{name}");
            return sync.FileDeleted(u.OrgId, name) ? Results.Ok(new { ok = true }) : throw new ApiProblem(404, "not_found", "No such file.");
        });
    }

    /// <summary>The signed-in user (set by the auth middleware), or a 401.</summary>
    public static SessionUser User(HttpContext http) =>
        http.Items["user"] as SessionUser ?? throw new ApiProblem(401, "unauthorized", "You're signed out of Guardrail Server. Sign in again to keep syncing.");

    public static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ApiProblem TooMany() => new(429, "too_many_attempts", "Too many tries. Wait 15 minutes and try again.");
}
