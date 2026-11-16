using System.IO;
using System.Text.Json;

namespace Guardrail.Data;

/// <summary>What Guardrail Server sync needs from a store, whatever its record type.</summary>
public interface ISyncStore
{
    /// <summary>The collection name on the server (the file name: "incidents", "fleet-vehicles"…).</summary>
    string Name { get; }
    IEnumerable<Guid> Ids { get; }
    /// <summary>The record as JSON, or null when it's gone.</summary>
    string? Serialize(Guid id);
    /// <summary>When the record was last changed, as Unix milliseconds (UTC).</summary>
    long ModifiedMs(Guid id);
    /// <summary>Applies records from the server (json null = deleted) without queuing them to be sent back.</summary>
    void ApplyRemote(IReadOnlyList<(Guid Id, string? Json)> changes);
    /// <summary>Takes the organization-wide record number the server assigned.</summary>
    void SetNumber(Guid id, int number);
    /// <summary>Empties the store (connecting to a server and taking its records instead of this PC's).</summary>
    void Clear();
}

/// <summary>Every store, and the "something changed here" signal the sync engine listens to.</summary>
public static class SyncHooks
{
    public static readonly List<ISyncStore> Stores = [];
    /// <summary>(collection, record id, deleted) after a local add, update or delete.</summary>
    public static event Action<string, Guid, bool>? LocalChange;
    internal static void Raise(string name, Guid id, bool deleted) => LocalChange?.Invoke(name, id, deleted);
    public static ISyncStore? Find(string name) => Stores.FirstOrDefault(s => s.Name == name);
}

/// <summary>
/// A list of records kept in one JSON file. Writes go to a temp file first and are then
/// swapped in, so a crash mid-save never corrupts the existing data.
/// </summary>
public sealed class JsonStore<T> : ISyncStore where T : RecordBase
{
    private static readonly JsonSerializerOptions Compact = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<T> _items;
    /// <summary>True when the file couldn't be read because Guardrail is locked; saving is then refused so nothing is overwritten.</summary>
    private bool _unreadable;

    public JsonStore(string folder, string name)
    {
        Name = name;
        _path = Path.Combine(folder, name + ".json");
        _items = Load(_path, out _unreadable);
        // Only the app's own store for each name syncs (copies opened elsewhere, e.g. by a restore check, don't).
        lock (SyncHooks.Stores)
            if (SyncHooks.Stores.All(s => s.Name != name)) { SyncHooks.Stores.Add(this); _syncs = true; }
    }

    private readonly bool _syncs;

    public string Name { get; }

    public IReadOnlyList<T> Items => _items;

    /// <summary>Raised after any add, update or delete.</summary>
    public event EventHandler? Changed;

    public void Add(T item)
    {
        item.Seq = _items.Count == 0 ? 1 : _items.Max(i => i.Seq) + 1;
        item.CreatedAt = item.UpdatedAt = DateTime.Now;
        _items.Add(item);
        Save();
        if (_syncs) SyncHooks.Raise(Name, item.Id, false);
    }

    public void Update(T item)
    {
        item.UpdatedAt = DateTime.Now;
        Save();
        if (_syncs) SyncHooks.Raise(Name, item.Id, false);
    }

    public void Delete(T item)
    {
        _items.Remove(item);
        Save();
        if (_syncs) SyncHooks.Raise(Name, item.Id, true);
    }

    // ----- Guardrail Server sync -----

    IEnumerable<Guid> ISyncStore.Ids => _items.Select(i => i.Id).ToList();

    string? ISyncStore.Serialize(Guid id) => _items.FirstOrDefault(i => i.Id == id) is { } item ? JsonSerializer.Serialize(item, Compact) : null;

    long ISyncStore.ModifiedMs(Guid id) =>
        _items.FirstOrDefault(i => i.Id == id) is { } item
            ? new DateTimeOffset(item.UpdatedAt.Kind == DateTimeKind.Utc ? item.UpdatedAt : item.UpdatedAt.ToUniversalTime()).ToUnixTimeMilliseconds()
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    void ISyncStore.ApplyRemote(IReadOnlyList<(Guid Id, string? Json)> changes)
    {
        bool any = false;
        foreach (var (id, json) in changes)
        {
            int at = _items.FindIndex(i => i.Id == id);
            if (json == null)
            {
                if (at >= 0) { _items.RemoveAt(at); any = true; }
                continue;
            }
            T? item;
            try { item = JsonSerializer.Deserialize<T>(json, Options); }
            catch (JsonException) { continue; } // a record from a newer Guardrail this version can't read
            if (item == null) continue;
            item.Id = id;
            if (at >= 0) _items[at] = item; else _items.Add(item);
            any = true;
        }
        if (any) Save();
    }

    void ISyncStore.SetNumber(Guid id, int number)
    {
        if (number <= 0 || _items.FirstOrDefault(i => i.Id == id) is not { } item || item.Seq == number) return;
        item.Seq = number;
        Save();
    }

    void ISyncStore.Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Save();
    }

    private void Save()
    {
        if (_unreadable) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            DataLock.WriteText(temp, JsonSerializer.Serialize(_items, Options));
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
            else File.Move(temp, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Guardrail.Services.Toaster.Show($"Couldn't save to disk ({ex.Message}). Your change is kept until you close the app.");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<T> Load(string path, out bool unreadable)
    {
        unreadable = false;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate))
                    return JsonSerializer.Deserialize<List<T>>(DataLock.ReadText(candidate), Options) ?? [];
            }
            catch (JsonException)
            {
                // Fall through to the backup copy.
            }
            catch (LockedException)
            {
                unreadable = true;
                return [];
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Damaged or encrypted with another key: try the backup copy.
            }
        }
        return [];
    }
}
