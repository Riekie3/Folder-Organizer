using GlassFolders.Core.Backup;

namespace GlassFolders.Core.Icons;

/// <summary>
/// The app's own copies of pictures chosen as item icons. A picture is only ever copied and decoded as an image;
/// nothing the user picks is run. Only files inside the app's item-icons folder are ever deleted.
/// </summary>
public static class ItemIconFiles
{
    /// <summary>The largest picture accepted for an item icon. A 256 x 256 PNG is a few tens of kilobytes.</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Below this size (in pixels, the longer side) a picture is likely to look soft and the user is warned.</summary>
    public const int MinRecommendedPixels = 128;

    /// <summary>The size the dialog recommends.</summary>
    public const int RecommendedPixels = 256;

    /// <summary>Shown wherever a picture is chosen.</summary>
    public const string RecommendationText = "Recommended: a square PNG, 256 x 256 px, with a transparent background.";

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="directory"/> under a name made from the item id.
    /// Throws <see cref="InvalidDataException"/> for a missing, empty, oversized or unsupported file.
    /// </summary>
    public static string Store(string sourcePath, string itemId, string directory)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new InvalidDataException("The file does not exist.");

        string extension = info.Extension.ToLowerInvariant();
        if (!ZipSafety.IsPictureExtension(extension))
            throw new InvalidDataException($"'{extension}' is not a supported picture type. Use PNG, JPG, BMP, GIF, ICO or TIFF.");
        if (info.Length == 0) throw new InvalidDataException("The file is empty.");
        if (info.Length > MaxBytes)
            throw new InvalidDataException($"The picture is {info.Length / 1024} KB; the limit for an icon is {MaxBytes / (1024 * 1024)} MB.");

        Directory.CreateDirectory(directory);
        string copy = Path.Combine(directory, $"{SafeName(itemId)}-{DateTime.UtcNow.Ticks}{extension}");
        File.Copy(info.FullName, copy, overwrite: false);
        return copy;
    }

    /// <summary>Removes the app's copy of an icon. Files outside <paramref name="directory"/> are never touched.</summary>
    public static void DeleteCopy(string? path, string directory)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A copy that cannot be removed is harmless; it only takes a little disk space.
        }
    }

    private static string SafeName(string id)
    {
        var chars = id.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray();
        return chars.Length == 0 ? "item" : new string(chars);
    }
}
