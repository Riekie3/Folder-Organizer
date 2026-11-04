using System.Text;

namespace GlassFolders.Core.Storage;

/// <summary>
/// Crash-safe file writes: write to a temp file in the same folder, flush to disk, then swap it in.
/// The previous version is kept as "&lt;name&gt;.bak" so a corrupted main file can be recovered.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        string bak = path + ".bak";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = new UTF8Encoding(false).GetBytes(contents);
            fs.Write(bytes);
            fs.Flush(true);
        }

        if (File.Exists(path))
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }

    /// <summary>Reads the main file; if it is missing or <paramref name="isValid"/> rejects it, falls back to the .bak copy.</summary>
    public static string? ReadWithFallback(string path, Func<string, bool> isValid)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string text = File.ReadAllText(candidate);
                if (isValid(text)) return text;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}
