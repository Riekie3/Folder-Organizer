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
    public const long MaxPictureBytes = 10 * 1024 * 1024; // 10 MB, same as Change Icon
    public const int MaxEntryCount = 500;
    public const long MaxCompressionRatio = 100;

    private static readonly HashSet<string> DisallowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".dll", ".vbs", ".ps1", ".scr", ".msi", ".com", ".cpl", ".sys", ".drv"
    };

    private static readonly HashSet<string> AllowedRootEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "manifest.json", "folders.json", "settings.json"
    };

    private static readonly HashSet<string> AllowedIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".ico"
    };

    private static readonly HashSet<string> AllowedPictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff"
    };

    public static bool IsPictureExtension(string extension) => AllowedPictureExtensions.Contains(extension);

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

        // Backup archives hold configuration and pictures only. Anything else is either a mistake or a payload.
        if (!IsAllowedEntry(name))
            throw new InvalidOperationException($"Unexpected file '{entry.FullName}' is not permitted in backup archives.");

        // Size accumulation and bomb protection
        currentTotalSize += entry.Length;
        if (currentTotalSize > MaxTotalSizeBytes)
            throw new InvalidOperationException($"Archive contents exceed maximum safe uncompressed size limit of {MaxTotalSizeBytes / (1024 * 1024)} MB.");

        if (entry.Length > 0)
        {
            if (entry.CompressedLength <= 0)
                throw new InvalidOperationException($"Archive entry '{entry.FullName}' claims contents but has no compressed size.");

            if (entry.Length > entry.CompressedLength * MaxCompressionRatio)
                throw new InvalidOperationException($"Archive entry '{entry.FullName}' expands more than {MaxCompressionRatio}:1 and is treated as a decompression bomb.");
        }
    }

    private static bool IsAllowedEntry(string name)
    {
        if (AllowedRootEntries.Contains(name)) return true;
        if (name is "icons/" or "folder-icons/" or "item-icons/") return true;

        return IsFileIn(name, "icons/", AllowedIconExtensions)
            || IsFileIn(name, "folder-icons/", AllowedPictureExtensions)
            || IsFileIn(name, "item-icons/", AllowedPictureExtensions);
    }

    private static bool IsFileIn(string name, string folder, HashSet<string> extensions) =>
        name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && name.LastIndexOf('/') == folder.Length - 1
        && extensions.Contains(Path.GetExtension(name));

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

    /// <summary>
    /// Reads an entry, never allocating more than <paramref name="maxBytes"/>. The declared length can lie
    /// in a hand-made archive, so the limit is enforced while reading too.
    /// </summary>
    public static byte[] ReadAllBytes(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
            throw new InvalidOperationException($"Archive entry '{entry.FullName}' is larger than the {maxBytes / (1024 * 1024)} MB limit.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
        byte[] chunk = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"Archive entry '{entry.FullName}' expands past the {maxBytes / (1024 * 1024)} MB limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
