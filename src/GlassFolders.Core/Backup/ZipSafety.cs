using System.IO.Compression;
using System.Security.Cryptography;

namespace GlassFolders.Core.Backup;

/// <summary>
/// Security validator for backup ZIP archives. Protects against zip slip path traversal,
/// zip bombs (excessive file size/count), and untrusted executable payload injection.
/// </summary>
public static class ZipSafety
{
    public const long MaxTotalSizeBytes = 50 * 1024 * 1024; // 50 MB
    public const int MaxEntryCount = 500;

    private static readonly HashSet<string> DisallowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".dll", ".vbs", ".ps1", ".scr", ".msi", ".com", ".cpl", ".sys", ".drv"
    };

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxEntryCount)
            throw new InvalidOperationException($"Backup archive contains {archive.Entries.Count} entries, exceeding the maximum safe limit of {MaxEntryCount}.");

        long totalSize = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntry(entry, ref totalSize);
        }
    }

    public static void ValidateEntry(ZipArchiveEntry entry, ref long currentTotalSize)
    {
        string name = entry.FullName.Replace('\\', '/');

        // Path traversal check
        if (name.StartsWith('/') || name.Contains("/../") || name.StartsWith("../") || name.EndsWith("/..") || name == "..")
            throw new InvalidOperationException($"Insecure path traversal detected in archive entry: '{entry.FullName}'.");

        // Windows drive letter check (e.g. C:/)
        if (name.Length >= 2 && name[1] == ':')
            throw new InvalidOperationException($"Absolute drive-letter path detected in archive entry: '{entry.FullName}'.");

        // Disallowed executable extensions
        string ext = Path.GetExtension(name);
        if (!string.IsNullOrEmpty(ext) && DisallowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Executable or script file type '{ext}' is not permitted in backup archives.");

        // Size accumulation and bomb protection
        currentTotalSize += entry.Length;
        if (currentTotalSize > MaxTotalSizeBytes)
            throw new InvalidOperationException($"Archive contents exceed maximum safe uncompressed size limit of {MaxTotalSizeBytes / (1024 * 1024)} MB.");
    }

    public static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeSha256(Stream stream)
    {
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
