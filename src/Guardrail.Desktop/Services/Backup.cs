using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using Guardrail.Data;

namespace Guardrail.Services;

/// <summary>
/// Whole-data-folder backups as a .zip. With PIN / password protection on, the files inside are already
/// encrypted and lock.json travels with them, so a backup needs the same PIN, password or recovery key.
/// </summary>
public static class Backup
{
    public static void Create(string zipPath)
    {
        if (!Directory.Exists(DataStore.Folder)) Directory.CreateDirectory(DataStore.Folder);
        string temp = zipPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(DataStore.Folder, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetRelativePath(DataStore.Folder, file).Replace('\\', '/');
                // Shared read: a store may be saving at the same moment.
                using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = zip.CreateEntry(name, DataLock.IsVideo(file) ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open();
                src.CopyTo(dst);
            }
        }
        File.Move(temp, zipPath, overwrite: true);
    }

    /// <summary>Swaps the data folder for the backup's contents. The current folder is kept as data-before-restore-….</summary>
    public static void Restore(string zipPath)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (!zip.Entries.Any(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/')))
                throw new InvalidDataException("That file doesn't look like a GuardHawk backup.");
            if (zip.Entries.Any(e => e.FullName.Contains("..")))
                throw new InvalidDataException("That backup contains unsafe paths.");
        }
        string parent = Path.GetDirectoryName(DataStore.Folder)!;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string incoming = Path.Combine(parent, $"data-restoring-{stamp}");
        ZipFile.ExtractToDirectory(zipPath, incoming);
        if (Directory.Exists(DataStore.Folder))
            Directory.Move(DataStore.Folder, Path.Combine(parent, $"data-before-restore-{stamp}"));
        Directory.Move(incoming, DataStore.Folder);
    }

    /// <summary>Starts a fresh copy of Guardrail and closes this one (so everything reloads from the restored data).</summary>
    public static void Restart()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
