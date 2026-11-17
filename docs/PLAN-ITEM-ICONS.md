# Plan: change an item's icon inside a folder

**Status: planned, not started.** Nothing in this plan is built. It is written down so the work can be agreed before any code changes.

## What exists today

Right-clicking an item in a folder popup offers: **Open**, **Open file location**, **Rename...** and **Remove from folder**. Rename changes only the label. The icon always comes from Windows: the shortcut first, then the app's own artwork, then the file itself.

The data model already has `IconLocation` and `IconIndex` on every item, but they only record what the shortcut pointed at. They are used as a last-resort fallback and nothing in the interface sets them. Folders on the desktop can already take a picture of their own (up to 10 MB, included in backups); items inside a folder cannot.

## Goal

Let the user give any item in a folder their own icon, and put it back to the Windows icon when they want.

## Behaviour

1. **Change icon...** is added to the item menu, between **Rename...** and **Remove from folder**.
2. It opens a file picker for images (`.png`, `.ico`, `.jpg`, `.jpeg`, `.bmp`, `.webp`) and for files that hold icons (`.exe`, `.dll`, `.ico`). For an `.exe` or `.dll` with several icons, the user picks one from a small list.
3. The chosen picture is **copied into the app's own data folder** and the item points at the copy, so moving or deleting the original file does not break the icon. This is the same approach folder pictures use.
4. **Reset icon** appears in the menu only when the item has a custom icon, and returns it to the Windows icon.
5. A custom icon is never replaced by the automatic icon refresh, and survives renaming.
6. Custom icons are included in backups and restored with them, with the same checks as folder pictures (a size limit, supported image types only, a checksum in the manifest). Merge keeps an icon the item already has unless the archive supplies one.
7. The icon change follows the animation settings like the rest of the popup.

## Recommended icon size

**Use a square PNG of 256 x 256 pixels with a transparent background**, or an `.ico` that contains a 256 x 256 image.

Why that size:

- Windows icons are fetched at up to 256 px, and the popup draws items at 56, 67 or 73 DIP (Small, Medium, Large at the popup's 70 % scale). On screen that is about 70 to 91 px at 125 % scaling, 84 to 109 px at 150 % and 112 to 146 px at 200 %.
- 256 px stays sharp up to roughly 350 % scaling, so one file suits every display.
- **Minimum:** 128 x 128. Smaller pictures look soft on high-scaling screens.
- **Keep the artwork inside the middle ~90 %** of the square so it does not touch the edge, and use a transparent background so the glass shows through.
- Non-square pictures are fitted inside a square, not stretched.

A custom icon is treated as full-size artwork. The automatic "low resolution" handling that puts tiny app icons on a tile does not apply to it.

## Work to do

1. **Model:** add `CustomIconPath` to `FolderItem` and bump the folders schema version with a migration that leaves old files unchanged.
2. **Storage:** a small store that validates the file (size limit, real image, decodable), copies it into the app data folder under an id, and removes it when the item or folder is deleted or the icon is reset.
3. **Icon cache:** include the custom icon in the cache key, so changing it refreshes only that item, and load it ahead of the shell icon.
4. **Popup menu:** **Change icon...** and **Reset icon**, with the file picker and the icon list for `.exe` / `.dll`.
5. **Backup and restore:** write and read `item-icons/` in the archive with manifest checksums; extend the zip safety rules to allow only that folder's image types.
6. **Tests:** model migration, validation (oversize, not an image, empty), cache key changes, backup round trip, merge versus replace.
7. **Docs:** User Guide, Backup & Restore guide, README and CHANGELOG.
8. **Check on screen** before release: choose an icon, restart the app, back up and restore, reset.

## Open questions

- Should a custom icon also be offered when adding an item, or only afterwards from the menu? (Proposed: afterwards only.)
- Should there be an optional **tile behind the icon** setting for pictures with no padding? (Proposed: not in the first version.)
- Maximum file size for an item icon. (Proposed: 2 MB, since 256 x 256 images are small; folder pictures keep 10 MB.)

## Out of scope for this change

Changing the folder's own picture (already possible), editing or cropping images inside the app, and icon packs.
