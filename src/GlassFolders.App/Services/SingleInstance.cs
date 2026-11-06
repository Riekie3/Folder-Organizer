using System.IO;
using System.IO.Pipes;
using System.Text;

namespace GlassFolders.App.Services;

/// <summary>
/// Enforces single-instance execution via a named Mutex and communicates between
/// secondary launches and the primary instance using a local Named Pipe.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\GlassFolders.SingleInstance";
    private const string ExitEventName = @"Local\GlassFolders.Exit";
    private const string PipeName = "GlassFolders.IpcPipe";

    private Mutex? _mutex;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private CancellationTokenSource? _pipeCts;
    private bool _isDisposed;

    public bool IsFirstInstance { get; private set; }

    public event Action<string>? CommandReceived;
    public event Action? ExitRequested;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;

        if (IsFirstInstance)
        {
            _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) =>
            {
                ExitRequested?.Invoke();
            }, null, Timeout.Infinite, true);

            StartPipeServer();
        }
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? command = await reader.ReadLineAsync(ct);
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        CommandReceived?.Invoke(command.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    DiagnosticService.Instance.LogWarn("IPC pipe server error", ex);
                    try { await Task.Delay(500, ct); } catch { break; }
                }
            }
        }, ct);
    }

    /// <summary>
    /// Sends a command to the primary instance from a secondary launch.
    /// </summary>
    public static async Task<bool> SendCommandAsync(string command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(cts.Token);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            await writer.WriteLineAsync(command.AsMemory(), cts.Token);
            await writer.FlushAsync(cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogWarn("Failed to send command to primary instance via IPC pipe", ex);
            return false;
        }
    }

    /// <summary>
    /// Signals the running instance to exit via the named event.
    /// </summary>
    public static void SignalExit()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ExitEventName);
            handle.Set();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _pipeCts?.Cancel();
        _pipeCts?.Dispose();

        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();

        if (IsFirstInstance && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
    }
}
