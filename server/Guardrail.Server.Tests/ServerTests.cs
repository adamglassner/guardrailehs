using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Guardrail.Sync;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Guardrail.Server.Tests;

/// <summary>
/// A fresh server with its own data folder and a bootstrap admin. Uses SQLite, or PostgreSQL when
/// GR_TEST_POSTGRES holds a connection string (CI runs the tests both ways); each server gets its own database.
/// </summary>
public sealed class TestServer : IDisposable
{
    public readonly WebApplicationFactory<Program> Factory;
    public readonly string Folder = Path.Combine(Path.GetTempPath(), "gr-test-" + Guid.NewGuid().ToString("N")[..8]);
    public const string AdminName = "admin", AdminPassword = "correct-horse-1";
    private readonly string? _pgAdmin, _pgDatabase;

    public static string? Postgres => Environment.GetEnvironmentVariable("GR_TEST_POSTGRES") is { Length: > 0 } s ? s : null;

    public TestServer(Action? before = null)
    {
        Environment.SetEnvironmentVariable("GR_DATA", Folder);
        Environment.SetEnvironmentVariable("GR_ADMIN_USERNAME", AdminName);
        Environment.SetEnvironmentVariable("GR_ADMIN_PASSWORD", AdminPassword);
        Environment.SetEnvironmentVariable("GR_RATE_LIMIT", "0");
        Environment.SetEnvironmentVariable("GR_DB", null);
        Environment.SetEnvironmentVariable("GR_DB_CONNECTION", null);
        if (Postgres is { } pg)
        {
            _pgAdmin = pg;
            _pgDatabase = "gr_test_" + Guid.NewGuid().ToString("N")[..10];
            using (var c = new NpgsqlConnection(pg))
            {
                c.Open();
                using var cmd = new NpgsqlCommand($"CREATE DATABASE {_pgDatabase}", c);
                cmd.ExecuteNonQuery();
            }
            Environment.SetEnvironmentVariable("GR_DB", "postgres");
            Environment.SetEnvironmentVariable("GR_DB_CONNECTION", new NpgsqlConnectionStringBuilder(pg) { Database = _pgDatabase }.ToString());
        }
        before?.Invoke();
        Factory = new WebApplicationFactory<Program>();
        Factory.CreateClient().Dispose(); // start it now, while the environment points at our folder
    }

    public SyncApiClient Api(string? token = null) => new("http://localhost/", Factory.Server.CreateHandler(), token);

    /// <summary>An HttpClient signed in to the web panel.</summary>
    public async Task<HttpClient> Admin()
    {
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        http.DefaultRequestHeaders.Add("X-Guardrail", "admin");
        var r = await http.PostAsJsonAsync("/admin/api/login", new { username = AdminName, password = AdminPassword });
        r.EnsureSuccessStatusCode();
        return http;
    }

    public async Task<string> Invite(string role = "member", string? username = null, int uses = 1, string? newOrg = null)
    {
        using var admin = await Admin();
        var r = await admin.PostAsJsonAsync("/admin/api/invites", new { role, username, uses, days = 7, newOrgName = newOrg });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    /// <summary>Creates an account with an invite and returns a signed-in client.</summary>
    public async Task<(SyncApiClient Api, SessionResponse Session)> Join(string username, string role = "member", string? newOrg = null)
    {
        var api = Api();
        var s = await api.RegisterAsync(new RegisterRequest(username, "password-123", username.ToUpperInvariant(), null, await Invite(role, null, 1, newOrg), "Test PC", "Windows", "1.0"));
        api.Token = s.Token;
        return (api, s);
    }

    public void Dispose()
    {
        Factory.Dispose();
        try { Directory.Delete(Folder, true); } catch { }
        if (_pgAdmin != null)
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                using var c = new NpgsqlConnection(_pgAdmin);
                c.Open();
                using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_pgDatabase} WITH (FORCE)", c);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}

public class ProtocolTests
{
    [Theory]
    [InlineData("Jane.Doe", "jane.doe")]
    [InlineData("  jane@acme.com ", "jane@acme.com")]
    [InlineData("ab", null)]
    [InlineData("has space", null)]
    public void Usernames(string input, string? expected) => Assert.Equal(expected, Protocol.NormalizeUsername(input));

    [Theory]
    [InlineData("guardrail.local", "http://guardrail.local:5080/")]
    [InlineData("192.168.1.20:5080", "http://192.168.1.20:5080/")]
    [InlineData("https://ehs.acme.com", "https://ehs.acme.com/")]
    [InlineData("http://server/guardrail", "http://server:5080/guardrail/")]
    public void Addresses(string input, string expected) => Assert.Equal(expected, SyncApiClient.NormalizeUrl(input));

    [Fact]
    public void Ids()
    {
        Assert.True(Protocol.IsValidId("library/3f2a/Policy.pdf"));
        Assert.False(Protocol.IsValidId("../etc/passwd"));
        Assert.True(Protocol.IsValidCollection("fleet-vehicles"));
        Assert.False(Protocol.IsValidCollection("Fleet Vehicles"));
        Assert.True(Protocol.IsValidFileName("library/3f2a/Safety Policy (2026).pdf"));
        Assert.False(Protocol.IsValidFileName("library/../x"));
        Assert.False(Protocol.IsValidFileName("/etc/passwd"));
    }
}

public class ServerTests
{
    private static string Record(string title, int seq = 1, Guid? id = null) =>
        JsonSerializer.Serialize(new { Id = id ?? Guid.NewGuid(), Seq = seq, Title = title, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public async Task InfoAndHealth()
    {
        using var server = new TestServer();
        using var api = server.Api();
        var info = await api.InfoAsync();
        Assert.Equal("Guardrail Server", info.Product);
        Assert.True(info.Running);
        Assert.Equal(Protocol.Version, info.Protocol);

        using var http = server.Factory.CreateClient();
        Assert.Equal("ok", await http.GetStringAsync("/healthz"));
        Assert.Equal("ready", await http.GetStringAsync("/readyz"));
        string metrics = await http.GetStringAsync("/metrics");
        Assert.Contains("guardrail_requests_total", metrics);
        Assert.Contains("<title>GuardHawk Server</title>", await http.GetStringAsync("/"));
    }

    [Fact]
    public async Task SignUpNeedsInviteThenWorks()
    {
        using var server = new TestServer();
        using var api = server.Api();
        var ex = await Assert.ThrowsAsync<SyncException>(() => api.RegisterAsync(new RegisterRequest("jane", "password-123")));
        Assert.Equal("invite_required", ex.Error);

        var (jane, s) = await server.Join("jane", "manager");
        Assert.Equal("manager", s.User.Role);
        Assert.Equal("My organization", s.Org.Name);
        var account = await jane.AccountAsync();
        Assert.Single(account.Devices);
        Assert.True(account.Devices[0].Current);

        // Sign in on a second device; a wrong password fails
        using var second = server.Api();
        await Assert.ThrowsAsync<SyncException>(() => second.LoginAsync(new LoginRequest("jane", "nope-nope-nope")));
        var s2 = await second.LoginAsync(new LoginRequest("JANE", "password-123", "Laptop", "Windows", "1.0"));
        second.Token = s2.Token;
        Assert.Equal(2, (await second.AccountAsync()).Devices.Count);
        Assert.Single(await second.DirectoryAsync());

        await second.LogoutAsync();
        var gone = await Assert.ThrowsAsync<SyncException>(() => second.AccountAsync());
        Assert.True(gone.SignedOut);
    }

    [Fact]
    public async Task PushPullNumbersAndNewestWins()
    {
        using var server = new TestServer();
        var (a, _) = await server.Join("alice");
        var (b, _) = await server.Join("bob");

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var push = await a.PushAsync(new PushRequest([
            new PushChange("incidents", id1.ToString(), Now, false, Record("Slip in warehouse", 7, id1)),
            new PushChange("incidents", id2.ToString(), Now, false, Record("Forklift near miss", 8, id2)),
        ]));
        Assert.All(push.Results, r => Assert.Equal("ok", r.Status));
        Assert.Equal([1, 2], push.Results.Select(r => r.Number));

        // Bob, in the same organization, sees them with the server's numbers
        var pulled = await b.ChangesAsync(0);
        Assert.Equal(2, pulled.Changes.Count);
        Assert.Equal(2, pulled.Latest);
        var first = pulled.Changes.Single(c => c.Id == id1.ToString());
        Assert.Equal(1, first.Number);
        Assert.Equal("alice", first.UpdatedBy);
        Assert.Equal(1, JsonDocument.Parse(first.Payload!).RootElement.GetProperty("Seq").GetInt32());

        // Bob's new record gets the next number even though his app thought it was 1
        var id3 = Guid.NewGuid();
        var p3 = await b.PushAsync(new PushRequest([new PushChange("incidents", id3.ToString(), Now, false, Record("Chemical spill", 1, id3))]));
        Assert.Equal(3, p3.Results[0].Number);

        // An older edit loses to a newer one
        long t = Now;
        await a.PushAsync(new PushRequest([new PushChange("incidents", id1.ToString(), t + 5000, false, Record("Slip (updated)", 1, id1))]));
        var stale = await b.PushAsync(new PushRequest([new PushChange("incidents", id1.ToString(), t, false, Record("Slip (old edit)", 1, id1))]));
        Assert.Equal("stale", stale.Results[0].Status);

        // Deletes are kept as markers, and only changes after "since" come back
        long since = (await b.ChangesAsync(0)).Latest;
        await b.PushAsync(new PushRequest([new PushChange("incidents", id2.ToString(), Now + 10_000, true, null)]));
        var after = await a.ChangesAsync(since);
        Assert.Single(after.Changes);
        Assert.True(after.Changes[0].Deleted);
        Assert.Null(after.Changes[0].Payload);
    }

    [Fact]
    public async Task OrganizationsAreSeparate()
    {
        using var server = new TestServer();
        var (a, sa) = await server.Join("alice");
        var (other, so) = await server.Join("olga", "admin", newOrg: "Other Co");
        Assert.NotEqual(sa.Org.Id, so.Org.Id);
        Assert.Equal("Other Co", so.Org.Name);

        await a.PushAsync(new PushRequest([new PushChange("actions", Guid.NewGuid().ToString(), Now, false, Record("Fix guard rail"))]));
        Assert.Empty((await other.ChangesAsync(0)).Changes);
        var p = await other.PushAsync(new PushRequest([new PushChange("actions", Guid.NewGuid().ToString(), Now, false, Record("Other company action"))]));
        Assert.Equal(1, p.Results[0].Number); // numbering is per organization
        Assert.Single((await a.ChangesAsync(0)).Changes);
    }

    [Fact]
    public async Task ViewersAreReadOnly()
    {
        using var server = new TestServer();
        var (viewer, _) = await server.Join("vic", "viewer");
        var r = await viewer.PushAsync(new PushRequest([new PushChange("incidents", Guid.NewGuid().ToString(), Now, false, Record("Nope"))]));
        Assert.Equal("forbidden", r.Results[0].Status);
        var ex = await Assert.ThrowsAsync<SyncException>(() => viewer.UploadFileAsync("library/x/a.txt", new MemoryStream([1, 2, 3])));
        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task Files()
    {
        using var server = new TestServer();
        var (a, _) = await server.Join("alice");
        var (b, _) = await server.Join("bob");
        var bytes = Encoding.UTF8.GetBytes(new string('x', 200_000));
        await a.UploadFileAsync("library/doc1/Safety Policy.pdf", new MemoryStream(bytes));
        var copy = new MemoryStream();
        Assert.True(await b.DownloadFileAsync("library/doc1/Safety Policy.pdf", copy));
        Assert.Equal(bytes, copy.ToArray());
        await b.DeleteFileAsync("library/doc1/Safety Policy.pdf");
        Assert.False(await a.DownloadFileAsync("library/doc1/Safety Policy.pdf", new MemoryStream()));
    }

    [Fact]
    public async Task PauseStopsSyncButNotTheInfo()
    {
        using var server = new TestServer();
        var (a, _) = await server.Join("alice");
        using var admin = await server.Admin();
        (await admin.PostAsJsonAsync("/admin/api/service/stop", new { })).EnsureSuccessStatusCode();
        var ex = await Assert.ThrowsAsync<SyncException>(() => a.ChangesAsync(0));
        Assert.Equal("paused", ex.Error);
        Assert.True(ex.Offline);
        Assert.False((await server.Api().InfoAsync()).Running);
        (await admin.PostAsJsonAsync("/admin/api/service/start", new { })).EnsureSuccessStatusCode();
        await a.ChangesAsync(0);
    }

    [Fact]
    public async Task AdminManagesPeople()
    {
        using var server = new TestServer();
        using var admin = await server.Admin();

        // Add a person from the panel; they sign in with the temporary password
        var created = await (await admin.PostAsJsonAsync("/admin/api/users", new { username = "sam@acme.com", displayName = "Sam Smith", role = "viewer" })).Content.ReadFromJsonAsync<JsonElement>();
        string password = created.GetProperty("password").GetString()!;
        long id = created.GetProperty("user").GetProperty("id").GetInt64();
        using var sam = server.Api();
        var s = await sam.LoginAsync(new LoginRequest("sam@acme.com", password));
        Assert.Equal("viewer", s.User.Role);
        sam.Token = s.Token;

        // Change role, reset the password (signs them out), turn the account off
        (await admin.PutAsJsonAsync($"/admin/api/users/{id}/role", new { role = "manager" })).EnsureSuccessStatusCode();
        var reset = await (await admin.PostAsJsonAsync($"/admin/api/users/{id}/reset-password", new { })).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.ThrowsAsync<SyncException>(() => sam.AccountAsync());
        var s2 = await sam.LoginAsync(new LoginRequest("sam@acme.com", reset.GetProperty("password").GetString()!));
        Assert.Equal("manager", s2.User.Role);
        (await admin.PostAsJsonAsync($"/admin/api/users/{id}/disable", new { })).EnsureSuccessStatusCode();
        var off = await Assert.ThrowsAsync<SyncException>(() => sam.LoginAsync(new LoginRequest("sam@acme.com", reset.GetProperty("password").GetString()!)));
        Assert.Equal("disabled", off.Error);

        var users = await admin.GetFromJsonAsync<JsonElement>("/admin/api/users");
        Assert.Equal(1, users.GetArrayLength());
    }

    [Fact]
    public async Task PanelDataBrowserAndOverview()
    {
        using var server = new TestServer();
        var (a, _) = await server.Join("alice");
        await a.PushAsync(new PushRequest([new PushChange("incidents", Guid.NewGuid().ToString(), Now, false, Record("Ladder fall"))]));
        using var admin = await server.Admin();
        var cols = await admin.GetFromJsonAsync<JsonElement>("/admin/api/data/collections");
        Assert.Equal("incidents", cols[0].GetProperty("collection").GetString());
        var recs = await admin.GetFromJsonAsync<JsonElement>("/admin/api/data/records?collection=incidents&q=ladder");
        Assert.Equal(1, recs.GetProperty("total").GetInt64());
        Assert.Equal("Ladder fall", recs.GetProperty("rows")[0].GetProperty("title").GetString());
        string export = await admin.GetStringAsync("/admin/api/data/export");
        Assert.Contains("Ladder fall", export);

        var o = await admin.GetFromJsonAsync<JsonElement>("/admin/api/overview");
        Assert.Equal(1, o.GetProperty("users").GetInt32());
        Assert.Equal(1, o.GetProperty("records").GetInt64());
        var nodes = await admin.GetFromJsonAsync<JsonElement>("/admin/api/nodes");
        Assert.True(nodes.GetArrayLength() >= 1);

        // The panel needs a sign-in and the X-Guardrail header on changes
        using var anon = server.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/admin/api/overview")).StatusCode);
        var noHeader = server.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await noHeader.PostAsJsonAsync("/admin/api/service/stop", new { })).StatusCode);
    }

    [Fact]
    public async Task LoginThrottle()
    {
        using var server = new TestServer();
        await server.Join("alice");
        using var api = server.Api();
        for (int i = 0; i < 10; i++)
            await Assert.ThrowsAsync<SyncException>(() => api.LoginAsync(new LoginRequest("alice", "wrong-password")));
        var ex = await Assert.ThrowsAsync<SyncException>(() => api.LoginAsync(new LoginRequest("alice", "password-123")));
        Assert.Equal(429, ex.Status);
    }

    [Fact]
    public async Task OpenSignupMakesTheFirstPersonAdmin()
    {
        using var server = new TestServer();
        using var admin = await server.Admin();
        (await admin.PutAsJsonAsync("/admin/api/settings", new { openSignup = true, signupRole = "viewer" })).EnsureSuccessStatusCode();
        using var api = server.Api();
        var first = await api.RegisterAsync(new RegisterRequest("first", "password-123"));
        Assert.Equal("admin", first.User.Role);
        var second = await api.RegisterAsync(new RegisterRequest("second", "password-123"));
        Assert.Equal("viewer", second.User.Role);
    }
}
