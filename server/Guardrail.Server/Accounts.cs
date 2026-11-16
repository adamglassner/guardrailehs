using System.Collections.Concurrent;
using System.Data.Common;
using Guardrail.Sync;

namespace Guardrail.Server;

/// <summary>Who's making a request (set by the bearer-token middleware).</summary>
public sealed record SessionUser(long UserId, long OrgId, string Username, string DisplayName, string? Email, string Role, string SessionId, string OrgName)
{
    public UserInfo Info => new(UserId, Username, DisplayName, Email, Role);
    public OrgInfo Org => new(OrgId, OrgName);
    public string Key => $"{OrgId}:{Username}";
}

public sealed record UserRow(long Id, long OrgId, string Org, string Username, string DisplayName, string? Email, string Role, bool Disabled,
    DateTime CreatedAt, DateTime? LastSeenAt, int Devices, long RecordsUpdated);
public sealed record AdminDevice(string Id, long UserId, string Username, string Org, string Name, string Platform, string AppVersion,
    DateTime CreatedAt, DateTime LastSeen, string? Ip);
public sealed record Invite(string Code, long? OrgId, string? Org, string? NewOrgName, string Role, string? Username, string? Note,
    int UsesLeft, DateTime CreatedAt, DateTime? ExpiresAt, string? UsedBy);
public sealed record OrgRow(long Id, string Name, DateTime CreatedAt, int Users, long Records, long Files, long FileBytes);

/// <summary>Organizations, the people in them, their signed-in devices, and invites.</summary>
public sealed class Accounts(Db db, SettingsStore settings, ActivityLog activity, ServerConfig config)
{
    /// <summary>Signed-in devices, cached for 30 seconds per server so most requests don't touch the sessions table.</summary>
    private readonly ConcurrentDictionary<string, (SessionUser User, long At, long SeenWritten)> _sessions = new();
    private const long SessionCacheMs = 30_000;

    // ----- Organizations -----

    /// <summary>The first organization (created on first use). A single-company server only ever has this one.</summary>
    public long DefaultOrgId()
    {
        using var c = db.Open();
        long id = Db.Long(Db.Scalar(c, "SELECT MIN(id) FROM orgs"));
        if (id != 0) return id;
        return CreateOrg(c, null, config.OrgName is { Length: > 0 } n ? n : "My organization");
    }

    private static long CreateOrg(DbConnection c, DbTransaction? tx, string name) =>
        Db.Long(Db.Scalar(c, "INSERT INTO orgs(name, seq, created_at) VALUES(@name, 0, @now) RETURNING id", new { name = name.Trim(), now = Db.Now() }, tx));

    public List<OrgRow> Orgs()
    {
        using var c = db.Open();
        return Db.Query(c, """
            SELECT o.id, o.name, o.created_at,
                (SELECT COUNT(*) FROM users u WHERE u.org_id = o.id),
                (SELECT COUNT(*) FROM records r WHERE r.org_id = o.id AND r.deleted = 0),
                (SELECT COUNT(*) FROM files f WHERE f.org_id = o.id),
                (SELECT COALESCE(SUM(f.size), 0) FROM files f WHERE f.org_id = o.id)
            FROM orgs o ORDER BY o.id
            """, r => new OrgRow(Db.Long(r, 0), r.GetString(1), Db.Time(Db.Long(r, 2)), (int)Db.Long(r, 3), Db.Long(r, 4), Db.Long(r, 5), Db.Long(r, 6)));
    }

    public void RenameOrg(long id, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80) throw new ApiProblem(400, "bad_request", "Organization names are 1 to 80 characters.");
        using var c = db.Open();
        if (Db.Exec(c, "UPDATE orgs SET name = @name WHERE id = @id", new { name, id }) == 0) throw new ApiProblem(404, "not_found", "No such organization.");
        _sessions.Clear();
    }

    public void DeleteOrg(long id, BlobStore blobs)
    {
        using var c = db.Open();
        if (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM orgs")) <= 1) throw new ApiProblem(400, "last_org", "This is the only organization. Erase its data instead.");
        blobs.DeletePrefix($"{id}/").GetAwaiter().GetResult();
        Db.Exec(c, "DELETE FROM orgs WHERE id = @id", new { id });
        _sessions.Clear();
    }

    // ----- Signing up and in -----

    public SessionResponse Register(RegisterRequest r, string ip)
    {
        string username = Protocol.NormalizeUsername(r.Username) ?? throw new ApiProblem(400, "bad_username", Protocol.UsernameRules);
        CheckPassword(r.Password);
        string display = string.IsNullOrWhiteSpace(r.DisplayName) ? username : r.DisplayName.Trim();
        if (display.Length > 80) throw new ApiProblem(400, "bad_request", "Names can be up to 80 characters.");
        string? email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim();

        using var c = db.Open();
        using var tx = db.Begin(c);
        if (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM users WHERE username_key = @username", new { username }, tx)) > 0)
            throw new ApiProblem(409, "username_taken", "That username is taken. Pick another, or sign in if it's yours.");
        int max = settings.MaxUsers;
        if (max > 0 && Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM users", tx: tx)) >= max)
            throw new ApiProblem(403, "server_full", "This server has reached its account limit. Ask its admin.");

        long orgId;
        string role;
        string? code = string.IsNullOrWhiteSpace(r.Invite) ? null : r.Invite.Trim().ToUpperInvariant();
        if (code != null)
        {
            var inv = Db.Query(c, "SELECT org_id, new_org_name, role, username, uses_left, expires_at FROM invites WHERE code = @code",
                x => (OrgId: x.IsDBNull(0) ? (long?)null : Db.Long(x, 0), NewOrg: Db.Str(x, 1), Role: x.GetString(2), Username: Db.Str(x, 3), Uses: Db.Long(x, 4), Expires: x.IsDBNull(5) ? (long?)null : Db.Long(x, 5)),
                new { code }, tx).FirstOrDefault();
            if (inv == default || inv.Uses <= 0 || (inv.Expires is long e && e < Db.Now()))
                throw new ApiProblem(403, "bad_invite", "That invite code isn't valid or has been used up. Ask your admin for a new one.");
            if (inv.Username != null && inv.Username != username)
                throw new ApiProblem(403, "bad_invite", $"That invite is for the username \"{inv.Username}\".");
            orgId = inv.OrgId ?? CreateOrg(c, tx, inv.NewOrg ?? display + "'s organization");
            role = inv.Role;
            Db.Exec(c, "UPDATE invites SET uses_left = uses_left - 1, used_by = CASE WHEN used_by IS NULL THEN @username ELSE used_by || ', ' || @username END WHERE code = @code",
                new { username, code }, tx);
        }
        else
        {
            bool firstEver = Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM users", tx: tx)) == 0;
            if (!settings.OpenSignup) throw new ApiProblem(403, "invite_required", "This server needs an invite code to sign up. Ask its admin for one.");
            orgId = Db.Long(Db.Scalar(c, "SELECT MIN(id) FROM orgs", tx: tx));
            if (orgId == 0) orgId = CreateOrg(c, tx, config.OrgName is { Length: > 0 } n ? n : "My organization");
            role = firstEver ? Protocol.Roles.Admin : settings.SignupRole;
        }

        string salt = Secrets.NewSalt();
        long userId = Db.Long(Db.Scalar(c, """
            INSERT INTO users(org_id, username, username_key, display_name, email, role, pw_hash, pw_salt, pw_iterations, disabled, created_at)
            VALUES(@orgId, @username, @username, @display, @email, @role, @hash, @salt, @it, 0, @now) RETURNING id
            """, new { orgId, username, display, email, role, hash = Secrets.HashPassword(r.Password, salt, Secrets.Iterations), salt, it = Secrets.Iterations, now = Db.Now() }, tx));
        var session = NewSession(c, tx, userId, r.DeviceName, r.Platform, r.AppVersion, ip);
        tx.Commit();
        activity.Add("signup", username, $"{username} created an account" + (code != null ? $" with invite {code}" : "") + $" ({Protocol.Roles.Label(role)})", orgId);
        return Session(userId, session.Token, session.Id);
    }

    public SessionResponse Login(LoginRequest r, string ip)
    {
        string username = Protocol.NormalizeUsername(r.Username) ?? "";
        using var c = db.Open();
        var found = Db.Query(c, "SELECT id, pw_hash, pw_salt, pw_iterations, disabled, org_id FROM users WHERE username_key = @username",
            x => (Id: Db.Long(x, 0), Hash: x.GetString(1), Salt: x.GetString(2), It: (int)Db.Long(x, 3), Disabled: Db.Flag(x, 4), Org: Db.Long(x, 5)), new { username }).FirstOrDefault();
        if (found == default)
        {
            Secrets.HashPassword(r.Password ?? "", Secrets.NewSalt(), Secrets.Iterations); // same timing either way
            throw new ApiProblem(401, "bad_login", "Wrong username or password.");
        }
        if (!Secrets.CheckPassword(r.Password ?? "", found.Hash, found.Salt, found.It))
        {
            activity.Add("signin", username, $"Failed sign-in for {username} from {ip}", found.Org);
            throw new ApiProblem(401, "bad_login", "Wrong username or password.");
        }
        if (found.Disabled) throw new ApiProblem(403, "disabled", "This account is turned off. Ask your Guardrail Server admin.");
        var session = NewSession(c, null, found.Id, r.DeviceName, r.Platform, r.AppVersion, ip);
        activity.Add("signin", username, $"{username} signed in on {Clip(r.DeviceName, "a device")}", found.Org);
        return Session(found.Id, session.Token, session.Id);
    }

    private (string Token, string Id) NewSession(DbConnection c, DbTransaction? tx, long userId, string? device, string? platform, string? appVersion, string ip)
    {
        string token = Secrets.NewToken(), id = Guid.NewGuid().ToString("N")[..12];
        long now = Db.Now();
        Db.Exec(c, """
            INSERT INTO sessions(id, token_hash, user_id, device_name, platform, app_version, ip, created_at, last_seen_at)
            VALUES(@id, @hash, @userId, @device, @platform, @appVersion, @ip, @now, @now)
            """, new { id, hash = Secrets.TokenHash(token), userId, device = Clip(device, "Device"), platform = Clip(platform, "Unknown"), appVersion = Clip(appVersion, ""), ip, now }, tx);
        Db.Exec(c, "UPDATE users SET last_seen_at = @now WHERE id = @userId", new { now, userId }, tx);
        return (token, id);
    }

    private static string Clip(string? s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim().Length > 80 ? s.Trim()[..80] : s.Trim();

    private SessionResponse Session(long userId, string token, string sessionId)
    {
        var u = LoadUser(userId) ?? throw new ApiProblem(500, "server_error", "The account vanished.");
        return new SessionResponse(token, sessionId, u.Info, u.Org);
    }

    private SessionUser? LoadUser(long userId, string sessionId = "")
    {
        using var c = db.Open();
        return Db.Query(c, """
            SELECT u.id, u.org_id, u.username, u.display_name, u.email, u.role, o.name FROM users u JOIN orgs o ON o.id = u.org_id WHERE u.id = @userId
            """, r => new SessionUser(Db.Long(r, 0), Db.Long(r, 1), r.GetString(2), r.GetString(3), Db.Str(r, 4), r.GetString(5), sessionId, r.GetString(6)),
            new { userId }).FirstOrDefault();
    }

    /// <summary>The signed-in user for a bearer token, or null.</summary>
    public SessionUser? Authenticate(string token, string ip)
    {
        string hash = Secrets.TokenHash(token);
        long now = Db.Now();
        if (_sessions.TryGetValue(hash, out var hit) && now - hit.At < SessionCacheMs)
        {
            if (now - hit.SeenWritten > 60_000) TouchSession(hash, hit, now, ip);
            return hit.User;
        }
        using var c = db.Open();
        var user = Db.Query(c, """
            SELECT u.id, u.org_id, u.username, u.display_name, u.email, u.role, s.id, o.name, u.disabled
            FROM sessions s JOIN users u ON u.id = s.user_id JOIN orgs o ON o.id = u.org_id WHERE s.token_hash = @hash
            """, r => (User: new SessionUser(Db.Long(r, 0), Db.Long(r, 1), r.GetString(2), r.GetString(3), Db.Str(r, 4), r.GetString(5), r.GetString(6), r.GetString(7)), Disabled: Db.Flag(r, 8)),
            new { hash }).FirstOrDefault();
        if (user == default || user.Disabled) { _sessions.TryRemove(hash, out _); return null; }
        var entry = (user.User, now, 0L);
        TouchSession(hash, entry, now, ip);
        return user.User;
    }

    private void TouchSession(string hash, (SessionUser User, long At, long SeenWritten) entry, long now, string ip)
    {
        _sessions[hash] = (entry.User, entry.At == 0 ? now : entry.At, now);
        try
        {
            using var c = db.Open();
            Db.Exec(c, "UPDATE sessions SET last_seen_at = @now, ip = @ip WHERE token_hash = @hash", new { now, ip, hash });
            Db.Exec(c, "UPDATE users SET last_seen_at = @now WHERE id = @id", new { now, id = entry.User.UserId });
        }
        catch { /* best effort */ }
    }

    private void Forget(Func<SessionUser, bool> which)
    {
        foreach (var kv in _sessions.Where(kv => which(kv.Value.User)).ToList()) _sessions.TryRemove(kv.Key, out _);
    }

    public void Logout(SessionUser u)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE id = @id", new { id = u.SessionId });
        Forget(x => x.SessionId == u.SessionId);
    }

    public void ChangePassword(SessionUser u, ChangePasswordRequest r)
    {
        using var c = db.Open();
        var row = Db.Query(c, "SELECT pw_hash, pw_salt, pw_iterations FROM users WHERE id = @id", x => (x.GetString(0), x.GetString(1), (int)Db.Long(x, 2)), new { id = u.UserId }).First();
        if (!Secrets.CheckPassword(r.CurrentPassword ?? "", row.Item1, row.Item2, row.Item3))
            throw new ApiProblem(403, "bad_password", "Your current password isn't right.");
        SetPassword(c, u.UserId, r.NewPassword);
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = @id AND id <> @sid", new { id = u.UserId, sid = u.SessionId });
        Forget(x => x.UserId == u.UserId && x.SessionId != u.SessionId);
        activity.Add("password", u.Username, $"{u.Username} changed their password (other devices signed out)", u.OrgId);
    }

    private static void CheckPassword(string? password)
    {
        if ((password ?? "").Length < Protocol.MinPasswordLength)
            throw new ApiProblem(400, "weak_password", $"Use at least {Protocol.MinPasswordLength} characters for your password.");
    }

    private static void SetPassword(DbConnection c, long userId, string password)
    {
        CheckPassword(password);
        string salt = Secrets.NewSalt();
        Db.Exec(c, "UPDATE users SET pw_hash = @hash, pw_salt = @salt, pw_iterations = @it WHERE id = @id",
            new { hash = Secrets.HashPassword(password, salt, Secrets.Iterations), salt, it = Secrets.Iterations, id = userId });
    }

    public AccountResponse Account(SessionUser u) => new(u.Info, u.Org, Devices(u.UserId, u.SessionId));

    private List<DeviceInfo> Devices(long userId, string currentSession = "")
    {
        using var c = db.Open();
        return Db.Query(c, "SELECT id, device_name, platform, app_version, created_at, last_seen_at, ip FROM sessions WHERE user_id = @userId ORDER BY last_seen_at DESC",
            r => new DeviceInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Db.Time(Db.Long(r, 4)), Db.Time(Db.Long(r, 5)), Db.Str(r, 6), r.GetString(0) == currentSession),
            new { userId });
    }

    public bool SignOutDevice(SessionUser u, string id)
    {
        using var c = db.Open();
        bool done = Db.Exec(c, "DELETE FROM sessions WHERE id = @id AND user_id = @userId", new { id, userId = u.UserId }) > 0;
        Forget(x => x.SessionId == id);
        return done;
    }

    public List<DirectoryUser> Directory(SessionUser u)
    {
        using var c = db.Open();
        return Db.Query(c, "SELECT username, display_name, role FROM users WHERE org_id = @org AND disabled = 0 ORDER BY display_name",
            r => new DirectoryUser(r.GetString(0), r.GetString(1), r.GetString(2)), new { org = u.OrgId });
    }

    // ----- Web panel -----

    public List<UserRow> List(long? orgId = null)
    {
        using var c = db.Open();
        string where = orgId != null ? "WHERE u.org_id = @orgId" : "";
        return Db.Query(c, $"""
            SELECT u.id, u.org_id, o.name, u.username, u.display_name, u.email, u.role, u.disabled, u.created_at, u.last_seen_at,
                (SELECT COUNT(*) FROM sessions s WHERE s.user_id = u.id),
                (SELECT COUNT(*) FROM records r WHERE r.org_id = u.org_id AND r.updated_by = u.username)
            FROM users u JOIN orgs o ON o.id = u.org_id {where} ORDER BY u.display_name
            """, r => new UserRow(Db.Long(r, 0), Db.Long(r, 1), r.GetString(2), r.GetString(3), r.GetString(4), Db.Str(r, 5), r.GetString(6), Db.Flag(r, 7),
                Db.Time(Db.Long(r, 8)), Db.Time(r, 9), (int)Db.Long(r, 10), Db.Long(r, 11)),
            orgId != null ? new { orgId = orgId.Value } : null);
    }

    public UserRow Get(long id) => List().FirstOrDefault(u => u.Id == id) ?? throw new ApiProblem(404, "not_found", "No such user.");

    public List<AdminDevice> AllDevices(long? userId = null)
    {
        using var c = db.Open();
        string where = userId != null ? "WHERE s.user_id = @userId" : "";
        return Db.Query(c, $"""
            SELECT s.id, u.id, u.username, o.name, s.device_name, s.platform, s.app_version, s.created_at, s.last_seen_at, s.ip
            FROM sessions s JOIN users u ON u.id = s.user_id JOIN orgs o ON o.id = u.org_id {where} ORDER BY s.last_seen_at DESC
            """, r => new AdminDevice(r.GetString(0), Db.Long(r, 1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                Db.Time(Db.Long(r, 7)), Db.Time(Db.Long(r, 8)), Db.Str(r, 9)),
            userId != null ? new { userId = userId.Value } : null);
    }

    public void SetDisabled(long id, bool disabled)
    {
        using var c = db.Open();
        Db.Exec(c, "UPDATE users SET disabled = @disabled WHERE id = @id", new { disabled, id });
        if (disabled) Db.Exec(c, "DELETE FROM sessions WHERE user_id = @id", new { id });
        Forget(x => x.UserId == id);
    }

    public void SetRole(long id, string role)
    {
        if (!Protocol.Roles.IsValid(role)) throw new ApiProblem(400, "bad_role", "Roles are admin, manager, member or viewer.");
        using var c = db.Open();
        Db.Exec(c, "UPDATE users SET role = @role WHERE id = @id", new { role, id });
        Forget(x => x.UserId == id);
    }

    public void SetProfile(long id, string? displayName, string? email)
    {
        using var c = db.Open();
        if (displayName != null)
        {
            string d = displayName.Trim();
            if (d.Length is < 1 or > 80) throw new ApiProblem(400, "bad_request", "Names are 1 to 80 characters.");
            Db.Exec(c, "UPDATE users SET display_name = @d WHERE id = @id", new { d, id });
        }
        if (email != null)
        {
            string e = email.Trim();
            if (e.Length == 0) Db.Exec(c, "UPDATE users SET email = NULL WHERE id = @id", new { id });
            else Db.Exec(c, "UPDATE users SET email = @e WHERE id = @id", new { e, id });
        }
        Forget(x => x.UserId == id);
    }

    /// <summary>Sets a new random password and signs the person out everywhere. Returns the password to give them.</summary>
    public string ResetPassword(long id)
    {
        string password = Secrets.NewTemporaryPassword();
        using var c = db.Open();
        SetPassword(c, id, password);
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = @id", new { id });
        Forget(x => x.UserId == id);
        return password;
    }

    /// <summary>Creates an account from the web panel.</summary>
    public (UserRow User, string Password) CreateUser(long orgId, string username, string? displayName, string? email, string role)
    {
        string name = Protocol.NormalizeUsername(username) ?? throw new ApiProblem(400, "bad_username", Protocol.UsernameRules);
        if (!Protocol.Roles.IsValid(role)) throw new ApiProblem(400, "bad_role", "Roles are admin, manager, member or viewer.");
        string password = Secrets.NewTemporaryPassword(), salt = Secrets.NewSalt();
        using var c = db.Open();
        if (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM orgs WHERE id = @orgId", new { orgId })) == 0) throw new ApiProblem(404, "not_found", "No such organization.");
        if (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM users WHERE username_key = @name", new { name })) > 0)
            throw new ApiProblem(409, "username_taken", "That username is taken.");
        long id = Db.Long(Db.Scalar(c, """
            INSERT INTO users(org_id, username, username_key, display_name, email, role, pw_hash, pw_salt, pw_iterations, disabled, created_at)
            VALUES(@orgId, @name, @name, @display, @email, @role, @hash, @salt, @it, 0, @now) RETURNING id
            """, new { orgId, name, display = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(), email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                role, hash = Secrets.HashPassword(password, salt, Secrets.Iterations), salt, it = Secrets.Iterations, now = Db.Now() }));
        return (Get(id), password);
    }

    public void SignOutEverywhere(long id)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE user_id = @id", new { id });
        Forget(x => x.UserId == id);
    }

    public void RevokeDevice(string sessionId)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM sessions WHERE id = @sessionId", new { sessionId });
        Forget(x => x.SessionId == sessionId);
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM users WHERE id = @id", new { id });
        Forget(x => x.UserId == id);
    }

    // ----- Invites -----

    public List<Invite> Invites()
    {
        using var c = db.Open();
        return Db.Query(c, """
            SELECT i.code, i.org_id, o.name, i.new_org_name, i.role, i.username, i.note, i.uses_left, i.created_at, i.expires_at, i.used_by
            FROM invites i LEFT JOIN orgs o ON o.id = i.org_id ORDER BY i.created_at DESC
            """, r => new Invite(r.GetString(0), r.IsDBNull(1) ? null : Db.Long(r, 1), Db.Str(r, 2), Db.Str(r, 3), r.GetString(4), Db.Str(r, 5), Db.Str(r, 6),
                (int)Db.Long(r, 7), Db.Time(Db.Long(r, 8)), Db.Time(r, 9), Db.Str(r, 10)));
    }

    public Invite CreateInvite(long? orgId, string? newOrgName, string role, string? username, string? note, int uses, int? days)
    {
        if (!Protocol.Roles.IsValid(role)) throw new ApiProblem(400, "bad_role", "Roles are admin, manager, member or viewer.");
        string? name = string.IsNullOrWhiteSpace(username) ? null : Protocol.NormalizeUsername(username) ?? throw new ApiProblem(400, "bad_username", Protocol.UsernameRules);
        if (uses is < 1 or > 10_000) throw new ApiProblem(400, "bad_request", "Uses can be 1 to 10,000.");
        string? newOrg = string.IsNullOrWhiteSpace(newOrgName) ? null : newOrgName.Trim();
        if (newOrg == null) orgId ??= DefaultOrgId();
        else orgId = null;
        string code = Secrets.NewInviteCode();
        long now = Db.Now();
        using var c = db.Open();
        Db.Exec(c, """
            INSERT INTO invites(code, org_id, new_org_name, role, username, note, uses_left, created_at, expires_at)
            VALUES(@code, @orgId, @newOrg, @role, @name, @note, @uses, @now, @expires)
            """, new { code, orgId, newOrg, role, name, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), uses, now,
                expires = days is int d && d > 0 ? now + d * 86_400_000L : (long?)null });
        return Invites().First(i => i.Code == code);
    }

    public void DeleteInvite(string code)
    {
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM invites WHERE code = @code", new { code });
    }

    public string? NameOf(long id)
    {
        using var c = db.Open();
        return Db.Scalar(c, "SELECT username FROM users WHERE id = @id", new { id }) as string;
    }

    public long OrgOf(long userId)
    {
        using var c = db.Open();
        return Db.Long(Db.Scalar(c, "SELECT org_id FROM users WHERE id = @userId", new { userId }));
    }
}

/// <summary>Accounts for the web panel (separate from the people who use the apps). Sessions are an HttpOnly cookie.</summary>
public sealed class AdminAuth(Db db)
{
    public const string Cookie = "gr_admin";

    public bool NeedsSetup()
    {
        using var c = db.Open();
        return Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM admins")) == 0;
    }

    public void Create(string username, string password)
    {
        if (Protocol.NormalizeUsername(username) is not { } name) throw new ApiProblem(400, "bad_username", "Admin names are 3 to 64 characters: letters, numbers and . _ - + @");
        if ((password ?? "").Length < 10) throw new ApiProblem(400, "weak_password", "Use at least 10 characters for the admin password.");
        string salt = Secrets.NewSalt();
        using var c = db.Open();
        Db.Exec(c, """
            INSERT INTO admins(username, hash, salt, iterations, created_at) VALUES(@name, @hash, @salt, @it, @now)
            ON CONFLICT(username) DO UPDATE SET hash = excluded.hash, salt = excluded.salt, iterations = excluded.iterations
            """, new { name, hash = Secrets.HashPassword(password!, salt, Secrets.Iterations), salt, it = Secrets.Iterations, now = Db.Now() });
    }

    public bool Check(string username, string password)
    {
        using var c = db.Open();
        var found = Db.Query(c, "SELECT hash, salt, iterations FROM admins WHERE username = @username",
            r => (Hash: r.GetString(0), Salt: r.GetString(1), It: (int)Db.Long(r, 2)), new { username = (username ?? "").Trim().ToLowerInvariant() });
        if (found.Count == 0)
        {
            Secrets.HashPassword(password ?? "", Secrets.NewSalt(), Secrets.Iterations);
            return false;
        }
        return Secrets.CheckPassword(password ?? "", found[0].Hash, found[0].Salt, found[0].It);
    }

    public List<string> List()
    {
        using var c = db.Open();
        return Db.Query(c, "SELECT username FROM admins ORDER BY username", r => r.GetString(0));
    }

    public void Delete(string username)
    {
        using var c = db.Open();
        if (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM admins")) <= 1) throw new ApiProblem(400, "last_admin", "You can't remove the last admin.");
        Db.Exec(c, "DELETE FROM admins WHERE username = @username", new { username });
        Db.Exec(c, "DELETE FROM admin_sessions WHERE username = @username", new { username });
    }

    public string NewSession(string username)
    {
        string token = Secrets.NewToken();
        using var c = db.Open();
        Db.Exec(c, "INSERT INTO admin_sessions(token_hash, username, expires_at) VALUES(@hash, @username, @exp)",
            new { hash = Secrets.TokenHash(token), username = username.Trim().ToLowerInvariant(), exp = Db.Now() + 7 * 86_400_000L });
        return token;
    }

    public string? SessionUser(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        using var c = db.Open();
        return Db.Scalar(c, "SELECT username FROM admin_sessions WHERE token_hash = @hash AND expires_at > @now",
            new { hash = Secrets.TokenHash(token), now = Db.Now() }) as string;
    }

    public void EndSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        using var c = db.Open();
        Db.Exec(c, "DELETE FROM admin_sessions WHERE token_hash = @hash", new { hash = Secrets.TokenHash(token) });
    }
}
