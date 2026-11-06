using System.IO;
using System.Windows;
using GlassFolders.App.Desktop;
using GlassFolders.App.Poc;

namespace GlassFolders.App;

/// <summary>
/// Application entry point. Command line:
///   --host=owned | child-defview | child-progman   desktop integration strategy (default owned)
///   --reset                                        forget saved positions
/// </summary>
public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private PocController? _poc;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(true, @"Local\GlassFolders.Poc.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        string? hostArg = e.Args.FirstOrDefault(a => a.StartsWith("--host="))?["--host=".Length..];
        bool reset = e.Args.Contains("--reset");

        // Lets tools (and later the installer) ask a running instance to exit cleanly.
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GlassFolders.Poc.Exit");
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, Timeout.Infinite, true);

        try
        {
            _poc = new PocController(DesktopHostFactory.Parse(hostArg), reset);
            _poc.Start();
        }
        catch (Exception ex)
        {
            LogCrash("StartupException", ex);
            MessageBox.Show($"GlassFolders failed to start:\n\n{ex.Message}\n\nSee %LOCALAPPDATA%\\GlassFolders\\poc\\crash.log for details.",
                "GlassFolders Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poc?.Dispose();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(string context, Exception? ex)
    {
        if (ex is null) return;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassFolders", "poc");
        try
        {
            Directory.CreateDirectory(dir);
            string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}: {ex}\n";
            File.AppendAllText(Path.Combine(dir, "crash.log"), log);
            File.AppendAllText(Path.Combine(dir, "poc.log"), log);
        }
        catch { }
    }
}
