using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guardrail.Data;

/// <summary>
/// Optional PIN / password protection for everything Guardrail keeps on this PC (local mode).
///
/// When turned on, a random 256-bit data key encrypts every record file and every attached document with
/// AES-GCM. The data key is stored in lock.json twice: once wrapped with a key derived from your PIN or
/// password (PBKDF2-SHA256, 600,000 rounds) and once with a key derived from a recovery key shown when you
/// turn protection on. Guardrail never stores the PIN or password itself.
///
/// Video files (dashcam / CCTV) are left as they are: they're large, they're often copies of footage kept
/// elsewhere, and Crash Review AI reads them directly.
///
/// When Guardrail Server arrives, signing in to the server replaces this lock for connected installs;
/// local-only installs keep using it.
/// </summary>
public static class DataLock
{
    private static readonly byte[] Magic = "GRENC1"u8.ToArray();
    private const int Iterations = 600_000;
    private const int NonceSize = 12, TagSize = 16;
    private static byte[]? _key;

    /// <summary>The data folder (also used by <see cref="DataStore"/>; defined here so the lock never loads the stores).</summary>
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Guardrail", "data");

    /// <summary>For the self-test: work on a temporary folder instead of the real one.</summary>
    internal static string? FolderOverride { get; set; }
    private static string Folder => FolderOverride ?? DataFolder;

    public sealed class LockInfo
    {
        public int Version { get; set; } = 1;
        /// <summary>"PIN" or "Password".</summary>
        public string Kind { get; set; } = "PIN";
        public string Salt { get; set; } = "";
        public int Iterations { get; set; } = DataLock.Iterations;
        public string WrappedKey { get; set; } = "";
        public string RecoverySalt { get; set; } = "";
        public string RecoveryWrappedKey { get; set; } = "";
        /// <summary>Minutes without activity before Guardrail locks its window (0 = never).</summary>
        public int AutoLockMinutes { get; set; } = 10;
    }

    public static string LockFile => Path.Combine(Folder, "lock.json");

    /// <summary>True when the data is protected with a PIN / password.</summary>
    public static bool IsEnabled => File.Exists(LockFile);

    /// <summary>True when the data can be read: protection is off, or it's on and has been unlocked.</summary>
    public static bool IsOpen => !IsEnabled || _key != null;

    public static LockInfo? Info
    {
        get
        {
            try { return IsEnabled ? JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(LockFile)) : null; }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
    }

    public static bool IsPin => Info?.Kind == "PIN";

    // ----- Reading and writing data files -----

    public static bool IsEncrypted(byte[] bytes) => bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static bool IsEncryptedFile(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[Magic.Length];
            return f.Read(head, 0, head.Length) == head.Length && IsEncrypted(head);
        }
        catch (IOException) { return false; }
    }

    public static byte[] ReadBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!IsEncrypted(bytes)) return bytes;
        if (_key == null) throw new LockedException();
        return Decrypt(bytes, _key);
    }

    public static void WriteBytes(string path, byte[] data) =>
        File.WriteAllBytes(path, _key != null && IsEnabled ? Encrypt(data, _key) : data);

    public static string ReadText(string path) => Encoding.UTF8.GetString(ReadBytes(path));

    public static void WriteText(string path, string text) => WriteBytes(path, Encoding.UTF8.GetBytes(text));

    /// <summary>Encrypts a file in the data folder in place when protection is on (videos are left alone).</summary>
    public static void ProtectFile(string path)
    {
        if (!IsEnabled || _key == null || IsVideo(path) || !File.Exists(path)) return;
        var bytes = File.ReadAllBytes(path);
        if (!IsEncrypted(bytes)) File.WriteAllBytes(path, Encrypt(bytes, _key));
    }

    internal static bool IsVideo(string path) => Guardrail.CrashAi.CrashAnalyzer.IsVideo(path);

    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);
        var output = new byte[Magic.Length + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        tag.CopyTo(output, Magic.Length + NonceSize);
        cipher.CopyTo(output, Magic.Length + NonceSize + TagSize);
        return output;
    }

    private static byte[] Decrypt(byte[] data, byte[] key)
    {
        int offset = Magic.Length;
        var nonce = data.AsSpan(offset, NonceSize);
        var tag = data.AsSpan(offset + NonceSize, TagSize);
        var cipher = data.AsSpan(offset + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    // ----- Keys -----

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string Wrap(byte[] dataKey, byte[] wrappingKey) => Convert.ToBase64String(Encrypt(dataKey, wrappingKey));

    private static byte[]? Unwrap(string wrapped, byte[] wrappingKey)
    {
        try { return Decrypt(Convert.FromBase64String(wrapped), wrappingKey); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return null; }
    }

    /// <summary>"ABCDE-FGHJK-LMNPQ-RSTUV-WXYZ2": 25 letters and digits, easy to copy down.</summary>
    private static string NewRecoveryKey()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 25).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return string.Join("-", Enumerable.Range(0, 5).Select(i => new string(chars, i * 5, 5)));
    }

    private static string NormalizeRecovery(string key) => new(key.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static void SaveInfo(LockInfo info)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(LockFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>A PIN is 4–12 digits; a password is at least 8 characters. Returns the problem, or null when it's fine.</summary>
    public static string? Validate(string secret, string kind) => kind == "PIN"
        ? (secret.Length is >= 4 and <= 12 && secret.All(char.IsDigit) ? null : "A PIN is 4 to 12 digits.")
        : (secret.Length >= 8 ? null : "Use at least 8 characters for your password.");

    // ----- Lock / unlock -----

    /// <summary>Checks the PIN / password and, if it's right, opens the data. Slow on purpose (about half a second).</summary>
    public static bool Unlock(string secret)
    {
        var info = Info;
        if (info == null) return true;
        var key = Unwrap(info.WrappedKey, Derive(secret, Convert.FromBase64String(info.Salt), info.Iterations));
        if (key == null) return false;
        _key = key;
        return true;
    }

    public static bool UnlockWithRecoveryKey(string recoveryKey)
    {
        var info = Info;
        if (info == null) return true;
        var key = Unwrap(info.RecoveryWrappedKey, Derive(NormalizeRecovery(recoveryKey), Convert.FromBase64String(info.RecoverySalt), info.Iterations));
        if (key == null) return false;
        _key = key;
        return true;
    }

    /// <summary>For the self-test and restores: forget the key.</summary>
    internal static void Forget()
    {
        if (_key != null) CryptographicOperations.ZeroMemory(_key);
        _key = null;
    }

    /// <summary>Turns protection on: encrypts every file in the data folder and returns the recovery key to show once.</summary>
    public static string Enable(string secret, string kind, int autoLockMinutes)
    {
        if (IsEnabled) throw new InvalidOperationException("Protection is already on.");
        var dataKey = RandomNumberGenerator.GetBytes(32);
        string recovery = NewRecoveryKey();
        var salt = RandomNumberGenerator.GetBytes(16);
        var recoverySalt = RandomNumberGenerator.GetBytes(16);
        var info = new LockInfo
        {
            Kind = kind,
            Salt = Convert.ToBase64String(salt),
            RecoverySalt = Convert.ToBase64String(recoverySalt),
            WrappedKey = Wrap(dataKey, Derive(secret, salt, Iterations)),
            RecoveryWrappedKey = Wrap(dataKey, Derive(NormalizeRecovery(recovery), recoverySalt, Iterations)),
            AutoLockMinutes = autoLockMinutes,
        };
        Directory.CreateDirectory(Folder);
        _key = dataKey;
        RewriteAll(encrypt: true);
        SaveInfo(info);
        return recovery;
    }

    /// <summary>Turns protection off: decrypts every file. The data must be unlocked.</summary>
    public static void Disable()
    {
        if (!IsEnabled) return;
        if (_key == null) throw new LockedException();
        RewriteAll(encrypt: false);
        File.Delete(LockFile);
        Forget();
    }

    /// <summary>Changes the PIN / password (the data key and the recovery key stay the same).</summary>
    public static void ChangeSecret(string newSecret, string kind)
    {
        var info = Info ?? throw new InvalidOperationException("Protection is off.");
        if (_key == null) throw new LockedException();
        var salt = RandomNumberGenerator.GetBytes(16);
        info.Kind = kind;
        info.Salt = Convert.ToBase64String(salt);
        info.Iterations = Iterations;
        info.WrappedKey = Wrap(_key, Derive(newSecret, salt, Iterations));
        SaveInfo(info);
    }

    public static void SetAutoLock(int minutes)
    {
        var info = Info;
        if (info == null) return;
        info.AutoLockMinutes = minutes;
        SaveInfo(info);
    }

    /// <summary>Re-saves every record and document encrypted (or plain). Old backup copies (.bak / .tmp) are removed.</summary>
    private static void RewriteAll(bool encrypt)
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var path in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).ToList())
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, "lock.json", StringComparison.OrdinalIgnoreCase) || IsVideo(path)) continue;
            if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path); // plain copies left over from earlier saves
                continue;
            }
            var bytes = File.ReadAllBytes(path);
            bool isEncrypted = IsEncrypted(bytes);
            if (encrypt && !isEncrypted) File.WriteAllBytes(path, Encrypt(bytes, _key!));
            else if (!encrypt && isEncrypted) File.WriteAllBytes(path, Decrypt(bytes, _key!));
        }
    }
}

public sealed class LockedException() : InvalidOperationException("Guardrail is locked.");

/// <summary>Opens protected documents: decrypts a temporary copy that's deleted when Guardrail closes.</summary>
public static class FileVault
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "Guardrail-open");

    /// <summary>Opens a file from the data folder with its default program (decrypting a temporary copy when needed).</summary>
    public static void Open(string path)
    {
        string target = path;
        if (DataLock.IsEncryptedFile(path))
        {
            var dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            target = Path.Combine(dir, Path.GetFileName(path));
            File.WriteAllBytes(target, DataLock.ReadBytes(path));
        }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// <summary>Deletes decrypted copies (called at startup and exit; files still open in another program are skipped).</summary>
    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            foreach (var dir in Directory.GetDirectories(TempRoot))
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (IOException) { }
    }
}
