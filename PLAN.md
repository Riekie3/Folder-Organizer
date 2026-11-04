# GlassFolders — Detailed Development Plan

_Drafted 2026-11-03. Source of truth for the build; each phase session reads this first and appends to `DEVLOG.md`._

---

## 0. Starting point (inspected 2026-11-03)

| Item | Finding | Impact |
|---|---|---|
| Local folder `D:\Rie\Claude\Folder Organizer` | Only `Referance\` (4 images). No code, no `.git`. | Greenfield; `git init` + add remote in Phase 1. |
| GitHub `Riekie3/Folder-Organizer` | Reachable, **empty** (no refs). `gh` logged in as Riekie3. | First push creates `main`. Releases can be published via `gh`. |
| .NET SDKs | 6.0.428 and **10.0.403** | Target **.NET 10 (LTS)** WPF. |
| Windows | 11 **25H2**, build 26200 | Uses the post-24H2 desktop hierarchy (WorkerW is now a *child* of Progman). Older WorkerW tricks from blog posts are wrong here — PoC must test against this build. |
| Monitors | **1** (`DISPLAY1`, 1536×864 logical → likely 1920×1080 @125%) | Multi-monitor *code* can be built and unit-tested, but 2/3-monitor acceptance tests need a second screen (or a virtual display driver). Flagged in §9. |
| Installer tooling | Inno Setup not installed | Install via `winget install JRSoftware.InnoSetup` in Phase 9 (and in GitHub Actions). |

### Reference images — design language extracted

1. **Desktop + Work popup (main reference)** — Folders sit in the desktop icon column alongside This PC / Recycle Bin. Each folder = colored glossy folder glyph with a white category glyph (briefcase, gamepad, play, gear), label below. Hover/selected folder gets a **frosted rounded tile** (~16px radius, faint white border, soft glow). Popup: large frosted panel (~28px radius), header row = folder glyph + title (~28pt semibold, white) + divider + expand ⤢ + close ✕, a subtle header separator, 4×2 grid of **full-color real app icons** (~96px) with white labels, **page dots** at bottom. Background clearly blurred wallpaper, blue tint, 1px light rim.
2. **Glass folder icon** — two-layer folder: saturated blue→violet back plate, **frosted translucent front flap** with lighter edge highlight, soft colored glow. → This is the house style for the default folder icon (generated as vector, recolorable per folder).
3. **Start-menu concept panels** — tall glass panels, pill search box, segmented "Category view / Grid view" control, toggle switches, small icon footers. → Pattern library for the **Settings window** (pill inputs, glass toggles, segmented controls).
4. **3×4 icon grid** — rounded-square icon tiles with a subtle inner tile behind each icon, hover state = lighter rounded rectangle around item ("Create Shortcut"). → Item hover/selection style inside popups.

**Design tokens (initial):** radius 8 / 16 / 28; border 1px white @ 22–35% alpha; tint blue `#3B6FE0` @ 10–25%; text white with 30% black drop shadow for readability on any wallpaper; Segoe UI Variable (Display for titles); ease = cubic-bezier(0.2, 0.8, 0.2, 1); durations 120 / 200 / 320 ms (Fast / Normal / Slow).

---

## 1. Key technical decisions

| Area | Decision | Why |
|---|---|---|
| Stack | C# 13, **.NET 10 WPF**, Per-Monitor-V2 DPI manifest | Requested; WPF gives GPU rendering + animation with zero rendering when idle. |
| Dependencies | **None at runtime** beyond the BCL (System.Text.Json, System.IO.Compression). Own P/Invoke layer (`NativeMethods`) instead of WinForms/H.NotifyIcon. Tests: xUnit. | Keeps working set low, no supply-chain bloat. |
| Desktop layer | **Option A (primary):** each desktop folder is a borderless top-level window *owned by Progman* (`GWLP_HWNDPARENT`), `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, pinned to the bottom of the z-order via `WM_WINDOWPOSCHANGING`. Survives Win+D / "Show desktop", stays below all app windows, never in taskbar/Alt-Tab, gets normal mouse input. **Option B (fallback, tested in PoC):** child window of the `SHELLDLL_DefView` host. Chosen strictly by PoC results. Both sit behind an `IDesktopHost` interface. | Fences-style behaviour without hiding desktop icons or using topmost. Undocumented bits isolated in one class. |
| Folder hosting | **One window per desktop folder** (small, ~110×120 DIP). Not one full-screen overlay. | A full-screen transparent overlay would block desktop icon clicks and drag-select. |
| Glass | Popup + Settings: real DWM backdrop — `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (Acrylic) + `DWMWA_USE_IMMERSIVE_DARK_MODE` + `DWMWA_WINDOW_CORNER_PREFERENCE`, WPF background transparent via `DwmExtendFrameIntoClientArea`. Tint/rim/gloss layers drawn in WPF on top. Desktop folder tiles: **no live blur** — static gradient + rim (they sit on wallpaper, not over moving content). Fallback when transparency is off / Remote Desktop / battery saver: solid tinted panel. | Blur is done by DWM in the compositor (no CPU, no screenshots from us). Live blur on every desktop tile would cost GPU for no visible gain. |
| Explorer detection | Event-driven only: `RegisterWindowMessage("TaskbarCreated")` broadcast, wait handle on the explorer process (`RegisterWaitForSingleObject`), `SetWinEventHook(EVENT_OBJECT_DESTROY)` scoped to the Progman HWND. No polling. | Spec §9.1. |
| Monitor identity | `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo(TARGET_DEVICE_NAME)` → `monitorDevicePath` + EDID manufacturer/product IDs → stable `MonitorKey`. Enumeration index never persisted. | Spec §10.1. |
| Coordinates | Stored per monitor as **work-area-relative fractions + DIP offset + saved resolution/DPI**; re-projected on change, clamped to work area. | Survives resolution/DPI/orientation changes. |
| Icons | `IShellItemImageFactory::GetImage` (up to 256px) → PNG cache in `%LOCALAPPDATA%\GlassFolders\IconCache`, key = hash(path + last-write-time + size). Loaded lazily when a popup opens; frozen `BitmapSource`s. | Real icons, UWP/Store apps included, fast reopen. |
| Launching | `ShellExecuteEx` on the **original .lnk / .url** when the item came from a shortcut (preserves args, working dir, run-as, icon); on the target path for .exe/files/folders; `shell:AppsFolder\<AUMID>` for Store apps. | Spec §8.2. |
| Storage | `%APPDATA%\GlassFolders\` → `settings.json`, `folders.json`, `layouts.json`; atomic write (temp file + `File.Replace`), `schemaVersion` + migration chain, rolling `.bak` copies, debounced saves (500 ms). | Spec §19. |
| Tray | Own `Shell_NotifyIcon` wrapper on a hidden message-only window (this window also receives TaskbarCreated, WM_DISPLAYCHANGE, power events). Tray menu = custom glass WPF popup. | Lightweight, one message pump for all system events. |
| Startup | `HKCU\...\Run` → `GlassFolders.exe --tray`; optional delay; single instance via named Mutex + named pipe ("open settings" from second launch). | No admin, no service. |
| Publish | Framework-dependent, ReadyToRun, x64 (+ arm64 later). Installer: **Inno Setup**, checks for .NET 10 Desktop Runtime and offers to install it. | Small download (~3–5 MB app); self-contained would be ~70+ MB. (Decision D2 below.) |

---

## 2. Solution layout

```
Folder Organizer/
├─ Referance/                      (untouched)
├─ PLAN.md  DEVLOG.md  README.md  CHANGELOG.md  LICENSE  .gitignore  .editorconfig
├─ GlassFolders.sln
├─ src/
│  ├─ GlassFolders.Core/           (no WPF refs → fully unit-testable)
│  │  ├─ Models/                   FolderDef, FolderItem, MonitorLayout, Settings, AnimationSettings
│  │  ├─ Config/                   ConfigurationManager, AtomicFileWriter, Migrations/
│  │  ├─ Folders/                  FolderManager, ShortcutManager (lnk/url parsing via IShellLink)
│  │  ├─ Layout/                   PositionMath, MonitorKey, MonitorMapping
│  │  └─ Backup/                   BackupService, BackupManifest, ImportPlanner, PathRelinker, ZipSafety
│  ├─ GlassFolders.Native/         NativeMethods (P/Invoke), DwmHelpers, ShellIcons, DisplayConfig
│  └─ GlassFolders.App/            (WPF exe)
│     ├─ App.xaml / ApplicationLifecycle.cs
│     ├─ Desktop/                  IDesktopHost, ProgmanOwnedHost, DefViewChildHost, DesktopFolderWindow, DesktopInteractionManager
│     ├─ Services/                 ExplorerRecoveryService, MonitorService, TrayService, StartupService, IconCacheService, DiagnosticService, SingleInstance
│     ├─ UI/                       FolderPopup, SettingsWindow (pages), FolderEditor, ContextMenus, ImportPreview, RelinkDialog
│     └─ Themes/                   Tokens.xaml, Glass.xaml, Controls.xaml, Animations.xaml, FolderGlyphs.xaml
├─ tests/
│  ├─ GlassFolders.Core.Tests/     xUnit
│  └─ manual/                      acceptance checklists + scripts (restart-explorer.ps1, measure-idle.ps1)
├─ installer/GlassFolders.iss
├─ docs/                           ARCHITECTURE.md, USER-GUIDE.md, BACKUP-RESTORE.md, PERFORMANCE.md, KNOWN-ISSUES.md
└─ .github/workflows/              build.yml (PR/push), release.yml (tag-triggered, draft release)
```

---

## 3. Phases

Every phase ends with: build green → tests green → DEVLOG entry → commit(s) → **push to GitHub**. Commits are pushed during the phase, not batched at the end.

### Phase 1 — Inspection & desktop-integration Proof of Concept  (`v0.1.0-poc`)

**Goal:** prove folders can live on the real desktop on *this* Windows 25H2 build before any UI polish.

1. `git init`, `.gitignore` (VisualStudio template + `bin/ obj/ *.user .vs/ publish/ artifacts/`), README stub, `PLAN.md`, `DEVLOG.md`; add remote; first push to `main`.
2. Create solution skeleton (Core / Native / App / Tests) targeting `net10.0-windows`, PerMonitorV2 manifest, `<Nullable>enable`, `<InvariantGlobalization>`.
3. **Desktop hierarchy probe tool** (`tools/DesktopProbe`, console): dumps Progman → WorkerW / SHELLDLL_DefView → SysListView32 tree, class names, rects, styles, owner/parent, before and after sending `0x052C` to Progman. Result recorded in `docs/ARCHITECTURE.md`.
4. Implement `IDesktopHost` with **Option A (Progman-owned)** and **Option B (DefView child)**; switchable by a command-line flag.
5. PoC window: plain rounded rectangle + label, draggable, click shows a MessageBox-free inline "opened" state.
6. Test matrix (both options), recorded in DEVLOG:
   - visible on desktop; desktop icons still clickable, rubber-band selection still works around it
   - stays **behind** a normal window when that window is focused/moved over it
   - **Win+D**, "Show desktop" corner, Win+M, Task View, virtual desktops switch
   - does not cover taskbar; not in taskbar/Alt-Tab
   - drag-move works; position saved/restored after app restart
   - `taskkill /f /im explorer.exe` + `start explorer` → window re-attaches (minimal recovery)
   - DPI: move between scale 100/125/150% (Settings → Display)
7. Pick the host strategy; write limitations into `docs/ARCHITECTURE.md` + `docs/KNOWN-ISSUES.md`.

**Exit criteria:** all bullets in step 6 pass for the chosen option (multi-monitor items marked "pending hardware" if no second screen). **Stop and report to the user before Phase 2.**

### Phase 2 — Core desktop organizer  (`v0.2.0`)

1. Core models + `ConfigurationManager` (atomic write, schema v1, debounced save, corrupt-file recovery → load last `.bak`, log, start with defaults). Unit tests.
2. `FolderManager`: create / rename / delete (never touches source files) / hide / show-all / reorder; `ShortcutManager`: classify item (exe, lnk, url, file, folder, Store app), resolve .lnk via `IShellLinkW` (target, args, workdir, icon location) — store **both** the shortcut path and resolved target.
3. `DesktopFolderWindow` per folder via `IDesktopHost`; drag to move with snap-to-grid (optional); label; resize presets (S/M/L).
4. `FolderPopup` (single instance, reused): grid of items, columns adjustable, paging when rows overflow, page dots, ESC / click-outside (`WM_ACTIVATE` deactivate) / ✕ / open-another closes it. Positioned beside its folder, clamped to monitor **work area** (taskbar-aware via `GetMonitorInfo`).
5. Launch via `ShellExecuteEx` (rules in §1); missing target → item shows warning badge, not removed.
6. Adding items: drop on open popup, drop desktop shortcut onto a desktop folder (OLE drop target), "Add Application" (Start-menu apps list from `shell:AppsFolder`), "Add File/Folder" (`IFileOpenDialog`). Drag-reorder within popup; drag item out → "Remove from folder" (with explicit wording: _"Removes the shortcut from GlassFolders. The app/file is not deleted."_).
7. Right-click folder menu with all 9 entries from spec §6.1 (some open stub dialogs until Phase 3/5). Item context menu: Open, Open file location, Rename (display name), Change icon, Remove from folder.
8. `IconCacheService` (lazy, disk + in-memory weak cache).

**Exit:** create folders, add 20+ mixed items, launch each type, restart app → everything restored. Core tests pass.

### Phase 3 — Liquid Glass interface  (`v0.3.0`)

1. `Themes/Tokens.xaml` (colors, radii, spacing, typography, durations) — single source; Light / Dark / Follow-Windows (`UISettings` + `WM_SETTINGCHANGE`).
2. Folder glyph system: vector two-layer glass folder (ref image 2) with recolorable plate + white category glyph (Segoe Fluent Icons: briefcase, game, play, settings, code, palette, document, person …) + custom image/ICO option. Optional 2×2 mini app-icon preview mode.
3. Desktop folder visuals: idle = glyph + shadowed label; hover / selected = frosted tile (ref image 1).
4. Popup: DWM acrylic + tint + 1px rim + top gloss gradient, header (glyph, title, divider, expand, close), item tiles with hover plate (ref image 4), page dots, smooth resize via grip.
5. Settings window shell: glass sidebar nav + pages; controls styled per ref image 3 (glass toggles, pill text boxes, segmented control, sliders). Pages wired: General, Appearance, Desktop Layout, Performance, Recovery (status only), Backup (stubs), Animation (stubs), About.
6. Folder editor / create-folder dialog (name, color, glyph, custom icon, size, label visibility).
7. Appearance sliders live-preview: transparency, blur on/off, border brightness, corner radius, shadow intensity, icon sizes.
8. Fallback rendering when transparency effects are disabled / high contrast mode (respect `SystemParameters.HighContrast`).
9. Readability check on 4 wallpapers (light, dark, busy photo, solid color) — screenshots saved to `docs/screens/`.

**Exit:** side-by-side screenshots vs. reference image 1 approved by the user.

### Phase 4 — Windows integration & Explorer recovery  (`v0.4.0`)

1. `SingleInstance` (Mutex `Local\GlassFolders.SingleInstance` + pipe to forward "open settings").
2. `TrayService`: icon (light/dark variants), glass menu with the 7 items of spec §13, no taskbar button ever.
3. `StartupService`: HKCU Run toggle, `--tray` arg, optional delay (0/5/10/30 s), waits for shell (`GetShellWindow()` + TaskbarCreated) with timeout.
4. `ApplicationLifecycle`: ordered startup and **clean exit** (flush saves → close popups → detach desktop windows → unhook WinEvents → unregister waits → remove tray icon → stop recovery). Intentional exit sets a `ShuttingDown` flag that recovery honours.
5. `ExplorerRecoveryService` state machine: `Healthy → ShellLost → WaitingForShell → Reattaching → Verifying → Healthy | Failed(retrying)`; triggers from §1; exponential backoff (250 ms → 8 s, max 2 min) with timeout; idempotent reattach (reuses existing windows, never creates duplicates — guarded by folder-ID → window map); verification = Progman found + every visible folder window has correct owner/parent and is on-screen. Status + last event shown in Settings → Explorer Recovery, with "Recover now" button.
6. Pause mode (hide desktop windows, release popup/visual resources, stop hooks except recovery) vs. Hide-all (visibility only).
7. Sleep/resume (`WM_POWERBROADCAST`) and session lock/unlock (`WTSRegisterSessionNotification`) → re-verify.
8. `DiagnosticService`: rolling log file (`%LOCALAPPDATA%\GlassFolders\logs`, 5 × 1 MB), level toggle in Settings.
9. `tests/manual/restart-explorer.ps1`: loops 10 restarts, then checks window count == folder count (via `EnumWindows` on our process).

**Exit:** §23.2 and §23.6 checklists pass.

### Phase 5 — Multi-monitor  (`v0.5.0`)

1. `MonitorService`: enumerate via DisplayConfig, build `MonitorKey`, react to `WM_DISPLAYCHANGE` / `WM_DPICHANGED` / `WM_SETTINGCHANGE(SPI_SETWORKAREA)` (debounced 300 ms).
2. Layouts keyed by `MonitorKey`; each folder has `HomeMonitorKey` + saved relative position; folders whose monitor is absent are shown on the fallback (primary) monitor with a "displaced" flag — **saved home layout is never overwritten** while displaced; drag on fallback monitor asks nothing but stores as temporary unless user picks "Keep here".
3. Reconnect → folders return home.
4. Context menu "Move to Monitor ▸"; Settings → Desktop Layout: per-monitor list, duplicate layout to another monitor, restore defaults, choose where new folders appear (current / primary / chosen).
5. Portrait/landscape + resolution change + DPI change re-projection; overlap avoidance (nudge to nearest free grid cell).
6. Unit tests for PositionMath and displacement logic with synthetic monitor sets (1, 2, 3 monitors; reorders; primary swap; disconnect/reconnect).
7. Hardware tests on this PC: resolution and scale changes, and a second screen if available (see D1).

**Exit:** §23.3 checklist (items needing extra screens marked accordingly).

### Phase 6 — Portable backup & restore  (`v0.6.0`)

1. Export → ZIP with the exact structure of spec §11.1 + `manifest.json` (app version, schema version, created-at, machine name, monitor list, file checksums). Custom icons copied into `icons/`. No executables.
2. `ZipSafety`: reject absolute paths, `..`, symlinks, oversize entries (> 50 MB total), unknown extensions, entry count limit; verify checksums.
3. `ImportPlanner` → preview model: folder count, item count, missing targets, duplicate IDs, monitor mapping proposal, version compatibility. Preview dialog with **Replace / Merge / Cancel**.
4. Replace → auto-backup current config to `%APPDATA%\GlassFolders\Backups\pre-import-<timestamp>.zip` first. Merge → ID collision rules (same ID + same name = merge items; same ID different name = regenerate ID).
5. `PathRelinker`: detect missing targets, suggestions via env-var expansion (`%ProgramFiles%`, `%LocalAppData%`…), known folders, Start-menu shortcut name match, App-Paths registry, AUMID for Store apps; dialog with Browse / Relink / "apply same prefix change to N other items". Missing items are kept with warning badges until resolved.
6. Monitor mapping dialog when monitor keys don't match.
7. Automatic backups (before replace/reset/migration; keep last 10), custom backup folder, restore-from-list, reset configuration.
8. Migration framework test: v1 → v2 fake migration round trip.
9. Unit tests: round-trip export/import, corrupt zip, traversal zip, missing paths, merge collisions.

**Exit:** §23.4 checklist; round-trip tested by exporting, wiping `%APPDATA%\GlassFolders`, importing.

### Phase 7 — Animation control system  (`v0.7.0`)

1. `AnimationSettings` (global, 8 categories, speed) + `AnimationService.ShouldAnimate(category)` / `Duration(base)`; global OFF overrides all; respects Windows "Animation effects" (`SPI_GETCLIENTAREAANIMATION`) as reduced-motion when "Follow Windows" is on.
2. All storyboards routed through one helper (`Animate.Fade/Scale/Slide`) that snaps to final value when disabled — guarantees no functional path depends on an animation completing.
3. Implement: folder open (scale 0.92→1 + fade, origin at the clicked folder), close, window transitions, hover, icon press, drag-ghost + drop-slot shuffle, settings page transitions, decorative (gloss shimmer on open only, never looping).
4. Settings → Animation page (spec §14.6) with inactive state when global OFF; Reset to default.
5. Verify with WPF perf counters that 0 animations/timers run when idle and when OFF.

**Exit:** §23.5 checklist.

### Phase 8 — Performance & stability  (`v0.9.0-rc`)

1. `tests/manual/measure-idle.ps1`: samples CPU %, working set, private bytes, GPU engine usage (perf counters `GPU Engine`) for 10 min idle, with 0 / 5 / 20 folders, after 50 popup opens.
2. Targets: idle CPU ≈ 0% (no wakeups — verify with `powercfg /energy` or ETW), working set < 100 MB (expect ~50–70 MB), popup open < 100 ms warm / < 250 ms cold, startup to folders visible < 1.5 s, Explorer recovery < 3 s after shell ready.
3. Optimisations as needed: release popup visual tree on close after N seconds, trim icon memory cache, `RenderOptions.ProcessRenderMode` checks, `GCSettings` / `SetProcessWorkingSetSize` after startup, ReadyToRun, `TieredPGO`.
4. 8-hour soak (left running overnight) + 100 Explorer restarts script → no handle/GDI/USER object growth (check via `GetGuiResources`).
5. Results in `docs/PERFORMANCE.md`.

### Phase 9 — Release & packaging  (`v1.0.0`)

1. Versioning from one `Directory.Build.props` (`<Version>`), surfaced in About, installer, tag.
2. Inno Setup script: per-user install (`{localappdata}\Programs\GlassFolders`, no admin), Start-menu shortcut, "Start with Windows" checkbox, .NET 10 Desktop Runtime check, closes running instance before upgrade, preserves `%APPDATA%` on upgrade, uninstaller asks "Also remove my folders and settings?".
3. Portable ZIP **only if** portable mode (config next to exe via `portable.flag`) is implemented and tested — otherwise skipped.
4. GitHub Actions: `build.yml` (restore, build, test on `windows-latest`), `release.yml` on tag `v*` → publish, compile installer, `SHA256SUMS.txt`, create **draft** release (human publishes after validation).
5. Docs: USER-GUIDE, BACKUP-RESTORE, ARCHITECTURE, KNOWN-ISSUES, CHANGELOG, release notes.
6. Full acceptance run (§23 of the brief) on a clean install; install/uninstall test; then publish `v1.0.0` release with `GlassFolders-Setup-v1.0.0.exe` + `SHA256SUMS.txt`.

---

## 4. Testing strategy

- **Automated (xUnit, CI):** config read/write/migration, atomic writer, corrupt-config recovery, position math, monitor mapping/displacement, backup round-trip, zip safety, path relinker, merge rules, animation-setting resolution. Target ≥ 80% line coverage on Core.
- **Integration (local, scripted):** Explorer restart loop, idle measurement, handle-leak check.
- **Manual checklists:** `tests/manual/ACCEPTANCE.md` mirrors brief §23 with pass/fail + date + build, filled in per phase.
- Native/desktop behaviour can't run on GitHub-hosted CI (no interactive desktop) — stays local.

## 5. Git & GitHub workflow

- `main` always builds. Work on `phase/N-name` branches, merged to `main` at phase end (or direct commits on `main` if preferred — D4).
- Conventional commits (`feat:`, `fix:`, `perf:`, `docs:`, `test:`, `build:`, `release:`); push after each meaningful commit.
- Tags `v0.1.0-poc` … `v1.0.0`; GitHub Releases only for `v0.5.0`+ (pre-release) and `v1.0.0` (stable).
- Never commit bin/obj/publish, logs, local config, secrets.

## 6. Risks & mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Progman-owned window misbehaves on 25H2 (Win+D hides it / z-order fights) | Medium | Option B in PoC; z-order guard; WinEvent on `EVENT_SYSTEM_FOREGROUND` for desktop-shown edge cases. |
| Future Windows update changes desktop hierarchy | Medium | All undocumented logic in `IDesktopHost` impls; probe tool; KNOWN-ISSUES. |
| WPF + DWM acrylic quirks (black flashes on open, resize artefacts) | Medium | Pre-create popup hidden; set backdrop before first show; fallback solid style. |
| Memory > 100 MB due to WPF baseline + icons | Low–Med | Lazy icons, frozen bitmaps, decode at display size, release popup tree. |
| Can't test 2–3 monitors on current hardware | High | Unit tests with synthetic topologies; virtual display driver or a borrowed second screen (D1). |
| .NET runtime missing on target PC | Medium | Installer runtime check/bootstrap (or self-contained, D2). |

## 7. Definition of done (v1.0.0)

All brief §23 checklists pass (hardware-limited items explicitly marked), installer + uninstaller tested, docs written, `v1.0.0` release published on GitHub with SHA-256 sums, DEVLOG complete.

## 8. Estimated effort (Claude session time, rough)

| Phase | Size | Notes |
|---|---|---|
| 1 PoC | M | Highest uncertainty; may need iteration. |
| 2 Core | L | Largest code volume. |
| 3 Glass UI | L | Iterative visual review with you. |
| 4 Integration/Recovery | M | |
| 5 Multi-monitor | M | |
| 6 Backup/Restore | M | |
| 7 Animations | S–M | |
| 8 Performance | S–M | Mostly measuring + overnight soak. |
| 9 Release | S | |

## 9. Model & effort per phase

| Phase | Model | Effort | Reason |
|---|---|---|---|
| 1 PoC desktop integration | **Opus 5.5** | **xhigh** | Undocumented Win32/Explorer behaviour on 25H2; wrong choice here poisons everything after. |
| 2 Core organizer | Sonnet 5.5 | high | Large but well-specified code (models, config, shell launching, drag-drop). |
| 3 Liquid Glass UI | **Opus 5.5** | high | Visual judgement against references + WPF/DWM acrylic interop quirks. |
| 4 Tray, startup, Explorer recovery | **Opus 5.5** | high | State machine, hooks, race conditions, clean shutdown. |
| 5 Multi-monitor | **Opus 5.5** | high | DisplayConfig/EDID, DPI math, displacement logic edge cases. |
| 6 Backup & restore | Sonnet 5.5 | high | Clear spec, heavily unit-testable. |
| 7 Animation controls | Sonnet 5.5 | medium | Settings plumbing + storyboards through one helper. |
| 8 Performance & stability | Sonnet 5.5 → Opus if leaks found | high | Measuring is routine; diagnosing a leak/handle growth is not. |
| 9 Release & packaging | Sonnet 5.5 | medium | Inno Setup, GitHub Actions, docs. |

**One model for the whole project:** Opus 5.5 at **high** (xhigh for Phase 1 only).
Run one session per phase; each starts by reading `PLAN.md` + `DEVLOG.md`, so a fresh context is cheap.

## 10. Decisions needed from you

- **D1 — Monitors:** this PC has 1 screen. OK to verify 2/3-monitor behaviour via unit tests + a virtual display driver (e.g. the open-source "Virtual Display Driver"), or will you test on a multi-monitor PC?
- **D2 — Installer runtime:** framework-dependent (~5 MB, installer fetches .NET 10 Desktop Runtime if missing) **[recommended]** vs self-contained (~70 MB, no prerequisites).
- **D3 — Dragging a desktop shortcut into a folder:** keep the original on the desktop **[spec default]** or offer an opt-in setting "Move dropped desktop shortcuts into GlassFolders" (moved to `%APPDATA%\GlassFolders\Shortcuts`, restorable)?
- **D4 — Branching:** phase branches merged to `main` **[recommended]** or commit straight to `main`?
- **D5 — License:** MIT, proprietary/all-rights-reserved, or other?
