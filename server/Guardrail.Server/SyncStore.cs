using System.Text.Json;
using System.Text.Json.Nodes;
using Guardrail.Sync;

namespace Guardrail.Server;

public sealed record CollectionCount(string Collection, long Records, long Deleted, DateTime? LastChange);
public sealed record RecordSummary(string Id, int Number, string Title, string? UpdatedBy, DateTime UpdatedAt, bool Deleted);
public sealed record RecordDetail(string Collection, string Id, int Number, long Seq, string? UpdatedBy, DateTime UpdatedAt, bool Deleted, JsonNode? Data);

/// <summary>
/// Each organization's records. Every accepted change gets the organization's next sequence number; apps ask for
/// "changes after N". When two people change the same record, the newer edit wins. Deletes are kept as markers so
/// every device sees them.
///
/// Scaling: a push locks only its own organization's row (UPDATE orgs … first thing in the transaction), so pushes
/// for one organization are serialized — which keeps sequence numbers in commit order and makes "changes after N"
/// safe — while different organizations never wait on each other.
/// </summary>
public sealed class SyncStore(Db db)
{
    public ChangesResponse Changes(long orgId, long since)
    {
        using var c = db.Open();
        var list = Db.Query(c, """
            SELECT collection, id, seq, modified, deleted, payload, number, updated_by FROM records
            WHERE org_id = @orgId AND seq > @since ORDER BY seq LIMIT @limit
            """, r => new Change(r.GetString(0), r.GetString(1), Db.Long(r, 2), Db.Long(r, 3), Db.Flag(r, 4), Db.Str(r, 5), (int)Db.Long(r, 6), Db.Str(r, 7)),
            new { orgId, since, limit = Protocol.MaxPullBatch + 1 });
        bool more = list.Count > Protocol.MaxPullBatch;
        if (more) list.RemoveAt(list.Count - 1);
        long latest = Db.Long(Db.Scalar(c, "SELECT seq FROM orgs WHERE id = @orgId", new { orgId }));
        return new ChangesResponse(list, latest, more);
    }

    public PushResponse Push(SessionUser user, PushRequest request)
    {
        if (request.Changes == null || request.Changes.Count == 0) return new PushResponse([], LatestSeq(user.OrgId));
        if (request.Changes.Count > Protocol.MaxPushBatch)
            throw new ApiProblem(400, "too_many", $"Send at most {Protocol.MaxPushBatch} changes at a time.");
        foreach (var ch in request.Changes)
        {
            if (!Protocol.IsValidCollection(ch.Collection) || !Protocol.IsValidId(ch.Id))
                throw new ApiProblem(400, "bad_request", "Invalid collection or id.");
            if (!ch.Deleted && (ch.Payload == null || ch.Payload.Length > Protocol.MaxPayloadChars))
                throw new ApiProblem(400, "bad_request", $"Missing or oversized record for {ch.Collection}/{ch.Id}.");
        }

        if (!Protocol.Roles.CanWrite(user.Role))
            return new PushResponse(request.Changes.Select(ch => new PushResult(ch.Collection, ch.Id, Protocol.Statuses.Forbidden, 0, 0)).ToList(), LatestSeq(user.OrgId));

        long orgId = user.OrgId;
        var results = new List<PushResult>();
        using var c = db.Open();
        using var tx = db.Begin(c);
        // Lock this organization's row for the rest of the transaction (works the same in SQLite and PostgreSQL).
        long seq = Db.Long(Db.Scalar(c, "UPDATE orgs SET seq = seq WHERE id = @orgId RETURNING seq", new { orgId }, tx));
        long now = Db.Now();
        foreach (var ch in request.Changes)
        {
            var existing = Db.Query(c, "SELECT modified, seq, number FROM records WHERE org_id = @orgId AND collection = @col AND id = @id",
                r => (Modified: Db.Long(r, 0), Seq: Db.Long(r, 1), Number: (int)Db.Long(r, 2)), new { orgId, col = ch.Collection, id = ch.Id }, tx);
            if (existing.Count > 0 && existing[0].Modified > ch.Modified)
            {
                results.Add(new PushResult(ch.Collection, ch.Id, Protocol.Statuses.Stale, existing[0].Seq, existing[0].Number));
                continue;
            }

            // Record numbers (INC-0042) are organization-wide: a new record gets the next number for its collection.
            int number = existing.Count > 0 ? existing[0].Number : 0;
            string? payload = null;
            if (!ch.Deleted)
            {
                JsonObject obj;
                try { obj = JsonNode.Parse(ch.Payload!) as JsonObject ?? throw new JsonException(); }
                catch (JsonException) { throw new ApiProblem(400, "bad_request", $"The record {ch.Collection}/{ch.Id} isn't valid JSON."); }
                if (number == 0)
                    number = (int)Db.Long(Db.Scalar(c, """
                        INSERT INTO counters(org_id, collection, value) VALUES(@orgId, @col, 1)
                        ON CONFLICT(org_id, collection) DO UPDATE SET value = counters.value + 1 RETURNING value
                        """, new { orgId, col = ch.Collection }, tx));
                if (obj.ContainsKey("Seq")) obj["Seq"] = number;
                payload = obj.ToJsonString();
            }

            seq++;
            Db.Exec(c, """
                INSERT INTO records(org_id, collection, id, seq, number, modified, deleted, payload, updated_by, updated_at)
                VALUES(@orgId, @col, @id, @seq, @number, @modified, @deleted, @payload, @by, @now)
                ON CONFLICT(org_id, collection, id) DO UPDATE SET seq = excluded.seq, number = excluded.number, modified = excluded.modified,
                    deleted = excluded.deleted, payload = excluded.payload, updated_by = excluded.updated_by, updated_at = excluded.updated_at
                """, new { orgId, col = ch.Collection, id = ch.Id, seq, number, modified = ch.Modified, deleted = ch.Deleted, payload, by = user.Username, now }, tx);
            results.Add(new PushResult(ch.Collection, ch.Id, Protocol.Statuses.Ok, seq, number));
        }
        Db.Exec(c, "UPDATE orgs SET seq = @seq WHERE id = @orgId", new { seq, orgId }, tx);
        tx.Commit();
        return new PushResponse(results, seq);
    }

    public long LatestSeq(long orgId)
    {
        using var c = db.Open();
        return Db.Long(Db.Scalar(c, "SELECT seq FROM orgs WHERE id = @orgId", new { orgId }));
    }

    /// <summary>Removes an organization's records and files but keeps its accounts.</summary>
    public async Task EraseData(long orgId, BlobStore blobs)
    {
        await blobs.DeletePrefix($"{orgId}/");
        using var c = db.Open();
        using var tx = db.Begin(c);
        Db.Exec(c, "DELETE FROM records WHERE org_id = @orgId", new { orgId }, tx);
        Db.Exec(c, "DELETE FROM files WHERE org_id = @orgId", new { orgId }, tx);
        Db.Exec(c, "DELETE FROM counters WHERE org_id = @orgId", new { orgId }, tx);
        tx.Commit();
    }

    // ----- Files: metadata here, contents in the BlobStore under "<org>/<name>" -----

    public void FileSaved(long orgId, string name, long size, string by)
    {
        using var c = db.Open();
        Db.Exec(c, """
            INSERT INTO files(org_id, name, size, uploaded_by, uploaded_at) VALUES(@orgId, @name, @size, @by, @now)
            ON CONFLICT(org_id, name) DO UPDATE SET size = excluded.size, uploaded_by = excluded.uploaded_by, uploaded_at = excluded.uploaded_at
            """, new { orgId, name, size, by, now = Db.Now() });
    }

    public bool FileDeleted(long orgId, string name)
    {
        using var c = db.Open();
        return Db.Exec(c, "DELETE FROM files WHERE org_id = @orgId AND name = @name", new { orgId, name }) > 0;
    }

    // ----- Web panel: totals and a read-only data browser -----

    public (long Records, long Files, long FileBytes) Totals()
    {
        using var c = db.Open();
        return (Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM records WHERE deleted = 0")),
                Db.Long(Db.Scalar(c, "SELECT COUNT(*) FROM files")),
                Db.Long(Db.Scalar(c, "SELECT COALESCE(SUM(size), 0) FROM files")));
    }

    public List<CollectionCount> Collections(long orgId)
    {
        using var c = db.Open();
        return Db.Query(c, """
            SELECT collection, SUM(CASE WHEN deleted = 0 THEN 1 ELSE 0 END), SUM(deleted), MAX(updated_at)
            FROM records WHERE org_id = @orgId GROUP BY collection ORDER BY collection
            """, r => new CollectionCount(r.GetString(0), Db.Long(r, 1), Db.Long(r, 2), Db.Time(r, 3)), new { orgId });
    }

    public (List<RecordSummary> Rows, long Total) Records(long orgId, string collection, string? search, int offset, int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        using var c = db.Open();
        string filter = string.IsNullOrWhiteSpace(search) ? "" : (db.IsPostgres ? " AND payload ILIKE @q" : " AND payload LIKE @q");
        object args = string.IsNullOrWhiteSpace(search)
            ? new { orgId, collection, offset, limit }
            : new { orgId, collection, offset, limit, q = "%" + search.Trim() + "%" };
        long total = Db.Long(Db.Scalar(c, $"SELECT COUNT(*) FROM records WHERE org_id = @orgId AND collection = @collection AND deleted = 0{filter}", args));
        var rows = Db.Query(c, $"""
            SELECT id, number, payload, updated_by, updated_at, deleted FROM records
            WHERE org_id = @orgId AND collection = @collection AND deleted = 0{filter}
            ORDER BY number DESC, updated_at DESC LIMIT @limit OFFSET @offset
            """, r => new RecordSummary(r.GetString(0), (int)Db.Long(r, 1), TitleOf(Db.Str(r, 2)), Db.Str(r, 3), Db.Time(Db.Long(r, 4)), Db.Flag(r, 5)), args);
        return (rows, total);
    }

    public RecordDetail? Record(long orgId, string collection, string id)
    {
        using var c = db.Open();
        return Db.Query(c, "SELECT number, seq, updated_by, updated_at, deleted, payload FROM records WHERE org_id = @orgId AND collection = @collection AND id = @id",
            r => new RecordDetail(collection, id, (int)Db.Long(r, 0), Db.Long(r, 1), Db.Str(r, 2), Db.Time(Db.Long(r, 3)), Db.Flag(r, 4),
                Db.Str(r, 5) is { } p ? JsonNode.Parse(p) : null), new { orgId, collection, id }).FirstOrDefault();
    }

    /// <summary>A readable one-liner for a record: its title, name or description.</summary>
    public static string TitleOf(string? payload)
    {
        if (payload == null) return "";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var key in new[] { "Title", "Name", "FullName", "Description", "Summary", "ChemicalName", "Equipment", "UnitNumber", "Task", "Hazard", "VideoPath", "Course", "Employee" })
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                    return s.Length > 120 ? s[..120] + "…" : s;
        }
        catch (JsonException) { }
        return "";
    }

    /// <summary>Everything for one organization as JSON lines (the web panel's Export).</summary>
    public async Task ExportJson(long orgId, Stream output, CancellationToken ct)
    {
        await using var w = new StreamWriter(output, leaveOpen: true);
        using var c = db.Open();
        using var cmd = Db.Command(c, "SELECT collection, id, number, updated_by, updated_at, payload FROM records WHERE org_id = @orgId AND deleted = 0 ORDER BY collection, number", new { orgId }, null);
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var line = new JsonObject
            {
                ["collection"] = r.GetString(0),
                ["id"] = r.GetString(1),
                ["number"] = Db.Long(r, 2),
                ["updatedBy"] = Db.Str(r, 3),
                ["updatedAt"] = Db.Time(Db.Long(r, 4)),
                ["data"] = Db.Str(r, 5) is { } p ? JsonNode.Parse(p) : null,
            };
            await w.WriteLineAsync(line.ToJsonString());
        }
    }
}
