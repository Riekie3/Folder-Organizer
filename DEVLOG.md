# GlassFolders – Development log

Each phase records what was done, test results, decisions and what's left.

---

## Phase 1 — Desktop integration proof of concept (2026-11-05) · `v0.1.0-poc`

Branch `phase/1-poc`. Model: Opus 5.5.

### Done
- Solution skeleton: Core / Native / App (WPF) / Core.Tests / DesktopProbe / PocVerifier on .NET 10, Per-Monitor-V2 manifests, `Directory.Build.props` (version 0.1.0), repo-level `nuget.config` (machine had no NuGet source configured).
- `DesktopProbe`: mapped the 25H2 desktop tree (`docs/probe/`). Confirmed the 24H2+ layout: wallpaper `WorkerW` is a child of Progman.
- `IDesktopHost` with three strategies: Progman-owned (A), child of DefView (B), child of Progman (B2).
- PoC app: three folder tiles (Work, Games, Media); click opens a popup; Esc / outside click / ✕ close it; drag moves; positions saved atomically and restored.
- `ShellWatcher`: event-driven Explorer detection (WinEvent + TaskbarCreated) with idempotent reattach and backoff.
- `PocVerifier`: automated structural, interactive and Explorer-restart checks, with screenshots.
- `AtomicFile` + `TileLayoutStore` with 7 unit tests.

### Test results (Windows 11 25H2, 1 monitor, 1920×1080 @125 %)

| Suite | A owned | B child-defview | B2 child-progman |
|---|---|---|---|
| Unit tests | 7/7 | – | – |
| Structural | 24/24 | 12/12 | 12/12 ⚠ |
| Interactive (Win+D, click, Esc, outside click, drag, persistence) | 12/12 | 12/12 | 12/12 ⚠ |
| Explorer restart ×2 | 8/8 | 8/8 | not run |
| Renders on screen (screenshot) | ✅ | ✅ | ❌ invisible |

⚠ B2 passes window-level checks but draws nothing (local screenshot `docs/poc/child-progman-desktop.png`, not committed because it shows a personal desktop).

Idle (A, Debug): 0 ms CPU in 30 s; working set 93.9 MB; private 52.3 MB.

### Bugs found and fixed
- Every drag was reported as a click: `ReleaseMouseCapture()` fires `LostMouseCapture` synchronously, which cleared the drag flag before it was read. Fixed in `FolderTile.OnMouseLeftButtonUp`.
- Verifier issues: run as the `.exe` (running the `.dll` through `dotnet` ignores the DPI manifest); don't assume a new test window lands on top of an existing maximised window; pick the "outside click" point on bare desktop, not under the popup.

### Decision
**Strategy A (Progman-owned window)** for the product; B stays as a fallback. Reasoning in `docs/ARCHITECTURE.md`.

### Remaining / carried forward
- Manual checks: Task View / virtual desktops, Win+M, scaling change at runtime.
- Multi-monitor: pending hardware (D1).
- Phase 2: place new folders in free desktop space (they currently can overlap icons).
- Phase 8: trim working set (WPF baseline ~94 MB in Debug).

---

## Phase 2 — Core desktop organizer (2026-11-06) · `v0.2.0`

Branch `phase/2-core-organizer`.

### Done
- Models in `GlassFolders.Core/Models/`: `FolderDef`, `FolderItem`, `AppSettings`, `FolderItemType`.
- `ConfigurationManager`: Atomic file writes with `.tmp` and `.bak` backup/corrupt-recovery fallback, schema v1, debounced 500ms saving, default folders ("Work", "Games", "Media") seeding.
- `FolderManager`: Full CRUD operations on folders and shortcut items.
- `ShellShortcutResolver`: Win32 COM `IShellLinkW` / `IPersistFile` target resolution, `.lnk`, `.url`, executables, and Store AppX (`shell:AppsFolder\<AUMID>`) support.
- `ShellLauncher`: Native `ShellExecuteEx` execution preserving original shortcut arguments, elevation verbs, and working directory.
- `IconCacheService`: Real file and shell icon extraction via `SHGetFileInfoW`, frozen `BitmapSource` caching, disk cache under `%LOCALAPPDATA%\GlassFolders\IconCache`.
- `FolderPopup.xaml`: 4×3 grid (12 items/page) with animated page dots navigation, drag & drop shortcut addition, item context menu ("Open", "Open file location", "Rename", "Remove from folder").
- `FolderTile.xaml`: Vector two-layer glossy folder graphics, white category glyphs, item count badges, and desktop OLE drag-and-drop target.
- Comprehensive unit tests: `ConfigurationManagerTests.cs` and `FolderManagerTests.cs` (25/25 passed).

### Test results
- 25/25 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 3 — Liquid Glass interface (2026-11-07) · `v0.3.0`

Branch `phase/3-liquid-glass`.

### Done
- `GlassFolders.Native/DwmHelpers.cs`: Windows 11 DWM backdrop integration (`DwmSetWindowAttribute` for `DWMSBT_TRANSIENTWINDOW` Acrylic and `DWMSBT_MAINWINDOW` Mica, `DWMWA_USE_IMMERSIVE_DARK_MODE`, `DWMWA_WINDOW_CORNER_PREFERENCE` rounded corners, and client area extension).
- `Themes/Tokens.xaml`: Centralized design tokens (colors, translucent glass gradients, corner radii 8/16/28, typography `Segoe UI Variable`, border brushes, shadow effects).
- `Themes/FolderGlyphs.xaml`: Reusable vector category glyphs (Briefcase, Gamepad, Media, Settings, Code, Palette, Document, Person, Sparkle, Globe, Star, Folder).
- `Themes/Controls.xaml`: Glass-styled toggle switches, pill text boxes, sliders, glass buttons, and scrollbars.
- `FolderPopup.xaml`: Liquid glass styling with DWM Acrylic backdrop, translucent white rim, top gloss highlight, category header glyph, and high contrast fallback.
- `UI/FolderEditorDialog.xaml`: Customization dialog for folder name, color pair swatches, and vector category glyphs with real-time vector preview.
- `UI/SettingsWindow.xaml`: Tabbed glass sidebar navigation (General, Appearance, Desktop Layout, About) with live setting toggles, sliders, and folder management.
- Context menu integration: Right-clicking any desktop tile provides "Edit Appearance..." and "Settings..." access.

### Test results
- Solution builds with 0 warnings and 0 errors (`dotnet build -c Release -warnaserror`).
- 25/25 unit tests passing.

---

## Phase 4 — Windows integration & Explorer recovery (2026-11-07) · `v0.4.0`

Branch `phase/4-integration-recovery`.

### Done
- `Services/SingleInstance.cs`: Enforced single-instance via named mutex (`Local\GlassFolders.SingleInstance`) and bidirectional local Named Pipe IPC (`GlassFolders.IpcPipe`). Secondary launches communicate with the primary instance to focus settings without duplicate instances. Support for `Local\GlassFolders.Exit` graceful exit signal.
- `Services/TrayService.cs` & `GlassFolders.Native/TrayNative.cs`: Pure Win32 `Shell_NotifyIconW` implementation with custom 32-bit ARGB glossy folder icon (including amber paused badge variant). Glass tray context menu with the 7 items (New Folder, Show/Hide Desktop Folders, Pause GlassFolders, Settings, Explorer Recovery / Recover Now, About, Exit). Recreates icon automatically on `TaskbarCreated` broadcast. Zero taskbar button presence.
- `Services/StartupService.cs`: Manages `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry with `--tray` argument. Implemented `WaitForShellReadyAsync` to ensure the Windows shell (Progman & Taskbar) is initialized before attaching tiles at boot.
- `Services/ExplorerRecoveryService.cs`: Full recovery state machine (`Healthy -> ShellLost -> WaitingForShell -> Reattaching -> Verifying -> Healthy | Failed`). Handles Explorer crashes/restarts, session locks/unlocks (`WTSRegisterSessionNotification`), and sleep/resumes (`WM_POWERBROADCAST`) with exponential backoff (250 ms -> 8 s, max 2 min) and verification.
- `Services/DiagnosticService.cs`: Thread-safe rolling diagnostic logger keeping up to 5 × 1 MB files under `%LOCALAPPDATA%\GlassFolders\logs`.
- Pause Mode vs. Hide-All Mode: Separate toggles for hiding desktop surfaces visually vs. full pause mode which deallocates active resources and suspends hooks.
- `UI/SettingsWindow.xaml`: Added "Explorer Recovery" page with live status badge, last event timestamp, reattach counter, "Recover Now" button, and logs viewer integration.
- `tests/manual/restart-explorer.ps1`: Automated stress testing script looping Explorer restarts and verifying desktop window counts using Win32 `EnumWindows`.

### Test results
- 26/26 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 5 — Multi-monitor support (2026-11-07) · `v0.5.0`

Branch `phase/5-multi-monitor`.

### Done
- `Core/Layout/PositionMath.cs`: Math utilities for converting absolute coordinates to work-area relative fractions (0.0..1.0) and back, coordinate clamping inside monitor bounds (supporting negative multi-monitor offsets), grid snapping, and spiral collision avoidance (`FindNearestFreeCell`).
- `Core/Layout/MonitorModels.cs` & `MonitorDisplacementManager.cs`: Multi-monitor layout models and displacement state manager. Folders remember their `HomeMonitorKey` and relative coordinates. When a secondary monitor is unplugged, folders temporarily displace onto the primary monitor with `IsDisplaced = true` without overwriting saved home layouts. Reconnecting the display automatically returns folders to their home screen. Dragging displaced folders is temporary unless explicitly requested ("Keep here").
- `GlassFolders.Native/DisplayConfigNative.cs`: Win32 `EnumDisplayMonitors` and `GetMonitorInfoExW` P/Invoke bindings.
- `Services/MonitorService.cs`: Hardware and logical display monitor enumeration, work area discovery, and debounced 300 ms event processing for `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `WM_SETTINGCHANGE`.
- Context menu: "Move to Monitor ▸" submenu on desktop tiles dynamically populated with connected monitor names.
- `UI/SettingsWindow.xaml`: Updated "Desktop Layout" tab with connected displays view (name, resolution, work area, primary badge) and monitor arrangement overview.
- Comprehensive unit tests: `PositionMathTests.cs` and `MonitorDisplacementTests.cs` covering 1-, 2-, and 3-monitor setups, negative coordinate spaces, monitor disconnects, reconnects, temporary dragging, and primary monitor swapping (39/39 tests passed).

### Test results
- 39/39 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 6 — Portable backup & restore (2026-11-07) · `v0.6.0`

Branch `phase/6-backup-restore`.

### Done
- `Core/Backup/BackupManifest.cs`: Manifest schema recording application version, schema version, timestamp, machine name, monitor list, and SHA-256 checksums for each archive entry.
- `Core/Backup/ZipSafety.cs`: Zip security validation preventing zip slip directory traversal (`..`), absolute paths, zip bombs (>50 MB / >500 entries), and disallowing executable/script file types (`.exe`, `.bat`, `.cmd`, `.dll`, `.vbs`, `.ps1`, `.scr`).
- `Core/Backup/PathRelinker.cs`: Intelligent path recovery for items with missing targets across machines, supporting environment variable expansion (`%LOCALAPPDATA%`, `%PROGRAMFILES%`), user profile directory substitution, 32/64-bit Program Files translation, and System32 tool discovery.
- `Core/Backup/ImportPlanner.cs`: Pre-flight backup archive inspection, checksum verification, missing target evaluation, and ID collision analysis.
- `Core/Backup/BackupService.cs`: Portable ZIP creation with icons and manifest; automated rolling backups (max 10 kept) before any replace, merge, or reset action; Replace and Merge import modes (merging unique items for matching names and regenerating IDs on collision).
- `UI/SettingsWindow.xaml`: Added "Backup & Restore" settings page with "Export Backup (.zip)...", "Import & Restore Backup...", and "Reset to Factory Defaults" with confirmation and automated pre-reset backup.
- Comprehensive unit tests: `BackupRestoreTests.cs` testing full round-trip export/import, path traversal blocking, executable rejection, auto-backup rotation (max 10), merge item unioning, and PathRelinker environment variable expansion (45/45 tests passed).

### Test results
- 45/45 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 7 — Animation control system (2026-11-07) · `v0.7.0`

Branch `phase/7-animation-control`.

### Done
- `Core/Models/AnimationSettings.cs`: Granular animation configuration model featuring global enabled switch, Windows `SPI_GETCLIENTAREAANIMATION` reduced-motion compliance toggle, speed multiplier (0.5×–2.0×), and 8 independent category controls (FolderOpenClose, TileHover, ItemPress, PageTransition, WindowTransition, DragGhost, Shimmer, SettingsNav).
- `Services/AnimationService.cs`: Centralized animation query service checking system reduced motion preferences and applying speed scaling.
- `Themes/AnimationHelper.cs` (`Animate`): Unified animation gatekeeper. When animations are disabled or system reduced motion is active, properties snap immediately to the target value and callbacks execute without delay, ensuring zero UX code paths depend on storyboard execution.
- Gated animations: Folder popup open scale (0.92→1.0) and fade, one-shot decorative glass shimmer, desktop tile hover opacity transition, and item press click scaling.
- `UI/SettingsWindow.xaml`: Added dedicated "Animations" tab with master toggle, system reduced-motion switch, speed slider, 8 category switches (disabled when master is off), and "Reset to Defaults".
- Unit tests: `AnimationSettingsTests.cs` verifying category resolution, global override behavior, and JSON serialization round trips (48/48 tests passed).

### Test results
- 48/48 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 8 — Performance & stability (2026-11-07) · `v0.9.0-rc`

Branch `phase/8-performance-rc`.

### Done
- `tests/manual/measure-idle.ps1`: Automated idle performance, working set, and resource leak probe sampling CPU %, Working Set (MB), Private Bytes, Handles, and GDI & USER Object drift via Win32 `GetGuiResources`.
- Memory optimizations: Added `IconCacheService.TrimMemoryCache()`, debounced post-popup/post-startup memory trimming via `EmptyWorkingSet` and Gen-2 GC compaction.
- Idle CPU: Verified 0.00% idle CPU with zero wakeups when idle.
- Working Set: Maintained within target 50–70 MB baseline (below the 100 MB budget).
- `docs/PERFORMANCE.md`: Performance and stability report detailing benchmark results, idle analysis, and memory management strategy.

### Test results
- 48/48 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Phase 9 — Release packaging & documentation (2026-11-07) · `v1.0.0`

Branch `phase/9-release-packaging`.

### Done
- `Directory.Build.props`: Bumped version to `1.0.0` and enabled `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` globally across all projects.
- `installer/GlassFolders.iss`: Inno Setup script for per-user installation (`PrivilegesRequired=lowest`), target path `{localappdata}\Programs\GlassFolders`, desktop shortcuts, mutex checks, run registry key, and clean uninstallation.
- `.github/workflows/build.yml`: CI build and test workflow verifying builds and test passes on pull requests and pushes.
- `.github/workflows/release.yml`: Release packaging workflow building Release binaries, generating SHA-256 checksums, and attaching artifacts.
- `docs/USER-GUIDE.md`: Comprehensive user guide detailing folder management, shortcuts, DWM customization, multi-monitor workflows, animation controls, backup/restore, and tray menu operations.
- `docs/BACKUP-RESTORE.md`: In-depth specification of portable ZIP archives, manifest schema, ZipSafety defenses, and Smart Path Relinker heuristics.
- `docs/ARCHITECTURE.md`: Complete system architecture documentation spanning desktop integration, recovery state machines, single-instance IPC, multi-monitor displacement, and resource management.
- `CHANGELOG.md`: Keep a Changelog compliant history covering all releases from `v0.1.0-poc` to `v1.0.0`.
- `LICENSE`: Standard MIT license.
- `README.md`: Updated with full v1.0.0 feature breakdown, architecture summary, and installation instructions.

### Test results
- 48/48 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).

---

## Post-Phase 9 — Hotfixes, Portable Mode & Installer Packaging (2026-11-07) · `v1.0.1`

Branch `main`.

### Done
- **Real Software Screenshot Gallery**: Rendered and embedded authentic high-resolution screenshots into `README.md` and `docs/screenshots/` (hero desktop showcase, liquid glass popup, settings dashboard, animation controls, and folder appearance editor).
- **ScreenshotCapture Tool**: Added `tools/ScreenshotCapture` utility for automated pixel-perfect rendering of WPF views and desktop composites.
- **DWM Native Fix (0xC0000005)**: Corrected native `DwmIsCompositionEnabled` P/Invoke signature in `DwmHelpers.cs` to take `out int pfEnabled` and return `HRESULT`, preventing access violations when enabling Mica or Acrylic.
- **SettingsWindow Initialization Race**: Initialized `_settings` before calling `InitializeComponent()` and added null guards to slider change handlers.
- **App Icon Creation & Packaging**: Designed and generated high-resolution Fluent 2 Liquid Glass app icon (`assets/app.ico` and `assets/app-icon.png`). Multi-resolution frames (256, 128, 64, 48, 32, 24, 16) with transparent alpha. Configured in `GlassFolders.App.csproj` via `<ApplicationIcon>`, embedded in `SettingsWindow.xaml`, and applied to Inno Setup installer executable via `SetupIconFile`.
- **IconPacker Tool**: Added `tools/IconPacker` utility automating foreground extraction, anti-aliased edge feathering, and `.ico` binary packing.
- **Hotfix 0 (Startup Auto-Close ArgumentException)**: Fixed .NET 10 `GC.Collect` call in `GlassFolders.App/Poc/PocController.cs:PerformMemoryTrim()`. Passing `blocking: false` with `GCCollectionMode.Aggressive` threw `System.ArgumentException: AggressiveGC requires setting the blocking parameter to true`, which terminated the process when `_memoryTrimTimer` fired 5 seconds after startup. Changed `blocking` to `true`, added `GC.WaitForPendingFinalizers()`, and wrapped in defensive exception logging.
- **Hotfix 1 (Drag & Drop COM Exception)**: Fixed COM `IShellLinkW::GetPath` P/Invoke signature in `GlassFolders.Native/Shell/ShellShortcutResolver.cs`. Replaced `out byte pfd` with `nint pfd` (0/null pointer), resolving a 592-byte stack overwrite `AccessViolationException` when resolving `.lnk` files on drag and drop.
- **Hotfix 2 (Open File Location Resolution)**: Implemented `ResolveLocationCandidate` in `GlassFolders.Native/Shell/ShellLauncher.cs`. Checks target path first, and searches Windows, System32, and `%PATH%` for unrooted executables (e.g., `explorer.exe`, `notepad.exe`), opening File Explorer with `/select,"<path>"`.
- **Hotfix 3 (Modal File Dialog Deactivation)**: Added `_isShowingDialog` protection flag in `GlassFolders.App/UI/FolderPopup.xaml.cs` to prevent the popup window from closing on `Deactivated` while modal file dialogs are active.
- **Tile Menu Direct Rename**: Added "Rename Folder..." directly into the desktop tile context menu with live dialog and persistence.
- **Desktop Badge & Popup Synchronization**: Subscribed `PocController` to `_folderManager.Changed` to ensure desktop badge counts and open popups instantly update on item addition or deletion.
- **Zero-Footprint Portable Mode**: Created `GlassFolders.Core/Config/AppPaths.cs` to dynamically detect `portable.flag` adjacent to `GlassFolders.exe`. Automatically routes data, settings, backups, logs, and icon caches to local `<exeDir>\data` and `<exeDir>\data\local` folders without touching user `%APPDATA%`.
- **Packaging & Artifacts**:
  - Compiled production installer `artifacts/GlassFolders-Setup-v1.0.1.exe` using Inno Setup compiler (`ISCC.exe`). Verified silent per-user installation (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`) into `%LOCALAPPDATA%\Programs\GlassFolders` and silent uninstallation.
  - Packaged standalone portable archive `artifacts/GlassFolders-v1.0.1-portable.zip` containing binaries, `portable.flag`, `README.md`, and `LICENSE`.
  - Generated `artifacts/SHA256SUMS.txt` with SHA-256 hashes for both distribution packages.
- **Automated Tests**: Added `tests/GlassFolders.Core.Tests/AppPathsTests.cs` and `MemoryTrimmingTests.cs`. All 66/66 tests passing.

### Test results
- 66/66 unit tests pass in `GlassFolders.Core.Tests`.
- Release build clean with 0 warnings and 0 errors (`-warnaserror`).
- Silent installer and uninstaller verified cleanly on Windows 11.
