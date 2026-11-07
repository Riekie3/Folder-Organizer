# <img src="assets/app-icon.png" width="36" height="36" align="center" alt="GlassFolders Icon"/> GlassFolders

Phone-style app folders on your real Windows 11 desktop, with a Liquid Glass popup.

> **Status: Production Release (`v1.0.1`) complete.** Built for Windows 11 (including 24H2 & 25H2). Fully tested (66/66 automated tests passed), zero-leak memory profile, event-driven Explorer recovery, and zero runtime dependencies beyond .NET 10.

![GlassFolders Desktop Experience](docs/screenshots/hero-desktop.png)

---

## Screenshots

| Liquid Glass Folder Popup | Settings Dashboard |
|:---:|:---:|
| ![Liquid Glass Folder Popup](docs/screenshots/folder-popup.png) | ![Settings Dashboard](docs/screenshots/settings.png) |
| *Frosted DWM Acrylic popup with real app icons, pagination, and category glyph* | *Glass sidebar navigation with full personalization, layout, and recovery controls* |

| Folder Appearance Customization | Granular Animation Controls |
|:---:|:---:|
| ![Folder Customization Dialog](docs/screenshots/folder-customization.png) | ![Animation Controls](docs/screenshots/settings-animations.png) |
| *Real-time vector preview, dual-stop color gradients, and category glyph picker* | *8 independent category toggles, speed slider, and Windows reduced-motion sync* |

---

## Overview

**GlassFolders** brings the mobile home-screen folder experience to Windows 11 without hacky overlays or third-party bloat. Folders like *Work*, *Games*, *Media*, and *Utilities* sit directly on your desktop layer alongside standard icons. Clicking a folder opens a frosted DWM Acrylic glass panel displaying real application icons, shortcuts, and files.

- **Native Desktop Integration:** Each folder is a small, borderless Progman-owned window pinned below normal applications. Desktop rubber-band selection, desktop icons, and Win+D ("Show Desktop") continue to work seamlessly.
- **Liquid Glass Interface:** Real Windows 11 DWM Acrylic (`DWMSBT_TRANSIENTWINDOW`) and Mica composition, subtle 1px specular highlight rims, customizable vector category glyphs, and dynamic dark mode.
- **Automated Explorer Recovery:** Event-driven state machine (`Healthy → ShellLost → WaitingForShell → Reattaching → Verifying`) automatically recovers desktop surfaces with exponential backoff on Explorer crashes, taskbar recreation, session locks, and power resumes.
- **Multi-Monitor Layouts:** Position math uses work-area relative fractions and persistent monitor identities. When a secondary screen is unplugged, folders gracefully displace to the primary monitor and return to their home screen when reconnected.
- **Portable Backup & Restore:** Self-contained `.zip` backup format with SHA-256 manifests, automated rolling pre-restore backups (max 10), collision-free merge mode, and intelligent path relinking for cross-PC migration.
- **Granular Animation Controls:** Master toggle, Windows reduced-motion (`SPI_GETCLIENTAREAANIMATION`) compliance, speed slider (0.5×–2.0×), and 8 independent animation category switches.
- **Ultra-Lightweight & Zero-Polling:** 0.00% idle CPU overhead, no periodic timer wakeups, and automated working set memory trimming (~50–65 MB typical).
- **System Tray Integration:** Runs quietly in the notification area with a custom glass context menu; no taskbar button.

> **Safety Guarantee:** Removing an item or deleting a folder from GlassFolders **never** deletes the original application or file from your computer.

---

## Technical Architecture

- **Runtime:** .NET 10 (LTS) WPF with Per-Monitor-V2 DPI awareness.
- **Platform:** Windows 11 (22H2, 23H2, 24H2, 25H2+) and Windows 10 64-bit.
- **Compositor:** Direct Desktop Window Manager (DWM) Acrylic blur and Mica backdrops.
- **Packaging:** Inno Setup per-user installer (`{localappdata}\Programs\GlassFolders`, no administrator privileges required) and standalone portable ZIP distribution.

---

## Building from Source

Requires Windows 11 and the **.NET 10 SDK**.

```powershell
# Clone the repository
git clone https://github.com/Riekie3/Folder-Organizer.git
cd "Folder-Organizer"

# Build solution in Release mode
dotnet build GlassFolders.sln --configuration Release -warnaserror

# Run comprehensive test suite (66 tests)
dotnet test GlassFolders.sln --configuration Release

# Launch GlassFolders
.\src\GlassFolders.App\bin\Release\net10.0-windows\GlassFolders.exe
```

---

## Documentation

- [User Guide](docs/USER-GUIDE.md) — Shortcut management, appearance customization, and settings.
- [Architecture Documentation](docs/ARCHITECTURE.md) — Window hierarchy, Progman ownership, and DWM composition details.
- [Backup & Restore Guide](docs/BACKUP-RESTORE.md) — Portable ZIP schema, zip safety, and path relinking.
- [Performance & Stability Report](docs/PERFORMANCE.md) — Benchmarks, working set metrics, and idle CPU analysis.
- [Known Issues & Quirks](docs/KNOWN-ISSUES.md) — Windows 11 25H2 desktop behavior and workarounds.
- [Changelog](CHANGELOG.md) — Complete milestone release history.
- [Development Log](DEVLOG.md) — Phase-by-phase implementation journal.

---

## License

GlassFolders is open-source software licensed under the [MIT License](LICENSE). Copyright © 2026 Riekie3.
