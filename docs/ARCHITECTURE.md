# GlassFolders – Architecture

_Living document. Phase 1 covers desktop integration; later phases add sections._

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `src/GlassFolders.Core` | `net10.0` | Platform-neutral logic: models, storage, layout. Fully unit-testable. |
| `src/GlassFolders.Native` | `net10.0-windows` | Win32 P/Invoke (`NativeMethods`), helpers (`Win32`), Explorer desktop handles (`DesktopWindows`). |
| `src/GlassFolders.App` | `net10.0-windows` WPF | The app. `Desktop/` holds the desktop-integration layer. |
| `tests/GlassFolders.Core.Tests` | xUnit | Unit tests for Core. |
| `tools/DesktopProbe` | console | Dumps Explorer's desktop window tree for the current Windows build. |
| `tools/PocVerifier` | console | Automated desktop-integration checks (structural, interactive, Explorer restart). |

All processes are **Per-Monitor-V2 DPI aware** (app manifest). All desktop coordinates in the integration layer are physical screen pixels.

## Desktop integration

### What the Windows 11 25H2 desktop looks like

Captured with `DesktopProbe` (raw output in `docs/probe/`):

```
Progman "Program Manager"            top-level, bottom of z-order, spans the virtual screen
├─ SHELLDLL_DefView                  WS_EX_LAYERED on 25H2
│  └─ SysListView32 "FolderView"     the desktop icons
└─ WorkerW                           only after message 0x052C; wallpaper layer, BELOW DefView
```

Since 24H2 the wallpaper `WorkerW` is a **child of Progman**. On older builds it was a separate top-level window and `SHELLDLL_DefView` could live inside a top-level `WorkerW`. `DesktopWindows.Find()` handles both layouts.

### Strategies evaluated

All strategies sit behind `IDesktopHost`, so the rest of the app never touches Explorer windows directly.

| | A. Owned by Progman (`ProgmanOwnedHost`) | B. Child of DefView (`DesktopChildHost`) | B2. Child of Progman |
|---|---|---|---|
| How | Top-level `WS_POPUP`, owner set to Progman (`GWLP_HWNDPARENT`), `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, z-order pinned with `WM_WINDOWPOSCHANGING` → `HWND_BOTTOM`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` | `WS_CHILD` of `SHELLDLL_DefView`, above the icon list view | `WS_CHILD` of Progman, above DefView |
| Renders | ✅ | ✅ | ❌ **invisible** (DefView's layered surface covers it) but still receives clicks |
| Structural checks | 24/24 | 12/12 | 12/12 (false positive) |
| Interactive checks | 12/12 | 12/12 | 12/12 (false positive) |
| Explorer restart ×2 | 8/8: windows **survive**, re-owned in ~0.75 s | 8/8: windows **destroyed**, recreated | not run |
| Input coupling | Independent of Explorer | Cross-process parent/child attaches our input queue to Explorer's: a hang in either can stall the other | same as B |
| DPI | Normal top-level windows: WPF receives `WM_DPICHANGED` per monitor | Child windows don't get `WM_DPICHANGED`; per-monitor scaling must be done manually | same as B |
| Never above apps | By owner relationship + z-order guard | Inherent | Inherent |

### Decision: Strategy A (Progman-owned), with B kept as a fallback

A passed every automated check on 25H2. It also keeps our windows independent of Explorer's process, survives Explorer restarts without losing state, and lets WPF handle per-monitor DPI normally. B passes too and stays in the codebase as a fallback (`--host=child-defview`) in case a future Windows build breaks owner-based layering. B2 is rejected: invisible but clickable is the worst possible failure mode.

How A behaves:

- **Show desktop (Win+D):** folders stay visible and clickable, because owned windows move with their owner.
- **Normal app windows:** always cover the folders. A click on a folder does not bring it forward (no-activate + z-order guard).
- **Taskbar / Alt-Tab:** never listed (tool window, no `WS_EX_APPWINDOW`).
- **Desktop icons:** everywhere outside the folder rectangles, hit-testing still returns Explorer's `SysListView32`, so icons, rubber-band select and the desktop context menu keep working.
- **Explorer restart:** the windows outlive Progman. `ShellWatcher` hears either the WinEvent `EVENT_OBJECT_DESTROY` for Progman or the `TaskbarCreated` broadcast, waits for a new Progman + list view (exponential backoff 250 ms → 8 s, 2 min cap), and re-owns every window. Recreates any that died. Idempotent: keyed by folder ID, it never makes duplicates.

### Shell event sources (no polling)

| Event | Mechanism |
|---|---|
| Explorer's desktop destroyed | `SetWinEventHook(EVENT_OBJECT_DESTROY)` scoped to Explorer's PID, filtered to the Progman HWND |
| Shell (re)created | `RegisterWindowMessage("TaskbarCreated")` on a hidden **top-level** window (broadcasts skip message-only windows) |
| Surface destroyed by someone else | `HwndSource.Disposed` → `DesktopSurface.Lost` |
| Display / DPI / work-area changes | `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE` on the same hidden window (used from Phase 5) |

`ShellWatcher` is the Phase 1 minimum; Phase 4 replaces it with the full `ExplorerRecoveryService` state machine.

## Storage

`Core/Storage/AtomicFile`: write to `<file>.tmp` with write-through, then `File.Replace` keeping `<file>.bak`. Readers validate and fall back to `.bak` if the main file is corrupt.

| Data | Location |
|---|---|
| Settings and layouts | `%APPDATA%\GlassFolders\` |
| Logs, caches, PoC state | `%LOCALAPPDATA%\GlassFolders\` |

## Measurements (Phase 1 PoC, Debug build, 3 folders)

| Metric | Value |
|---|---|
| CPU while idle | 0 ms over 30 s |
| Working set | 93.9 MB |
| Private bytes | 52.3 MB |
| Threads / handles | 13 / 558 |
| Explorer death → folders re-owned | ~0.75 s |
