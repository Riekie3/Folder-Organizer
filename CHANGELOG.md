# Changelog

All notable changes to GlassFolders will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Drag an icon between pages in the folder popup.** Hold a dragged icon at the left or right edge of the grid, or over a page dot, and the popup turns the page after a moment. Each edge entry turns one page, so resting there does not run through the whole folder; a page dot targets its page directly. The drop lands on the page shown when the button is released, so an item can move from page 1 to page 3 without expanding the popup first.
- **Dragging an icon now lifts it off the card.** While an item is dragged, a slightly enlarged copy with a soft shadow follows the pointer and the other icons slide aside to leave the drop cell open. On release the copy settles into the cell and the icons finish gliding to their places; cancelling the drag puts it back. This motion follows the drag setting under Animations and the reduce-motion option.

## [1.1.3] - 2026-11-15

### Added
- **Keyboard navigation in the folder popup.** Once an icon has focus, the arrow keys move between icons, Home and End go to the first and last icon on the page, and Enter opens the focused icon. With no icon focused, Left/Right and Home/End still change pages, so the old shortcuts keep working. Movement stays on the visible page, and Down stops on the last icon of a partly filled row instead of moving into an empty cell.
- **Custom folder pictures are now in backups.** A backup stores the picture each folder shows under `folder-icons/`, with its SHA-256 checksum and a folder-id map in the manifest; a restore writes the picture into the app's own folder and points the restored folder at it. Merge restores a picture only when the archive has one, so a folder that already shows a picture keeps it. A picture that is unreadable, empty, larger than 10 MB or not a supported image is skipped without failing the backup. A folder whose picture was skipped no longer keeps a path from the exporting PC: Replace brings it back with its drawn look, and Merge keeps the picture it already shows.

### Fixed
- **A Settings window stayed in memory after it was closed.** It subscribed to the app-wide Explorer recovery and monitor services with lambdas that were never removed, so every Settings window ever opened was kept alive by them. It now unsubscribes when it closes, and its save timer stops.
- **Renaming an item in the popup did not save the new name.** The rename changed the in-memory item and then "saved" by re-setting the folder's position, which only worked because that call happened to write the file. `FolderManager.RenameItem` now saves the rename itself and ignores empty names.
- **Deleting a folder left its layout and monitor placement behind.** The saved tile layout entry and the displaced-monitor registration stayed in the config, and a popup already open for the folder stayed on screen. Deleting now closes the popup first, then removes the layout entry, saves the layout and unregisters the placement.
- **A backup recorded `1.0.0` as its app version,** even in later releases. The manifest now takes the version from the assembly, so the file says which build made it.
- **Backup import now enforces the limits it documents.** `ZipSafety` rejected traversal and executable payloads but accepted any other entry and never checked the compression ratio. It now allows only `manifest.json`, `folders.json`, `settings.json`, `icons/*.png|.ico` and `folder-icons/*` images, rejects an entry that expands more than 100:1, and caps a folder picture at 10 MB. The JSON files and pictures are read through a bounded stream rather than extracted to a staging folder, so an entry whose declared length lies cannot allocate past the limit. An archive whose manifest declares a newer schema version is refused with a clear message instead of being read as if it were this version.

## [1.1.2] - 2026-11-10

### Fixed
- **The folder popup showed a solid navy panel instead of glass.** The glass was measured before the popup was placed on screen, so the card looked like it sat outside the captured area and fell back to the solid panel. The popup now measures again once it is in place, so it shows the blurred wallpaper and the Transparency, Blur and Border sliders in Settings take effect on it.

### Changed
- The glass tint lifts more over dark wallpapers and its safety layer is a vivid blue instead of navy, closer to the reference's bright blue glass. White text keeps a contrast of at least 2.6:1.

## [1.1.1] - 2026-11-10

### Added
- **Drag to reorder inside a folder popup.** Press and drag an icon; a highlight shows the cell it will land in and slides between cells, and on drop the icons glide to their new places. The order is saved. It works within the page you are viewing (expand the popup to move across a larger area). The motion follows the drag setting under Animations and the reduce-motion option.

## [1.1.0] - 2026-11-10

### Changed (size)
- **Smaller:** the popup is drawn at 70 % of the reference size (about 605 × 375 DIP) and desktop folders match Windows' own desktop icons (48 DIP for Medium icons, read from the desktop view and applied when folders are next refreshed, for example at start or after a scale change). Small and large folders are 0.75 and 1.25 times that. Screens too small for the card still shrink it further.

### Added
- **Screenshot tool:** `tools/ScreenshotCapture` now takes real on-screen captures. It draws a neutral wallpaper full screen, opens the real popup, Settings and folder editor on it and reads the screen back, so the glass in the README is the app's real glass. It uses Windows' own programs for the sample items, so no icon shows a missing badge.

### Version
- **1.1.0** in `Directory.Build.props` and the installer.

### Changed
- **Desktop folders:** redrawn as in the reference, as solid coloured folders with a white glyph, in its blue, purple, coral and green. Folders still on an old preset colour move to the matching new one once; colours chosen by hand are kept.
- **Selected folder:** a larger frosted plate with a cyan glow, shown on hover and while the folder is open. Its glass is the wallpaper behind it, blurred, read from the wallpaper file rather than the screen.
- **Folder labels:** white with a dark outline, as in the reference. On the plate over a light wallpaper they turn dark.
- **Placement:** new folders go into free cells of Windows' desktop icon grid, down each column from the left, never onto an icon or another folder. Saved positions keep their place.
- **Snapping:** dragging a folder snaps it to the nearest free cell of the desktop icon grid. The Grid Cell Size setting is gone.
- **Folder menu:** now follows the brief: Open, Rename, Change Icon, Add Application, Add File or Folder, Appearance, Size, Move to Monitor, Delete Folder. Hide per folder is still planned.
- **Small icons:** the app and tray icons have plain 16–32 px versions, and the tray icon follows the display scale.
- **Item count badge:** off by default, with a setting.
- **Popup layout:** rebuilt to match the reference: 864 × 536 card, large full-colour folder icon and title, 96 px app icons, two-line labels, a soft plate on hover, and page dots instead of arrows.
- **Popup position:** it opens about 350 DIP beside the folder, moving closer or to the other side when there's no room, and always stays inside the work area. Smaller screens scale it to fit.
- **Popup grid:** the default is now 4 × 2. The columns and rows settings now work. A saved value of 3 rows, the old default, moves to 2 once. The card only draws the rows its items use.
- **Icons:** up to 256 px from the shell, with correct transparency, loaded in the background so a first open isn't held up. Apps with no large icon sit on a rounded tile so they don't look blurry.
- **Add items:** the + button moved to a right-click menu and a ⋯ button that appears when the pointer is on the header. Dropping files still works.
- **Popup glass:** replaced Windows' blur with snapshot glass. The popup now has smooth rounded corners and no square block behind it, and you can see the wallpaper's colours through it.
- **Adaptive tint:** the tint lightens on dark wallpapers and darkens on bright ones, keeping white text readable.
- **Settings, folder editor and message boxes:** now drawn on the same snapshot glass as the popup, in the style of reference 3: a sidebar with icons and a blue selected pill, toggles with a white knob, glass sliders, pill inputs and segmented controls. No stock Windows control is left on any page. Windows' own Mica and Acrylic backdrops (and their square block) are gone.
- **Menus:** the folder menu, the popup's menus and the tray menu are glass menus with rounded corners, a shadow and a blue hover pill.
- **Rename and dialogs:** the rename box and all 10 message boxes (backup, restore, reset, delete, errors) are glass dialogs. Buttons say what they do ("Replace" and "Merge" instead of Yes and No), and Delete and Reset are red.
- **Window placement:** Settings and dialogs open centred on the monitor of their owner, or the one holding the pointer, and can be dragged. Their glass stays lined up with the screen as they move.
- **About page:** the globe emoji is a drawn icon, and the architecture line no longer claims DWM Acrylic.

### Added
- **Appearance settings, all working live:** Theme (Follow Windows, Light, Dark; Follow Windows changes while the app runs), Transparency, Blur, Border, Shadow, Corner radius, app icon size (Small, Medium, Large), popup columns and rows. Settings shows the change at once; the popup uses it the next time it opens. The popup keeps its blue glass in both themes.
- **Light theme:** dark ink on milky glass. A white scrim is added only where a dark wallpaper would make the ink hard to read (4.5:1 or better).
- **Log level setting:** Errors, Warnings, Info or Debug under Explorer Recovery. It now takes effect at start and as soon as it changes.
- **Change Icon:** choose another glyph, or a picture (PNG, JPG, ICO and others). The app keeps its own copy and never runs the file.
- **Folder sizes:** Small, Medium and Large, from the folder menu or Appearance.
- **Glass folder style:** reference 4's folder with a frosted front flap, as an option in Appearance.
- A fourth sample folder, Utilities, on first run.
- **Expand button:** shows one more row per page, and stays on the same items.
- **Open and close animation:** the popup grows out of the clicked folder and shrinks back into it. It follows the animation settings and Windows' animation effects setting.
- **Page changes:** mouse wheel, left and right arrows, Page Up, Page Down, Home, End, or click a page dot.
- Solid panel for high contrast, Windows transparency off, and failed or all-black captures.
- Clicking the popup's shadow now closes it, like any other click outside.

### Fixed
- **Changing Windows' display scale while GlassFolders runs** (100, 125, 150 %) used to leave the folders stacked on each other or on desktop icons, because the app kept the scale it started with. The folders now move with the scale and re-fit to the icon grid once Explorer has laid its icons out again.
- **Transparency effects turned off, or high contrast turned on, while Settings or a dialog is open:** the window now drops to the solid panel at once. It used to stay glass until it was reopened.
- **Folders hidden by a "minimise all" tool** come back at once.
- The "Show a warning badge on shortcuts whose target is missing" setting did nothing. The popup now follows it.
- The log level setting was ignored; logs were always written at Info.
- Add Application keeps a shortcut as a shortcut, with its own icon and arguments, instead of adding its target.
- A fade could stay stuck if animations were turned off while it ran.

## [1.0.2] - 2026-11-07

### Fixed
- **Startup entry:** opening Settings no longer rewrites the Windows startup entry. It is written only when you change the toggle, only from `GlassFolders.exe`, and a wrong path is repaired on app start.
- **ScreenshotCapture:** now uses a throw-away config, ignores `--tray`, and writes relative to the repo, so it can no longer modify your folders or settings.
- Version text in Settings comes from the assembly; the About screenshot shows the About page.
- README claims corrected (screenshots are off-screen renders; popup animation is planned).

## [1.0.1] - 2026-11-07

### Added
- **Publisher Branding & Website Integration**: Updated all official metadata, About screen, sidebar links, installer attributes, and documentation to credit **[Rie-Sources](https://rie-sources.com/)**. Clicking publisher links anywhere in the application or settings opens `https://rie-sources.com/` in the user's default browser.
- **Real Software Screenshot Gallery**: Captured authentic high-resolution screenshots of the software (`docs/screenshots/`) including hero desktop experience, folder popup, settings dashboard, animations manager, and folder appearance editor, embedded into `README.md`.
- **ScreenshotCapture Automation Tool**: Created `tools/ScreenshotCapture` utility rendering pixel-perfect screenshots of real WPF windows and desktop composite scenes.
- **Fluent 2 Liquid Glass App Icon**: Designed and packed high-resolution multi-frame Windows icon (`assets/app.ico` and `assets/app-icon.png`) containing 256x256, 128x128, 64x64, 48x48, 32x32, 24x24, and 16x16 frames with transparent feathered alpha. Integrated into `GlassFolders.exe` via `<ApplicationIcon>`, Inno Setup installer via `SetupIconFile`, and `SettingsWindow` sidebar & About dialogs.
- **IconPacker Utility**: Created `tools/IconPacker` to automate background removal, edge anti-aliasing, and multi-resolution `.ico` generation directly in C#.
- **Zero-Footprint Portable Mode**: Centralized path resolution in `AppPaths.cs`. If `portable.flag` exists next to `GlassFolders.exe`, configuration, logs, and icon caches are automatically directed to local `<exeDir>\data` and `<exeDir>\data\local` folders without touching user `%APPDATA%`.
- **Precompiled Release Packages**: Built `artifacts/GlassFolders-Setup-v1.0.1.exe` using Inno Setup compiler and generated standalone portable bundle `artifacts/GlassFolders-v1.0.1-portable.zip` with verified `artifacts/SHA256SUMS.txt`.
- **Desktop Tile Direct Rename**: Added "Rename Folder..." directly into desktop tile context menu for seamless folder updates.
- **Dynamic Tile Synchronization**: Linked `PocController` directly to `FolderManager.Changed` events to instantly reflect item badge count changes and newly added shortcuts on desktop tiles.
- **Automated Unit Tests**: Added `AppPathsTests` and `MemoryTrimmingTests`, expanding the test suite to 66 passing automated tests.

### Fixed
- **DWM Composition Check Access Violation (0xC0000005)**: Corrected native P/Invoke signature for `DwmIsCompositionEnabled` in `DwmHelpers.cs` to take `out int pfEnabled` and return `HRESULT`, preventing an access violation crash when opening `SettingsWindow` or `FolderPopup`.
- **SettingsWindow Slider Initialization Race**: Initialized `_settings` before calling `InitializeComponent()` and added null guards to slider change handlers to prevent NullReferenceException during XAML BAML parsing.
- **Startup Auto-Close Crash (ArgumentException in GC.Collect)**: Fixed .NET 10 memory trimming call in `PocController.PerformMemoryTrim()`. Passing `blocking: false` with `GCCollectionMode.Aggressive` threw `System.ArgumentException: AggressiveGC requires setting the blocking parameter to true`, which terminated the process when `_memoryTrimTimer` ticked 5 seconds after launch. Now passes `blocking: true` and wraps memory trimming in defensive exception handling.
- **Drag & Drop Crash (AccessViolationException)**: Corrected COM P/Invoke signature for `IShellLinkW::GetPath` in `ShellShortcutResolver.cs` by replacing `out byte pfd` with `nint pfd` (null pointer), preventing a 592-byte stack overwrite crash when dropping `.lnk` files.
- **Open File Location**: Resolved issue where "Open file location" did nothing for system binaries or unrooted paths; `ResolveLocationCandidate` in `ShellLauncher.cs` now resolves executables via `%PATH%`, System32, and Windows root.
- **File Dialog Deactivation Conflict**: Added `_isShowingDialog` guard to `FolderPopup.xaml.cs` to prevent window dismissal when opening modal file pickers.

---

## [1.0.0] - 2026-11-07

### Added
- **Production Installer**: Inno Setup script (`installer/GlassFolders.iss`) configuring per-user installation in `%LOCALAPPDATA%\Programs\GlassFolders`, desktop shortcuts, mutex checks, and clean uninstall capabilities.
- **CI/CD Automation**: GitHub Actions workflow for pull request verification (`.github/workflows/build.yml`) and automated release compilation, SHA-256 generation, and installer artifact publishing (`.github/workflows/release.yml`).
- **Complete Documentation Suite**: Full user guide (`docs/USER-GUIDE.md`), backup specification (`docs/BACKUP-RESTORE.md`), updated architecture overview (`docs/ARCHITECTURE.md`), and performance benchmarks (`docs/PERFORMANCE.md`).
- Enforced zero-warning policy across all projects (`TreatWarningsAsErrors=true`).

---

## [0.9.0-rc] - 2026-11-07

### Added
- **Aggressive Working Set Trimming**: Post-interaction 5-second debounced trimming using `EmptyWorkingSet` and `GC.Collect(2)` reducing memory footprint to ~50–65 MB baseline.
- **Icon Cache Memory Management**: `IconCacheService.TrimMemoryCache()` releasing WPF `BitmapSource` references while retaining disk cache integrity.
- **Automated Idle Benchmarking Tool**: `tests/manual/measure-idle.ps1` measuring CPU percentage, Working Set, Private Bytes, Thread Count, Handle Count, and USER/GDI objects over 30–60 second intervals.
- **Performance Documentation**: `docs/PERFORMANCE.md` certifying 0.00% idle CPU and zero GDI/USER object leakage.

---

## [0.7.0] - 2026-11-07

### Added
- **Granular Animation Engine**: `AnimationSettings` model with global master toggle, speed multiplier (0.5× to 2.0×), and 8 independent category switches (Folder Open/Close, Tile Hover, Tile Press, Item Launch, Dialog Fade, Drag Physics, Theme Transition, Layout Snap).
- **Windows Accessibility Integration**: Dynamic querying of `SPI_GETCLIENTAREAANIMATION` via `SystemParametersInfoW` to automatically respect system-wide "Show animations in Windows" preference.
- **Zero-Wakeup Animation Gatekeeper**: `AnimationService` and `AnimationHelper` ensuring that when animations are disabled, UI elements snap instantly without instantiating or running WPF Storyboard timers.
- **Interactive Animations UI**: Comprehensive Animations tab in `SettingsWindow` with live preview triggers, speed sliders, and quick preset buttons.

---

## [0.6.0] - 2026-11-07

### Added
- **Portable ZIP Backup & Restore**: Complete archive backup subsystem (`BackupService`) exporting all folder configurations, layout coordinate trees, and custom icon caches.
- **Security-Hardened ZIP Engine**: `ZipSafety` validation preventing ZipSlip directory traversal (`..`), path escaping, decompression bomb attacks (>50 MB / >500 entries), and blocking executable payloads (`.exe`, `.bat`, `.cmd`, `.dll`, `.vbs`, `.ps1`, `.scr`).
- **Path Relinker Heuristics**: Automated missing target resolution matching relative user folders (`%USERPROFILE%`, `%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMFILES%`) and System32 utilities when restoring archives across different machines or usernames.
- **Pre-Flight Import Planner**: SHA-256 integrity verification, missing path analysis, and preview modal displaying relinking suggestions before applying changes.
- **Backup & Restore UI**: Modern glass management panel in Settings with Replace vs. Merge import modes and rolling pre-action rollback history (up to 10 snapshots).

---

## [0.5.0] - 2026-11-07

### Added
- **Relative Coordinate System**: `PositionMath` translating absolute physical screen coordinates into normalized relative fractions (0.0–1.0) per monitor work area.
- **Multi-Monitor Displacement Engine**: `MonitorDisplacementManager` preserving folder home monitor assignments. If a secondary monitor is detached, folders seamlessly displace to the primary display without overwriting their home layout; upon reconnect, folders automatically return to their home coordinates.
- **Collision & Clamping Math**: Spiral search algorithm (`FindNearestFreeCell`) for automatic overlap avoidance and boundary clamping across heterogeneous DPI displays.
- **Monitor Management UI**: Multi-monitor desktop layout overview in Settings with real-time resolution/DPI badges and "Move to Monitor ▸" tile context menu.
- **Win32 Display Listeners**: Event-driven debouncing for `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `WM_SETTINGCHANGE`.

---

## [0.4.0] - 2026-11-07

### Added
- **Single-Instance IPC Engine**: Named mutex (`Local\GlassFolders.SingleInstance`) and asynchronous Named Pipe server (`GlassFolders.IpcPipe`) directing subsequent launches to activate the existing instance.
- **Win32 System Tray Integration**: Native `Shell_NotifyIconW` wrapper with dynamically rendered 32-bit ARGB glossy folder icon, paused indicator overlays, and glass context menu.
- **Automated Explorer Recovery**: Resilient state machine (`ExplorerRecoveryService`) detecting shell termination via `EVENT_OBJECT_DESTROY` and `TaskbarCreated`, recovering desktop attachments using exponential backoff (250 ms → 8 s, 2 min cap).
- **Session & Power Watcher**: Extended `ShellWatcher` handling `WM_POWERBROADCAST` and `WTSRegisterSessionNotification` for seamless workstation lock/unlock and sleep/resume recovery.
- **Startup Registration**: HKCU Run key integration with `--tray` argument and asynchronous shell readiness polling (`WaitForShellReadyAsync`).
- **Rolling Diagnostics**: Thread-safe logging subsystem (`DiagnosticService`) maintaining up to five 1 MB rolling log files in `%LOCALAPPDATA%\GlassFolders\logs`.

---

## [0.3.0] - 2026-11-07

### Added
- **Real DWM Acrylic & Mica Effects**: Win32 `DwmSetWindowAttribute` integration implementing `DWMSBT_TRANSIENTWINDOW` (Acrylic), `DWMSBT_MAINWINDOW` (Mica), dark mode flags, and rounded corner attributes.
- **Design System Tokens**: High-fidelity XAML design token suite (`Tokens.xaml`, `FolderGlyphs.xaml`, `Controls.xaml`) matching Windows 11 Fluent 2 guidelines.
- **Folder Customization Dialog**: `FolderEditorDialog` supporting glyph selection, dual-stop glass gradient pickers, opacity tuning, and shortcut management.
- **Tabbed Settings Suite**: Comprehensive glass `SettingsWindow` featuring General, Appearance, Folders, Desktop Layout, Animations, Backup & Restore, and About tabs.

---

## [0.2.0] - 2026-11-07

### Added
- **Domain Models & Validation**: `FolderDef`, `FolderItem`, `FolderAppearance`, and `AnimationSettings` with complete serialization contracts.
- **Atomic Storage Engine**: `AtomicFile` writer utilizing temporary write-through and atomic `File.Replace` with `.bak` rollback safeguards.
- **Shortcut & Icon Extractor**: COM-based `IShellLinkW` / `IPersistFile` target resolution and high-resolution multi-size icon extraction (`SHGetFileInfoW`).
- **Application Launcher**: `ShellLauncher` supporting standard binaries, UWP app execution (`shell:AppsFolder\`), and URL protocols.

---

## [0.1.0-poc] - 2026-11-07

### Added
- **Desktop Layer Proof-of-Concept**: Win32 desktop hierarchy traversal identifying Windows 11 25H2 `ProgmanOwnedHost` architecture.
- **Non-Activating Desktop Surfaces**: `WS_POPUP` windows owned by `Progman` with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` and `HWND_BOTTOM` z-order stabilization.
- **Input Validation**: Verified desktop icon interaction, rubber-band marquee selection, and Win+D visibility.
- **Verification Tools**: `DesktopProbe` and `PocVerifier` diagnostic utilities.
