using System.IO;
using System.Text.Json;

namespace Guardrail.Data;

/// <summary>
/// A list of records kept in one JSON file. Writes go to a temp file first and are then
/// swapped in, so a crash mid-save never corrupts the existing data.
/// </summary>
public sealed class JsonStore<T> where T : RecordBase
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<T> _items;
    /// <summary>True when the file couldn't be read because Guardrail is locked; saving is then refused so nothing is overwritten.</summary>
    private bool _unreadable;

    public JsonStore(string folder, string name)
    {
        _path = Path.Combine(folder, name + ".json");
        _items = Load(_path, out _unreadable);
    }

    public IReadOnlyList<T> Items => _items;

    /// <summary>Raised after any add, update or delete.</summary>
    public event EventHandler? Changed;

    public void Add(T item)
    {
        item.Seq = _items.Count == 0 ? 1 : _items.Max(i => i.Seq) + 1;
        item.CreatedAt = item.UpdatedAt = DateTime.Now;
        _items.Add(item);
        Save();
    }

    public void Update(T item)
    {
        item.UpdatedAt = DateTime.Now;
        Save();
    }

    public void Delete(T item)
    {
        _items.Remove(item);
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
