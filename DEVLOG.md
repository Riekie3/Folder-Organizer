# GlassFolders – Development log

Newest first. Each phase records what was done, test results, decisions and what's left.

---

## Phase 2 — Core desktop organizer (2026-11-07) · `v0.2.0`

Branch `phase/2-core-organizer`.

### Done
- Core domain models: `FolderDef`, `FolderItem`, `AppSettings`, `FoldersConfiguration` with AOT/trim-compatible System.Text.Json source generation (`ConfigurationJsonContext`).
- `ConfigurationManager`: handles atomic loading/saving of `%APPDATA%\GlassFolders\folders.json` and `settings.json`, automatic creation of default folders (Work, Games, Media), and corrupted file recovery via `.bak` fallback.
- `FolderManager`: domain service for CRUD operations on folders and items (create, rename, delete, set appearance, set position, add item, remove item, reorder items). Deleting folders or removing items never deletes physical source files or applications.
- `IShortcutResolver` & `ShellShortcutResolver`: resolves `.lnk` via COM `IShellLinkW` / `IPersistFile`, `.url` internet shortcuts, executables, files, directories, and Store apps (`shell:AppsFolder\<AUMID>`). Stores both original shortcut path and resolved target.
- `ShellLauncher`: executes original `.lnk` / `.url` when present (preserving arguments, working directory, elevation, and custom icons) and falls back to target path; provides `OpenLocation` to reveal targets in File Explorer (`explorer.exe /select,"<path>"`).
- `IconCacheService`: two-tier icon cache (in-memory `ConcurrentDictionary` + `%LOCALAPPDATA%\GlassFolders\IconCache` disk PNGs) with high-quality 32-bit system icon extraction via `ExtractIconEx` and `SHGetFileInfo`.
- Enhanced `FolderTile`: category glyphs (Briefcase, Gamepad, Play, Gear, Code, Folder), live item count badge, drag-and-drop file receiving directly on the desktop tile, and full context menu (Open, Add Files, Rename, Delete, Exit).
- `FolderPopup`: frosted glass popup window with 4×3 grid (12 items per page), page dots, pagination navigation, launch-on-click, empty state notice, OLE drag-and-drop file receiving, and item context menu (Open, Open file location, Rename, Remove from folder).
- Unit tests: expanded test suite from 7 to 25 tests covering `StorageTests`, `FolderManagerTests`, and `ConfigurationManagerTests`. All 25/25 passing in both Debug and Release.

### Test results (Windows 11 25H2, 1 monitor, 1920×1080 @125 %)
- Unit tests: 25/25 passed (0 failures, 100% pass rate).
- Build: 0 warnings, 0 errors in both Debug and Release (verified with `-warnaserror`).
- Persistence: dynamic folder creations, renames, additions, and deletions persist across app restarts and recover cleanly from corrupted files.

### Next / Phase 3
- Phase 3: Liquid Glass interface (tokens, theme switching, DWM acrylic backdrops, full appearance controls, and settings window).

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
