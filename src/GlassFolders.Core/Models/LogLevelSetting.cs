namespace GlassFolders.Core.Models;

/// <summary>
/// The log level names stored in settings. Settings files written by hand or by an older build may hold anything,
/// so unknown or empty names become Info rather than silencing the log.
/// </summary>
public static class LogLevelSetting
{
    public const string Error = "Error";
    public const string Warning = "Warning";
    public const string Info = "Info";
    public const string Debug = "Debug";

    public static string Normalize(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "debug" or "dbg" or "verbose" or "trace" => Debug,
        "warning" or "warn" or "wrn" => Warning,
        "error" or "err" or "errors" => Error,
        _ => Info,
    };
}
