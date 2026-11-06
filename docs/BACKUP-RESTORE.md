# GlassFolders — Portable Backup & Restore Specification

This document details the architectural design, safety defenses, manifest schema, and path relinking heuristics used by GlassFolders' portable backup and restore engine (`GlassFolders.Core.Backup`).

---

## 1. Archive Structure

GlassFolders backup archives are standard ZIP archives with the `.zip` extension. Each archive contains a self-contained, portable snapshot of all folder configurations, layout positions, visual styles, and cached iconography:

```
backup-2026-11-07.zip
├── manifest.json       # App metadata, schema version, creation info, entry checksums
├── folders.json        # Serialized folder definitions, items, layout coordinates, appearance
└── icons/              # Cached high-resolution PNG icon assets
    ├── 61664BB521B5BA46.png
    ├── 9A91AEC6F3E189D1.png
    └── AB21FF4B8F55884F.png
```

---

## 2. Manifest Schema (`manifest.json`)

Every backup archive includes a root `manifest.json` file providing cryptographic integrity records and machine context:

```json
{
  "AppVersion": "1.0.0",
  "SchemaVersion": 1,
  "CreatedUtc": "2026-11-07T03:30:00.0000000Z",
  "MachineName": "DESKTOP-RIEKIE",
  "UserName": "rieki",
  "FolderCount": 3,
  "ShortcutCount": 8,
  "Monitors": [
    {
      "DeviceName": "\\\\.\\DISPLAY1",
      "Resolution": "1920x1080",
      "DpiScale": 1.0,
      "IsPrimary": true
    }
  ],
  "EntryChecksums": {
    "folders.json": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "icons/61664BB521B5BA46.png": "7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069"
  }
}
```

### Manifest Fields
- **`AppVersion`**: The version of GlassFolders that produced the backup.
- **`SchemaVersion`**: The data format version (currently `1`).
- **`CreatedUtc`**: ISO 8601 UTC timestamp of creation.
- **`MachineName` / `UserName`**: Context of the exporting system, utilized by the path relinker to identify foreign user directory paths.
- **`Monitors`**: Monitored screen geometries at time of export.
- **`EntryChecksums`**: Dictionary mapping relative entry paths to their SHA-256 cryptographic hashes for tamper and corruption detection.

---

## 3. Security & Validation Defenses (`ZipSafety`)

Untrusted ZIP archives pose distinct security risks. The `ZipSafety` validator enforces strict constraints on every entry before extracting:

### 1. ZipSlip (Directory Traversal) Protection
- Rejects any entry whose normalized path contains path traversal sequences (`..`).
- Rejects entries with rooted paths (`C:\...`, `/...`, `\\...`).
- Guarantees extracted files remain strictly within the target temporary staging directory.

### 2. Decompression Bomb Protection
- **Max Uncompressed Size:** 50 MB total uncompressed volume across the entire archive.
- **Max Entry Count:** 500 files maximum.
- **Max Ratio:** Guarded streaming extraction aborts if compressed-to-uncompressed expansion ratio exceeds 100:1.

### 3. Executable Payload Rejection
- GlassFolders backup archives only store configuration data and image assets.
- Any archive containing executable extensions is immediately rejected during pre-flight validation:
  - Executables: `.exe`, `.dll`, `.com`, `.scr`, `.msi`
  - Scripts: `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.wsf`
  - Shell / System: `.lnk`, `.sys`, `.drv`, `.cpl`
- Icons must strictly match `.png` or `.ico` extensions.

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
1. Extract ZIP to an isolated temporary sandbox (`%TEMP%\GlassFolders\import_<guid>`).
2. Verify `manifest.json` schema version and validate SHA-256 entry checksums.
3. Validate `ZipSafety` rules.
4. Execute `PathRelinker` on all items, compiling a report of resolved vs. missing items.
5. Identify folder ID collisions against the active configuration.

### Stage 2: Application Mode
- **Replace Mode:** Completely replaces the current desktop layout and folders with the archive contents.
- **Merge Mode:** Preserves existing folders. Conflicting folder IDs are regenerated with new GUIDs to prevent overwrites, appending imported folders to the current desktop.

---

## 6. Rolling Pre-Action Backups

To ensure users never experience accidental configuration loss:
- Before executing any Import, Reset, or Bulk Modification, GlassFolders automatically writes a complete snapshot into `%APPDATA%\GlassFolders\backups\pre-import-<timestamp>.zip`.
- GlassFolders maintains up to **10 rolling pre-action backups**, automatically purging the oldest snapshots when the limit is reached.
- Users can revert to any prior snapshot directly through the Settings interface.
