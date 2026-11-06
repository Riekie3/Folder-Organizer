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
