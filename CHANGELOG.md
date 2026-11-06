# Changelog

All notable changes to GlassFolders will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- **Security-Hardened ZIP Engine**: `ZipSafety` validation preventing ZipSlip directory traversal (`..`), path escaping, decompression bomb attacks (>50 MB / >500 entries), and blocking executable payloads (`.exe`, `.bat`, `.cmd`, `.dll`, `.ps1`).
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
