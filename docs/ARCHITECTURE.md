# GlassFolders — System Architecture

**Version:** 1.0.0  
**Target Platform:** Windows 11 (22H2 / 23H2 / 24H2 / 25H2+)  
**Runtime:** .NET 10.0 (C# 14, WPF, Win32 Interop, Zero Third-Party Dependencies)

---

## 1. High-Level Architecture Overview

GlassFolders is engineered as a zero-idle, high-performance desktop enhancement application that anchors interactive, frosted-glass folder widgets directly onto the Windows 11 desktop layer.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             GlassFolders.App                                │
│  ┌───────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐ │
│  │     PocController     │  │   SettingsWindow      │  │  FolderPopup     │ │
│  │   (Lifecycle & Core)  │  │   (Fluent XAML UI)    │  │  (Acrylic Menu)  │ │
│  └───────────┬───────────┘  └───────────────────────┘  └──────────────────┘ │
│              │                                                              │
│  ┌───────────┴───────────┐  ┌───────────────────────┐  ┌──────────────────┐ │
│  │ ExplorerRecoverySvc   │  │   SingleInstance IPC  │  │   TrayService    │ │
│  │ (State Machine / 8s)  │  │   (Named Mutex/Pipe)  │  │ (Shell_Notify)   │ │
│  └───────────┬───────────┘  └───────────────────────┘  └──────────────────┘ │
└──────────────┼──────────────────────────────────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────────────────────────────────┐
│                            GlassFolders.Native                              │
│  ┌───────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐ │
│  │   DesktopWindows      │  │      DwmHelpers       │  │ DisplayConfig    │ │
│  │ (Progman/DefView Find)│  │ (Acrylic/Mica BT/Dark)│  │ (EnumMonitors)   │ │
│  └───────────────────────┘  └───────────────────────┘  └──────────────────┘ │
└──────────────┬──────────────────────────────────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────────────────────────────────┐
│                             GlassFolders.Core                               │
│  ┌───────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐ │
│  │   FolderManager       │  │     PositionMath      │  │  BackupService   │ │
│  │ (CRUD / Atomic Store) │  │ (Relative Multi-Mon)  │  │ (Safe ZIP / Rel) │ │
│  └───────────────────────┘  └───────────────────────┘  └──────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Solution Layout & Projects

| Project | Target | Purpose |
|---|---|---|
| `GlassFolders.Core` | `net10.0` | Pure, platform-neutral domain models, configuration management, atomic file storage, coordinate math, displacement logic, and ZIP backup/restore. |
| `GlassFolders.Native` | `net10.0-windows` | P/Invoke interop wrappers (`NativeMethods`, `Win32`), DWM backdrop integration (`DwmHelpers`), display enumeration (`DisplayConfigNative`), desktop handle resolution (`DesktopWindows`), and Shell COM interop (`IShellLinkW`). |
| `GlassFolders.App` | `net10.0-windows` (WPF) | Main executable application. Contains desktop surfaces, tile controls, acrylic popups, Settings UI, system tray manager, single-instance IPC, animation service, and recovery loop. |
| `GlassFolders.Core.Tests` | `net10.0` (xUnit) | Comprehensive unit test suite covering configuration persistence, layout math, monitor displacement, ZIP safety, and animation settings. |
| `DesktopProbe` | `net10.0-windows` | Diagnostic CLI tool dumping the active desktop window tree, DefView hierarchies, and z-order. |
| `PocVerifier` | `net10.0-windows` | Acceptance test tool verifying structural and interactive compliance against the desktop layer. |

---

## 3. Desktop Layer Integration (`ProgmanOwnedHost`)

### The Windows 11 Desktop Hierarchy
On modern Windows 11 (24H2 / 25H2+), the desktop window hierarchy is organized as follows:
```
Progman "Program Manager"            [Top-level window, bottom of z-order]
├─ SHELLDLL_DefView                  [Layered surface containing desktop icons]
│  └─ SysListView32 "FolderView"     [The actual icon view control]
└─ WorkerW                           [Spawned wallpaper surface, child of Progman]
```

### The Progman-Owned Architecture
To ensure GlassFolders surfaces appear behind normal applications but above the desktop wallpaper and icons:
1. **Window Style:** Surfaces are created as top-level `WS_POPUP | WS_CLIPCHILDREN` with extended styles `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
2. **Owner Relationship:** The surface HWND's owner is set to `Progman` via `SetWindowLongPtr(GWLP_HWNDPARENT)`. In Win32, owned windows always render in front of their owner.
3. **Z-Order Anchoring:** The surface intercepts `WM_WINDOWPOSCHANGING` and forces `hwndInsertAfter = HWND_BOTTOM`, preventing clicks from elevating the surface above active application windows.
4. **Hit-Testing Transparency:** Transparent regions of folder surfaces pass clicks straight through to Explorer's `SysListView32`, ensuring desktop selection rectangles, context menus, and wallpaper clicks function normally.
5. **Win+D ("Show Desktop") Immunity:** Because surfaces are owned by Progman, Windows treats them as part of the desktop canvas, keeping them visible when the user minimizes all windows via `Win+D`.

---

## 4. Explorer Recovery & Shell Watcher (`ExplorerRecoveryService`)

Desktop integration must gracefully handle shell restarts, graphics driver resets, workstation locking, and system sleep/resume.

```
       ┌────────────────────────┐
       │        Healthy         │◄─────────────────────────────┐
       └───────────┬────────────┘                              │
                   │ Shell Destroyed / TaskbarCreated          │
                   ▼                                           │
       ┌────────────────────────┐                              │
       │       ShellLost        │                              │
       └───────────┬────────────┘                              │
                   │ Enter Recovery Loop                       │
                   ▼                                           │
       ┌────────────────────────┐                              │
       │    WaitingForShell     │                              │
       └───────────┬────────────┘                              │
                   │ Progman Detected (Backoff: 250ms -> 8s)   │
                   ▼                                           │
       ┌────────────────────────┐                              │
       │      Reattaching       │                              │
       └───────────┬────────────┘                              │
                   │ Re-owner / Re-position Surfaces           │
                   ▼                                           │
       ┌────────────────────────┐         Passed               │
       │       Verifying        ├──────────────────────────────┘
       └───────────┬────────────┘
                   │ Failed (Attempts > Max)
                   ▼
       ┌────────────────────────┐
       │         Failed         │
       └────────────────────────┘
```

- **Event Sources:**
  - `SetWinEventHook(EVENT_OBJECT_DESTROY)` scoped to Explorer's PID.
  - `RegisterWindowMessage("TaskbarCreated")` on a dedicated top-level hidden window.
  - `WM_POWERBROADCAST` (Sleep/Resume detection).
  - `WTSRegisterSessionNotification` (Workstation Lock/Unlock detection).
- **Exponential Backoff:** Retries shell discovery at intervals starting at 250 ms, doubling up to 8 seconds, with a maximum 2-minute recovery ceiling.
- **Verification:** Ensures all surfaces exist, have valid HWNDs, maintain valid parent/owner relationships, and respond to input.

---

## 5. Single-Instance Architecture & IPC

- **Mutual Exclusion:** Governed by named mutex `Local\GlassFolders.SingleInstance`.
- **Named Pipe Server:** The primary instance runs an asynchronous named pipe listener on `\\.\pipe\GlassFolders.IpcPipe`.
- **Command Dispatch:** Secondary launches forward arguments (e.g. `SHOW_SETTINGS`, `PAGE:Appearance`) across the pipe, bring the main interface to the foreground, and cleanly exit.
- **Graceful Termination:** Named event wait handle `Local\GlassFolders.Exit` enables graceful IPC-driven shutdowns.

---

## 6. Multi-Monitor Coordinate & Displacement Engine

### Relative Coordinates (`PositionMath`)
Screen positions are stored as normalized fractions $(x_{rel}, y_{rel}) \in [0.0, 1.0]$ relative to each display's work area:
$$x_{rel} = \frac{x_{abs} - \text{WorkArea.Left}}{\text{WorkArea.Width} - \text{TileWidth}}$$
$$y_{rel} = \frac{y_{abs} - \text{WorkArea.Top}}{\text{WorkArea.Height} - \text{TileHeight}}$$

### Displacement Manager (`MonitorDisplacementManager`)
- **Home Monitor Preservation:** Each folder records its assigned `HomeMonitorKey`.
- **Display Disconnection:** When a secondary monitor is unplugged, its assigned folders are temporarily displaced to the primary monitor using collision-avoidance spiral search (`FindNearestFreeCell`).
- **Display Reconnection:** When the disconnected monitor returns, displaced folders automatically restore to their original coordinates on that display without manual user intervention.
- **DPI Changes:** Listens to `WM_DISPLAYCHANGE` and `WM_DPICHANGED` with 300 ms debouncing to avoid redundant recalculations.

---

## 7. DWM Glass & Visual Effects (`DwmHelpers`)

Folder popups and settings dialogs utilize real Windows 11 Desktop Window Manager (DWM) composition:
- **DWM Acrylic (`DWMSBT_TRANSIENTWINDOW`):** Applied via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, 3)`. Produces deep frosted-glass blur with transient lighting.
- **DWM Mica (`DWMSBT_MAINWINDOW`):** Applied via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, 2)`. Blends subtly with desktop background wallpaper.
- **Dark Mode Harmonization:** `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute 20) aligns window non-client borders with system dark/light modes.
- **Rounded Corners:** `DWMWA_WINDOW_CORNER_PREFERENCE` (attribute 33) enforces Fluent `DWMWCP_ROUND` (12px radius).

---

## 8. Animation Control & Zero-Wakeup Architecture

- **Windows Accessibility Compliance:** Queries `SPI_GETCLIENTAREAANIMATION` via `SystemParametersInfoW`. When the user disables "Show animations in Windows", animations automatically shut off.
- **Granular Category Toggles:** 8 independent categories (Popup, Hover, Press, Launch, Dialog, Drag, Theme, Snap) configured via `AnimationSettings`.
- **Zero-Wakeup Snap Gatekeeper:** When animations are disabled, properties snap instantly without allocating or starting WPF `Storyboard` or `DoubleAnimation` timers, preventing compositor wakeups and maintaining 0.00% idle CPU usage.

---

## 9. Portable Backup & Restore Subsystem

- **Archive Format:** Standard ZIP containing `manifest.json`, `folders.json`, and cached icon assets.
- **ZipSafety Hardening:** Blocks ZipSlip path traversal (`..`), path escaping, decompression bombs (>50 MB / >500 entries), and executable payloads (`.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`).
- **Path Relinker Heuristics:** Matches user profiles (`%USERPROFILE%`), `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMFILES%`, and System32 utilities to repair shortcut paths when moving backups between different PCs or Windows usernames.
- **Rollback Snapshots:** Maintains up to 10 rolling pre-action backup archives in `%APPDATA%\GlassFolders\backups\`.

---

## 10. Performance & Resource Management

- **Zero Idle Overhead:** Flat 0.00% CPU utilization at idle certified via `measure-idle.ps1`.
- **Post-Interaction Memory Trimming:** 5 seconds after a folder popup closes, `PerformMemoryTrim()` triggers:
  1. `IconCacheService.TrimMemoryCache()` (releases memory bitmaps while preserving disk cache).
  2. `GC.Collect(2, Aggressive)`.
  3. Win32 `EmptyWorkingSet(ProcessHandle)` returning working set to ~50–65 MB baseline.
- **Leak-Free Handles:** USER and GDI object counts remain constant across drag, click, and popup lifecycles.
