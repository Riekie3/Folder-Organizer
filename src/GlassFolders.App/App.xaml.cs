using System.Windows;
using GlassFolders.App.Desktop;
using GlassFolders.App.Poc;

namespace GlassFolders.App;

/// <summary>
/// Phase 1 entry point. Command line:
///   --host=owned | child-defview | child-progman   desktop integration strategy (default owned)
///   --reset                                        forget saved PoC positions
/// </summary>
public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private PocController? _poc;

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

        _poc = new PocController(DesktopHostFactory.Parse(hostArg), reset);
        _poc.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poc?.Dispose();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
