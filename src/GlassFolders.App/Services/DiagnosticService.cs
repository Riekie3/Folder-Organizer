using System.IO;
using GlassFolders.Core.Config;

namespace GlassFolders.App.Services;

public enum DiagnosticLogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Thread-safe rolling diagnostic logging service.
/// Keeps up to 5 rolling files of 1 MB each in logs directory.
/// Supports standard local appdata as well as portable mode.
/// </summary>
public sealed class DiagnosticService
{
    private static readonly Lazy<DiagnosticService> _instance = new(() => new DiagnosticService());
    public static DiagnosticService Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _logDir;
    private readonly string _mainLogFile;
    private const long MaxFileSizeBytes = 1024 * 1024; // 1 MB
    private const int MaxRollingFiles = 5;

    public DiagnosticLogLevel CurrentLogLevel { get; set; } = DiagnosticLogLevel.Info;

    /// <summary>Turns the level name saved in settings into a level. Unknown names mean Info.</summary>
    public static DiagnosticLogLevel LevelFor(string? settingName) =>
        Enum.Parse<DiagnosticLogLevel>(GlassFolders.Core.Models.LogLevelSetting.Normalize(settingName));
    public string LogDirectory => _logDir;

    public DiagnosticService()
    {
        _logDir = AppPaths.LogsDir;
        _mainLogFile = Path.Combine(_logDir, "glassfolders.log");
        try
        {
            Directory.CreateDirectory(_logDir);
        }
        catch { }
    }

    public void Log(DiagnosticLogLevel level, string message, Exception? ex = null)
    {
        if (level < CurrentLogLevel) return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string levelStr = level switch
        {
            DiagnosticLogLevel.Debug => "DBG",
            DiagnosticLogLevel.Info => "INF",
            DiagnosticLogLevel.Warning => "WRN",
            DiagnosticLogLevel.Error => "ERR",
            _ => "INF"
        };

        string line = $"[{timestamp}] [{levelStr}] {message}";
        if (ex != null)
        {
            line += Environment.NewLine + ex;
        }
        line += Environment.NewLine;

        lock (_lock)
        {
            try
            {
                RollIfNecessary();
                File.AppendAllText(_mainLogFile, line);
            }
            catch { }
        }
    }

    public void LogDebug(string message) => Log(DiagnosticLogLevel.Debug, message);
    public void LogInfo(string message) => Log(DiagnosticLogLevel.Info, message);
    public void LogWarn(string message, Exception? ex = null) => Log(DiagnosticLogLevel.Warning, message, ex);
    public void LogError(string message, Exception? ex = null) => Log(DiagnosticLogLevel.Error, message, ex);

    private void RollIfNecessary()
    {
        try
        {
            if (!File.Exists(_mainLogFile)) return;

            var info = new FileInfo(_mainLogFile);
            if (info.Length < MaxFileSizeBytes) return;

            // Delete oldest if exists
            string oldest = Path.Combine(_logDir, $"glassfolders.{MaxRollingFiles - 1}.log");
            if (File.Exists(oldest))
                File.Delete(oldest);

            // Shift older files
            for (int i = MaxRollingFiles - 2; i >= 1; i--)
            {
                string cur = Path.Combine(_logDir, $"glassfolders.{i}.log");
                string next = Path.Combine(_logDir, $"glassfolders.{i + 1}.log");
                if (File.Exists(cur))
                    File.Move(cur, next, true);
            }

            // Move main to .1
            string first = Path.Combine(_logDir, "glassfolders.1.log");
            File.Move(_mainLogFile, first, true);
        }
        catch { }
    }
}
