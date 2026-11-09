using System.IO;
using System.Windows;
using GlassFolders.App.Desktop;
using GlassFolders.App.Poc;
using GlassFolders.App.Services;

namespace GlassFolders.App;

/// <summary>
/// Application entry point and lifecycle manager.
/// Supports SingleInstance IPC, system tray mode, and graceful shutdown.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private PocController? _poc;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            DiagnosticService.Instance.LogError("DispatcherUnhandledException", e.Exception);
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            DiagnosticService.Instance.LogError("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // Forward command to the running instance and exit
            await SingleInstance.SendCommandAsync("SHOW_SETTINGS");
            Shutdown();
            return;
        }

        // Wire IPC command listener
        _singleInstance.CommandReceived += cmd =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (cmd == "SHOW_SETTINGS")
                {
                    _poc?.OpenSettings();
                }
                else if (cmd.StartsWith("PAGE:"))
                {
                    _poc?.OpenSettings(cmd["PAGE:".Length..]);
                }
            });
        };

        _singleInstance.ExitRequested += () =>
        {
            Dispatcher.BeginInvoke(() => Shutdown());
        };

        StartupService.RepairStartupPath();

        bool isTrayMode = e.Args.Contains("--tray");
        string? hostArg = e.Args.FirstOrDefault(a => a.StartsWith("--host="))?["--host=".Length..];
        bool reset = e.Args.Contains("--reset");

        if (isTrayMode)
        {
            DiagnosticService.Instance.LogInfo("GlassFolders started in --tray mode. Waiting for Windows shell...");
            await StartupService.WaitForShellReadyAsync();
        }

        try
        {
            _poc = new PocController(DesktopHostFactory.Parse(hostArg), reset);
            _poc.Start();
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("StartupException", ex);
            ShowStartupError(ex);
            Shutdown();
        }
    }

    private static void ShowStartupError(Exception ex)
    {
        string text = $"GlassFolders could not start.\n\n{ex.Message}\n\nThe log in %LOCALAPPDATA%\\GlassFolders\\logs has the details.";
        try
        {
            UI.GlassMessageBox.Show(null, "GlassFolders", text, kind: UI.GlassMessageKind.Error);
        }
        catch (Exception boxEx)
        {
            // The styled box needs the app's resources; if those are the problem, the plain one still tells the user.
            DiagnosticService.Instance.LogError("Could not show the startup error on glass", boxEx);
            MessageBox.Show(text, "GlassFolders", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppearanceService.Instance.Stop();
        _poc?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
