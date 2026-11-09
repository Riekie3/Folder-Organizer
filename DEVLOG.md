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
- **Publisher Branding & Website Integration**: Updated all official metadata, About screen, sidebar links, installer attributes, and documentation to credit **[Rie-Sources](https://rie-sources.com/)**. Added interactive links and stylized publisher card into `SettingsWindow` About tab and sidebar, updated `<Company>`, `<Authors>`, `<Copyright>`, and `<PackageProjectUrl>` in `Directory.Build.props`, updated `installer/GlassFolders.iss` (`#define MyAppPublisher "Rie-Sources"`, `#define MyAppURL "https://rie-sources.com/"`), updated `LICENSE`, and added dedicated sections to `README.md` and `docs/USER-GUIDE.md`.
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

## Stage 0 - Safe base (branch `stage/0-safe-base`)

- Startup: `_loadingUi` guard in Settings, `IsRealApp` check and `RepairStartupPath()` in `StartupService`.
- `ScreenshotCapture`: sandboxed config, `--tray` ignored, repo-relative output, About page navigation fixed.
- Version 1.0.2 (props, installer); version text read from assembly; release zip name uses the tag.
- README corrected. Not released: waiting for confirmation.

## Stage 4 - Settings, menus, dialogs (branch `stage/4-settings-menus-dialogs`)

- Core: `AppSettings` gains `Theme`, `GlassTransparency`, `GlassBlur`, `GlassBorder`, `GlassShadow`, `CornerRadius`, `PopupIconSize`. `GlassAppearance` turns them into tint strength, blur radius, rim and shadow opacity, corner radius and icon size; its defaults reproduce the Stage 3 look exactly. `LogLevelSetting` normalises the saved level name. `GlassTint.ForBackdrop` takes a strength, `GlassTint.ForLightPanel` is the light theme's tint, and `BackdropAnalyzer` also reports the 10th percentile. 46 new unit tests (231 total).
- Strength above 1 first made the tint paler, which white text can't sit on (contrast 2.2:1 at "nearly solid"). It now moves toward deeper blue as it grows, and is eased back if even a full scrim couldn't reach 3:1. The test over every transparency setting caught this.
- `AppearanceService` swaps `ThemeDark.xaml` or `ThemeLight.xaml` (same `Chrome*` keys, used through `DynamicResource`) and listens to Windows' colour mode. `Controls.xaml` was rewritten: buttons, toggle, slider, segmented control, sidebar item, pill text box, scroll bar, menus, tooltips. The enum is `AppTheme`, because .NET now has `System.Windows.ThemeMode`.
- `GlassWindow` is the base of Settings, the folder editor and the message boxes. It centres the window on a monitor, captures that whole monitor once before showing, and `GlassCard` slides the image as the window is dragged (the plan's "monitor-wide snapshot, shifted with the window"). Off its monitor the card shows the solid panel. A copy at half the final size is kept, in memory only, so the Blur slider can re-filter without capturing while the window covers the screen. Re-blur is a second `FrostFilter` pass over that copy, with the same output size as one pass.
- Radio buttons and toggles that start checked raise `Checked` while XAML is still loading; guarded with a `_built` flag after two null-reference crashes in the harness.
- `GlassMessageBox` and `GlassPromptDialog` replace the 10 `MessageBox` calls and the plain rename window. The startup-failure box falls back to the plain one if the styled box itself can't load.
- The popup reads the Appearance settings when it is made. The log level and the missing-target badge settings were saved but never read; both are now connected.

Tested off-screen over Windows' bloom wallpapers (light and dark), and on the live desktop with a sandbox copy after warning the user. No real desktop captures are kept or committed.

| Check | Result |
|---|---|
| Every Settings page, folder editor, message box, rename, folder menu; light and dark | Rendered and read. Fixes: solid red Delete button, pale slider fill in dark mode, menu icon column closes when empty |
| Sandbox app on the live desktop, light theme and Debug log level | Settings opened centred with glass in 53 ms; debug line written; exited cleanly through the exit event |
| Real popup on the live desktop in three Appearance settings | Corner radius, border, shadow, tint, blur, icon size and the missing badge each changed as set |

Not tested: the sliders and menus with the real mouse, the Windows light/dark switch while running, dragging Settings onto a second monitor, 100 % and 150 % scaling, high contrast. Stage 5 covers these.

## Stage 3 - Desktop folders match (branch `stage/3-desktop-folders`)

- `DesktopGrid`, `FolderTileMetrics` in `GlassFolders.Core/Layout`; `WallpaperMapping`, `LabelContrast` in `Core/Glass`; `FolderPalette` in `Core/Models`. 33 new unit tests (185 total).
- `DesktopIconGrid` (Native) reads the desktop through `IShellWindows`, `IShellBrowser` and `IFolderView`/`IFolderView2`, all documented and read-only. An item's position is its icon's top-left, so each cell's edge comes from the icon size and the desktop's DPI. Asking the view for `IOleWindow` or `IShellView` across processes failed, so the list view's window comes from `DesktopWindows` instead.
- `DesktopWallpaper` (Native) reads `IDesktopWallpaper`: the picture, the fit and the background colour for each monitor. `WallpaperService` decodes the picture once at 640 px wide, blurs the part under each plate with `FrostFilter`, and measures the brightness under the label. The decoded copy is dropped at the next memory trim.
- `FolderTile` rewritten around `FolderIconView`: plate (halo, glow, wallpaper glass, cyan veil, rim) and a box of art and label. The box has an almost invisible fill, so the whole box takes the mouse as a desktop icon's cell does. `OutlinedText` draws the label with a real outline; a blurred copy behind the text was too faint.
- `FolderIconView` gains the Glass style (traced from reference 4) and custom pictures.
- `PocController`: grid placement, a one-time move of old saved positions that keeps each centre, snapping on drag, size changes that keep the centre, backdrop refresh on wallpaper and display changes, and the brief's folder menu, built each time it opens.
- Tray icon made from the app icon's small frames at the tray's size for the display scale. New 16, 20, 24 and 32 px frames; 48 px and up keep the existing art.
- Folders schema 2 (old preset colours), layout schema 2 (window size). The settings log level isn't applied by `DiagnosticService` at all, so debug lines never print; left for Stage 4.

Tested off-screen against the reference, and on the live desktop with a sandbox copy (renamed exe, portable config) after warning the user:

| Check | Result |
|---|---|
| Overlay on the reference | Art 1 px, plate 3 px, labels 2 px at most |
| Solid and Glass, light and dark bloom | Labels white on the wallpaper; dark on the plate over light wallpaper only |
| Dry run against the desktop's 65 icons | 6 folders, all in free cells, none touching an icon |
| Sandbox app on the live desktop | 4 sample folders placed in column 6, rows 2–5, all attached; 6.9 MB working set after trim; clean exit through the exit event |
| Layout from version 1 | Moved once; (100, 100) became (70, 73), keeping the centre |
| Desktop grid read | About 30 ms |
| Real wallpaper (ASUS ROG, Fill) under 4 plates | 190 ms for the first, then about 2 ms each; label luminance 0.00–0.08 |
| Startup entry | Unchanged by the sandbox runs |

Not tested: hovering, opening and right-clicking a folder on the live desktop with the real mouse, Change Icon with a picture, drag snapping, other display scales, a second monitor. Stage 5 covers the live checks.

## Stage 2 - Popup matches the reference (branch `stage/2-popup-parity`)

- `PopupLayout` in `GlassFolders.Core/Popup`: grid, sizes, paging and placement as pure code. `IconAnalysis` in `Core/Icons`: alpha type, solid area, edge softness. 48 new unit tests (152 total).
- `ShellImage`: 256 px icons through `IShellItemImageFactory`, read as BGRA with `GetDIBits`, because the usual HBITMAP helper drops alpha. 76 real icons checked on a contact sheet.
- The shell returns both straight and premultiplied alpha, so each icon is checked. It also upscales or pads apps with small icons. Padded art is cropped, and soft edges (5 px or wider, against 3 or less for real 256 px art) mark an icon as low resolution, which gets the tile.
- `IconCacheService`: reading from the shell took 7–60 ms per icon, with spikes to 1.3 s on a cold disk, so it now runs on a background STA thread. Disk hits stay synchronous. PNGs hold the originals; memory holds display-size copies.
- `FolderPopup` rewritten. The window now covers the folder as well as the card, so the card can grow out of it. Its empty parts let clicks through.
- The snapshot covers the expanded card when the folder has more than one page, since a popup can't capture itself. Overscan dropped from 64 to 24 DIP, and shrinking reads every second pixel from a downsample of 6 up.
- Item menus are built on first open instead of for every item on every page.
- Found while testing: `Window.DpiChanged` also receives the event every `Image` raises, so each icon re-ran the popup's layout and cancelled the expand animation. It now checks the source.
- Found while testing: saved animation settings were only applied after opening Settings. They now load at startup.
- Height and top animate per frame through `CompositionTarget.Rendering`, which unsubscribes when done.

Tested with a throwaway harness on sandbox config, over a full-screen test window (1920×1080 at 125 %, Release), using real mouse and keyboard input through Windows:

| Check | Result |
|---|---|
| Overlay on the reference | Largest difference 8 px, 0.4 % of screen width |
| Capture + frost, median of 30 | One page: 30 ms, min 10, max 40. Expandable: 36 ms, min 11, max 41 |
| Construct + show, 6 opens with icons in memory | Median 123 ms, min 44, max 163 |
| Open animation frames | Median gap 16 ms, 1–2 gaps over 33 ms in each of 3 opens, worst 82 ms |
| Expand animation frames | Reaches 712 in about 190 ms. Gaps up to 43 ms in the first frames |
| CPU with the popup open | 0–16 ms over 5 s; nothing runs after the animation |
| Wheel, arrows, End, dots, expand, collapse | Change as expected; expand keeps the first visible item on screen |
| ⋯ menu, item menu, Escape on a menu | Popup stays open |
| Click in the gap, on the shadow, on close, Escape | Popup closes |
| Hit test: card / folder / gap | Popup / passes through / passes through |
| Corner anchors, 5×3, 6×5 | Card stays in the work area; 6×5 scales to fit |
| 0, 2 and 3 items | Card draws one row, expand disabled, grows when items arrive |
| Animation off | Opens at full opacity, closes at once |
| Close during the open animation | Closes cleanly |

Not tested: a click on a real desktop folder, other display scales, a second monitor, real high contrast and transparency settings, a drag and drop from Explorer, and memory in the real app. Stage 5 covers these. Drop handlers are unchanged apart from the layout refresh.

## Stage 1 - Glass engine (branch `stage/1-glass-engine`)

- `FrostFilter`, `GlassTint`, `BackdropAnalyzer` in `GlassFolders.Core/Glass`: pure image maths, 38 new unit tests (104 total).
- `ScreenCapture` (GDI, native memory only), `BackdropSnapshot`, `GlassCard`. `FolderPopup` positions and captures in `SourceInitialized`, while still hidden.
- `EnableAcrylic` and `EnableMica` removed from every layered window, then deleted as unused.
- First version captured the whole monitor, so the card could move or resize freely. Measured 16–51 ms for `BitBlt` alone, over budget. Time scales with area, so it now captures the card plus 64 DIP. Stage 4's Settings glass can still use a monitor-wide capture, because it doesn't need to open as fast.

Tested with a throwaway harness that opens the real `FolderPopup` on sandbox config over full-screen test windows (1920×1080 at 125 %, Release):

| Check | Result |
|---|---|
| Corners and square block, on bloom and the dev PC's dark wallpaper | Smooth, no block (by eye from screen captures) |
| Capture + frost during 6 real popup opens | 8–18 ms |
| Capture + frost, repeated every 120 ms | Median 20 ms, min 8, max 58. Spikes over the 30 ms target; CPU power state is the likely cause, not confirmed |
| Same at Stage 2's card size (1240×830 with overscan) | Median 37 ms, min 12, max 41. Over target: revisit in Stage 2 |
| Popup reopened right after another closed | Glass identical to a clean capture |
| CPU over 5 s with popup open / after close | 0–31 ms / 16 ms |
| Solid panel when the backdrop is null or all black | Shown |
| White, yellow, near-black backdrops | Text readable, tint adapts |
| Card kept inside work area from a corner anchor | Yes |
| Empty folder grows when an item is added | Glass stays aligned |

Not tested: a click on a real desktop folder (running `GlassFolders.exe` would rewrite the startup entry to a dev path), the real high contrast and transparency settings (only the solid panel they lead to), other scaling levels, a second monitor, memory in the real app. The shadow click was simulated with a routed event. Stage 5 covers the live desktop.
