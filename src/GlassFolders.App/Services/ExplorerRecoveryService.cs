using System.Windows.Threading;

namespace GlassFolders.App.Services;

public enum RecoveryState
{
    Healthy,
    ShellLost,
    WaitingForShell,
    Reattaching,
    Verifying,
    Failed,
    Paused
}

/// <summary>
/// State machine managing Explorer restarts, shell reattachments, power events, and session changes.
/// Implements exponential backoff (250ms -> 8s, max 2 min) and verification of desktop window attachments.
/// </summary>
public sealed class ExplorerRecoveryService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _stateLock = new();

    private RecoveryState _state = RecoveryState.Healthy;
    private CancellationTokenSource? _recoveryCts;
    private bool _isDisposed;

    public RecoveryState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                DiagnosticService.Instance.LogInfo($"Recovery state changed to: {value}");
                _dispatcher.BeginInvoke(() => StateChanged?.Invoke(value));
            }
        }
    }

    public string LastEventMessage { get; private set; } = "Initialized";
    public DateTime LastEventTime { get; private set; } = DateTime.Now;
    public int ReattachCount { get; private set; }

    public bool IsPaused { get; private set; }
    public bool IsShuttingDown { get; set; }

    public event Action<RecoveryState>? StateChanged;

    /// <summary>
    /// Callback to reattach all desktop windows to the new Progman/Shell.
    /// Must be idempotent (reuses existing windows, avoids duplicates).
    /// </summary>
    public Func<bool>? ReattachCallback { get; set; }

    /// <summary>
    /// Callback to verify all desktop windows have correct owner/parent and are on-screen.
    /// </summary>
    public Func<bool>? VerifyCallback { get; set; }

    public ExplorerRecoveryService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            RecordEvent(paused ? "Paused by user" : "Resumed by user");

            if (paused)
            {
                CancelActiveRecovery();
                State = RecoveryState.Paused;
            }
            else
            {
                State = RecoveryState.Healthy;
                RecoverNow();
            }
        }
    }

    public void RecordEvent(string message)
    {
        LastEventMessage = message;
        LastEventTime = DateTime.Now;
        DiagnosticService.Instance.LogInfo($"[ExplorerRecovery] {message}");
    }

    public void NotifyShellLost()
    {
        if (IsShuttingDown || IsPaused) return;

        lock (_stateLock)
        {
            RecordEvent("Shell lost (Progman destroyed)");
            State = RecoveryState.ShellLost;
            StartRecoveryLoop();
        }
    }

    public void NotifyShellReady()
    {
        if (IsShuttingDown || IsPaused) return;

        lock (_stateLock)
        {
            RecordEvent("Shell ready signal received (TaskbarCreated)");
            StartRecoveryLoop(immediate: true);
        }
    }

    public void NotifySystemResume()
    {
        if (IsShuttingDown || IsPaused) return;

        DiagnosticService.Instance.LogInfo("System resume / session unlock detected. Verifying desktop attachments...");
        RecordEvent("System resume / session unlock");
        VerifyOrRecover();
    }

    public void RecoverNow()
    {
        if (IsShuttingDown) return;

        lock (_stateLock)
        {
            RecordEvent("Manual recovery requested");
            StartRecoveryLoop(immediate: true);
        }
    }

    public void VerifyOrRecover()
    {
        if (IsShuttingDown || IsPaused) return;

        bool verified = false;
        try
        {
            verified = VerifyCallback?.Invoke() ?? true;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogWarn("Verification callback failed", ex);
        }

        if (!verified)
        {
            DiagnosticService.Instance.LogWarn("Desktop verification failed. Initiating recovery...");
            RecoverNow();
        }
        else
        {
            State = RecoveryState.Healthy;
        }
    }

    private void CancelActiveRecovery()
    {
        _recoveryCts?.Cancel();
        _recoveryCts?.Dispose();
        _recoveryCts = null;
    }

    private void StartRecoveryLoop(bool immediate = false)
    {
        CancelActiveRecovery();
        _recoveryCts = new CancellationTokenSource();
        var ct = _recoveryCts.Token;

        Task.Run(async () =>
        {
            try
            {
                State = RecoveryState.WaitingForShell;
                int delayMs = immediate ? 100 : 250;
                int maxDelayMs = 8000;
                DateTime start = DateTime.UtcNow;
                TimeSpan maxTimeout = TimeSpan.FromMinutes(2);

                while (!ct.IsCancellationRequested && (DateTime.UtcNow - start < maxTimeout))
                {
                    if (IsShuttingDown) return;

                    await Task.Delay(delayMs, ct);

                    State = RecoveryState.Reattaching;
                    bool reattachOk = false;

                    await _dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            reattachOk = ReattachCallback?.Invoke() ?? false;
                        }
                        catch (Exception ex)
                        {
                            DiagnosticService.Instance.LogError("Exception in ReattachCallback", ex);
                            reattachOk = false;
                        }
                    });

                    if (reattachOk)
                    {
                        State = RecoveryState.Verifying;
                        bool verifyOk = false;

                        await _dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                verifyOk = VerifyCallback?.Invoke() ?? false;
                            }
                            catch (Exception ex)
                            {
                                DiagnosticService.Instance.LogError("Exception in VerifyCallback", ex);
                                verifyOk = false;
                            }
                        });

                        if (verifyOk)
                        {
                            ReattachCount++;
                            RecordEvent($"Successfully recovered and verified (Attempt count: {ReattachCount})");
                            State = RecoveryState.Healthy;
                            return;
                        }
                    }

                    // Exponential backoff
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                    State = RecoveryState.WaitingForShell;
                }

                if (!ct.IsCancellationRequested)
                {
                    RecordEvent("Recovery timed out after 2 minutes");
                    State = RecoveryState.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Fatal error in recovery state machine", ex);
                State = RecoveryState.Failed;
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        IsShuttingDown = true;
        CancelActiveRecovery();
    }
}
