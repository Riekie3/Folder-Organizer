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
            MessageBox.Show($"GlassFolders failed to start:\n\n{ex.Message}\n\nSee %LOCALAPPDATA%\\GlassFolders\\logs for details.",
                "GlassFolders Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poc?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
