using System.Text.RegularExpressions;

namespace Guardrail.Sync;

/// <summary>Limits and rules shared by Guardrail Server and the Guardrail apps (see server/PROTOCOL.md).</summary>
public static partial class Protocol
{
    /// <summary>Bumped when the API changes in a way older apps can't handle.</summary>
    public const int Version = 1;

    public const int DefaultPort = 5080;
    public const int MaxPushBatch = 500;
    public const int MaxPullBatch = 1000;
    /// <summary>One record's JSON, in characters.</summary>
    public const int MaxPayloadChars = 4_000_000;
    /// <summary>One file (documents, photos): 1 GB.</summary>
    public const long MaxFileBytes = 1024L * 1024 * 1024;

    public static class Roles
    {
        /// <summary>Everything, including managing the organization's people from the app.</summary>
        public const string Admin = "admin";
        /// <summary>Adds, edits and deletes records.</summary>
        public const string Manager = "manager";
        /// <summary>Adds, edits and deletes records.</summary>
        public const string Member = "member";
        /// <summary>Reads everything, changes nothing.</summary>
        public const string Viewer = "viewer";

        public static readonly string[] All = [Admin, Manager, Member, Viewer];
        public static bool IsValid(string? role) => role != null && All.Contains(role);
        public static bool CanWrite(string role) => role != Viewer;

        public static string Label(string role) => role switch
        {
            Admin => "Admin",
            Manager => "Manager",
            Member => "Member",
            Viewer => "Viewer (read-only)",
            _ => role,
        };
    }

    public static class Statuses
    {
        public const string Ok = "ok";
        /// <summary>The server already has a newer version of the record; the app should take the server's copy.</summary>
        public const string Stale = "stale";
        /// <summary>The account can't change records (viewer).</summary>
        public const string Forbidden = "forbidden";
    }

    /// <summary>Collection names: "incidents", "fleet-vehicles" …</summary>
    public static bool IsValidCollection(string? name) => name != null && CollectionRegex().IsMatch(name);

    /// <summary>Record ids and file names: letters, digits and . _ - / (up to 200).</summary>
    public static bool IsValidId(string? id) => id != null && id.Length is > 0 and <= 200 && IdRegex().IsMatch(id) && !id.Contains("..");

    /// <summary>Usernames are case-insensitive: "Jane.Doe" → "jane.doe". Email addresses work too. Null when not allowed.</summary>
    public static string? NormalizeUsername(string? name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        return UsernameRegex().IsMatch(n) ? n : null;
    }

    public const string UsernameRules = "Usernames are 3 to 64 characters: letters, numbers and . _ - + @ (an email address works).";
    public const int MinPasswordLength = 8;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")] private static partial Regex CollectionRegex();
    [GeneratedRegex("^[A-Za-z0-9._/-]+$")] private static partial Regex IdRegex();
    [GeneratedRegex("^[a-z0-9][a-z0-9._+@-]{2,63}$")] private static partial Regex UsernameRegex();
}

// ----- Requests and responses (JSON, camelCase) -----

public sealed record ServerInfo(string Product, string Name, string Version, int Protocol, bool OpenSignup, bool Running);

public sealed record LoginRequest(string Username, string Password, string? DeviceName = null, string? Platform = null, string? AppVersion = null);

public sealed record RegisterRequest(
    string Username, string Password, string? DisplayName = null, string? Email = null, string? Invite = null,
    string? DeviceName = null, string? Platform = null, string? AppVersion = null);

public sealed record UserInfo(long Id, string Username, string DisplayName, string? Email, string Role);
public sealed record OrgInfo(long Id, string Name);
public sealed record SessionResponse(string Token, string DeviceId, UserInfo User, OrgInfo Org);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record DeviceInfo(string Id, string Name, string Platform, string AppVersion, DateTime CreatedAt, DateTime LastSeen, string? Ip, bool Current);
public sealed record AccountResponse(UserInfo User, OrgInfo Org, List<DeviceInfo> Devices);
/// <summary>Someone in the same organization (for "Owner" and "Assigned to" pickers).</summary>
public sealed record DirectoryUser(string Username, string DisplayName, string Role);

/// <summary>
/// One record as stored on the server. <see cref="Payload"/> is the record's JSON; <see cref="Number"/> is the
/// organization-wide record number the server assigned (INC-0042 → 42).
/// </summary>
public sealed record Change(string Collection, string Id, long Seq, long Modified, bool Deleted, string? Payload, int Number, string? UpdatedBy);
public sealed record ChangesResponse(List<Change> Changes, long Latest, bool More);

/// <summary>A change from an app. <see cref="Modified"/> is Unix milliseconds (UTC) of the edit; the newest edit wins.</summary>
public sealed record PushChange(string Collection, string Id, long Modified, bool Deleted, string? Payload);
public sealed record PushRequest(List<PushChange> Changes);
public sealed record PushResult(string Collection, string Id, string Status, long Seq, int Number);
public sealed record PushResponse(List<PushResult> Results, long Latest);

public sealed record ErrorResponse(string Error, string Message);
