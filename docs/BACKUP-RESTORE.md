# GlassFolders — Portable Backup & Restore Specification

This document details the architectural design, safety defenses, manifest schema, and path relinking heuristics used by GlassFolders' portable backup and restore engine (`GlassFolders.Core.Backup`).

---

## 1. Archive Structure

GlassFolders backup archives are standard ZIP archives with the `.zip` extension. Each archive contains a self-contained, portable snapshot of all folder configurations, layout positions, visual styles, and cached iconography:

```
GlassFolders-Backup-2026-11-07.zip
├── manifest.json       # App metadata, schema version, creation info, checksums, folder → picture map
├── folders.json        # Folder definitions, items, layout coordinates, appearance
├── settings.json       # App settings (theme, appearance, animations, layout)
├── icons/              # Cached high-resolution PNG icon assets
│   ├── 61664BB521B5BA46.png
│   ├── 9A91AEC6F3E189D1.png
│   └── AB21FF4B8F55884F.png
└── folder-icons/       # Pictures a folder shows instead of the drawn folder
    ├── 4F2A9C0E7B1D3358.png
    └── C81B6D4A2E90F7AC.jpg
```

`settings.json`, `icons/` and `folder-icons/` are optional. An archive without `settings.json`, such as one from an older release, imports with the built-in defaults. Pictures are named after a hash of the folder id rather than the user's own file name, and `manifest.json` maps each folder to its picture, so a restore does not depend on paths from the exporting PC.

---

## 2. Manifest Schema (`manifest.json`)

The manifest is camelCase JSON and carries the archive's integrity records and machine context:

```json
{
  "appVersion": "1.1.3",
  "schemaVersion": 1,
  "createdAt": "2026-11-07T03:30:00.0000000Z",
  "machineName": "DESKTOP-RIEKIE",
  "monitors": ["DISPLAY1"],
  "fileChecksums": {
    "folders.json": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "settings.json": "7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069",
    "icons/61664BB521B5BA46.png": "3f79bb7b435b05321651daefd374cdc681dc06faa65e374e38337b88ca046dea",
    "folder-icons/4F2A9C0E7B1D3358.png": "c2356069e9d1e79ca924378153cfbbfb4d2f2b7e1b8b8b4bfc7d3ed1c8f8d4a1"
  },
  "folderIcons": {
    "3fa2c1d9": "folder-icons/4F2A9C0E7B1D3358.png"
  }
}
```

### Manifest Fields
- **`appVersion`**: The version of GlassFolders that produced the backup, read from the assembly.
- **`schemaVersion`**: The backup format version. Currently `1`; an archive from a newer format is refused rather than partially read.
- **`createdAt`**: ISO 8601 UTC timestamp of creation.
- **`machineName`**: The exporting PC's name, shown in the import confirmation.
- **`monitors`**: The monitor keys the layout was saved against (for example `DISPLAY1`). Informational; the layout is stored as relative positions, so it does not need the same screens to import.
- **`fileChecksums`**: Dictionary mapping relative entry paths to their SHA-256 hashes. Every listed entry that is present in the archive is verified before import.
- **`folderIcons`**: Dictionary mapping a folder id to the archive entry holding the picture the folder shows. Empty for folders that use a drawn icon.

`folders.json` and `settings.json` use the same models and schema version fields as the app's own configuration files.

---

## 3. Security & Validation Defenses (`ZipSafety`)

Untrusted ZIP archives pose distinct security risks. `ZipSafety.ValidateArchive` runs before anything in the archive is read, and every entry must pass.

### 1. ZipSlip (Directory Traversal) Protection
- Rejects any entry whose normalized path contains path traversal (`..`), starts with `/`, or uses a Windows drive letter (`C:`).
- Accepts only the known relative entry names listed below, so no path from the archive is ever used to write a file.

### 2. Decompression Bomb Protection
- **Max Uncompressed Size:** 50 MB total uncompressed volume across the entire archive.
- **Max Entry Count:** 500 files maximum.
- **Max Ratio:** any entry whose declared uncompressed size is more than 100 times its compressed size is rejected as a bomb.
- **Bounded Reads:** `ZipSafety.ReadAllBytes` counts while it reads instead of trusting the declared length, so a hand-made entry cannot expand past its limit (50 MB for JSON, 10 MB for a folder picture).

### 3. Allowed Entry Types
GlassFolders backup archives hold configuration data and pictures only. An entry with any other name or extension rejects the archive instead of being skipped:

- **Root:** `manifest.json`, `folders.json`, `settings.json`
- **`icons/`:** `.png`, `.ico`
- **`folder-icons/`:** `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.ico`, `.tif`, `.tiff`

Executable and script extensions are also rejected explicitly: `.exe`, `.dll`, `.com`, `.scr`, `.msi`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.cpl`, `.sys`, `.drv`. The allow-list rejects the rest, including `.js`, `.wsf` and `.lnk`.

A folder picture is capped at 10 MB, the same limit Choose Picture uses. Export leaves out a larger or unreadable picture; import ignores such an entry. A folder whose picture was skipped comes back with its drawn icon in Replace mode, and keeps the picture it already shows in Merge mode.

---

## 4. Smart Path Relinker Heuristics (`PathRelinker`)

When migrating backups between machines or different Windows accounts, hardcoded file paths (e.g., shortcuts pointing to `C:\Users\Alice\AppData\...`) break on a machine where the active user is `Bob`.

The `PathRelinker` component analyzes all shortcut targets during import:

### Heuristic Steps
1. **Target Verification:** Tests if the current target path exists on the local machine. If valid, no transformation is needed.
2. **Username Remapping:** Detects path prefixes matching `C:\Users\<SourceUser>\` and swaps with the current `%USERPROFILE%` (`C:\Users\<CurrentActiveUser>\`).
3. **Environment Variable Expansion:**
   - `%LOCALAPPDATA%` (`AppData\Local`)
   - `%APPDATA%` (`AppData\Roaming`)
   - `%PROGRAMFILES%` / `%PROGRAMFILES(X86)%`
   - `%SYSTEMROOT%` / `%WINDIR%`
4. **Standard System Utilities:** Matches common tools in `System32` (e.g. `cmd.exe`, `powershell.exe`, `notepad.exe`, `calc.exe`, `mspaint.exe`, `taskmgr.exe`).
5. **Browser & Common App Discovery:** Checks well-known installation paths for common applications (Microsoft Edge, Google Chrome, Mozilla Firefox, VS Code, Discord, Spotify).

If a relinked candidate path exists on disk, it is updated in the imported configuration. If no valid path is found, the item is imported with a missing-target indicator in Settings, allowing manual reconfiguration.

---

## 5. Import Planner & Conflict Resolution (`ImportPlanner`)

The import workflow follows a strict two-stage process:

### Stage 1: Pre-Flight Analysis
1. Open the ZIP in place. Nothing is extracted to a staging folder; entries are read through bounded streams.
2. Run `ZipSafety` on every entry.
3. Read and deserialize `manifest.json` and `folders.json`, then `settings.json` when present. A manifest from a newer schema version is refused rather than partially read.
4. Verify the SHA-256 hash of every entry listed in `manifest.fileChecksums`.
5. Read the pictures listed in `manifest.folderIcons` (up to 10 MB each) into memory. A folder without a picture entry has its saved path cleared, because a picture path from another PC is not usable here.
6. Execute `PathRelinker` on all items, compiling a report of resolved vs. missing items.
7. Identify folder ID collisions against the active configuration.

### Stage 2: Application Mode
1. Write an automatic pre-import backup of the current setup.
2. Write imported pictures into the app's data folder (`%APPDATA%\GlassFolders\folder-icons`, or `data\folder-icons` in portable mode) and point the imported folders at those copies, before any configuration is saved. A failed write leaves the current configuration unchanged.
3. Apply the import:
   - **Replace Mode:** replaces the current folders and settings with the archive contents, then reloads. This is the mode that restores Settings along with the desktop.
   - **Merge Mode:** keeps the current folders and settings. A folder with the same ID and name merges the archive's unique items into the existing folder. A folder with the same ID but a different name gets a new ID so it cannot overwrite the existing one. An imported picture replaces the existing picture only when the archive has one.

---

## 6. Rolling Pre-Action Backups

To ensure users never experience accidental configuration loss:
- Before executing any Import, Reset, or Bulk Modification, GlassFolders automatically writes a complete snapshot into the app's `backups` folder (`%APPDATA%\GlassFolders\backups`, or `data\backups` in portable mode) as `pre-<reason>-<timestamp>.zip`.
- GlassFolders maintains up to **10 rolling pre-action backups**, automatically purging the oldest snapshots when the limit is reached.
- Users can revert to any prior snapshot directly through the Settings interface.
