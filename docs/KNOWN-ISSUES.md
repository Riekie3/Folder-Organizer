# Known issues and limitations

## Desktop integration

- **Undocumented shell structure.** Desktop integration relies on Explorer's `Progman` / `SHELLDLL_DefView` windows, which Microsoft doesn't document and changed in 24H2. All of it is isolated in `GlassFolders.Native/DesktopWindows.cs` and the `IDesktopHost` implementations; `tools/DesktopProbe` shows the tree on any new build.
- **Brief window during Explorer restart.** Between Explorer dying and the folders being re-owned (~0.75 s), the folder windows have no owner. They may briefly show above other windows during that gap.
- **Windows doesn't know where the folders are.** New folders go into free cells of the desktop icon grid, but Windows can later put a new desktop icon under a folder, and **Auto arrange icons** can move icons under one. Drag the folder to move it; it snaps to a free cell.
- **After a scale change Explorer re-flows its icons.** GlassFolders moves its folders with the scale and fits them to the new grid about two seconds later, so for a moment a folder can sit on an icon.
- **Folders saved before Stage 3 keep their spot.** They are not placed again, even if they sit over icons, as the old default on the right edge did. Drag them to move them.
- **Folder pictures aren't in backups.** A restored folder whose picture is missing shows the drawn folder instead.
- **"Child of Progman" variant is broken on 25H2.** Folders are invisible but still capture clicks. Rejected; kept only as `--host=child-progman` for future comparison.
- **Verifier can't see pixels.** `PocVerifier` checks window structure and hit-testing, not what's drawn. It passed the invisible variant above. Rendering must be confirmed from the screenshots it saves (`--shot=`; kept local under `docs/poc/`, git-ignored).

## Glass

- **The glass is a snapshot.** It's taken when the popup opens and doesn't update while it stays open, so a video behind the popup looks frozen in the blur.
- **No open or close animation?** GlassFolders follows Windows' animation effects setting by default. Turn that on in Windows, or turn off **Respect Windows 'Animation effects' setting** under **Settings → Animations**.
- **Protected video may capture as black.** If the whole capture is black, the popup uses the solid panel. If only part of it is, that part of the glass is dark.
- **Settings glass is a snapshot of one monitor.** Dragging Settings or a dialog to another monitor shows the solid panel until it comes back. Moving the window over other windows doesn't update the blur.
- **A Settings window that was open when Transparency effects went off turns solid, and stays solid** until it is closed and opened again.
- **Menus are not frosted.** They are small windows that can't carry a snapshot, so they use a nearly opaque tint.
- **The popup keeps its blue glass in light mode.** The Theme setting changes Settings, menus and dialogs only.
- **The folder plate follows the wallpaper file.** It updates when Windows announces a wallpaper change. A wallpaper Windows doesn't report as a file, or a moved file with no saved copy, leaves the plate tinted without the blurred picture.

## Not tested on a real desktop

Stage 5 and Stage 6 did not cover these, so expect surprises:

- **A second monitor**, including a popup that opens on it.
- **A popup at the right edge of the screen.** Top-left and bottom-centre were checked; the right edge was hidden by another window.
- **Other wallpapers.** The folder plate glass was checked on the PC's own wallpaper and on two drawn ones off-screen.
- **Choose Picture with a real file** and the real Win+M shortcut.
- **A video playing behind the popup**, which the snapshot freezes.
- **Memory.** Private memory is about 110 MB after use, flat over 20 opens, against a 60 MB target.

## Not yet verified (Phase 1)

| Item | Why | Plan |
|---|---|---|
| Two or three monitors | Dev PC has one screen | Virtual display driver or a second screen (decision D1); Phase 5 |
| Display scaling change at runtime (100/125/150 %) | Checked live in Stage 5 (primary monitor) | Folders follow the scale and re-fit to the icon grid. A folder on a second monitor with a different scale isn't rescaled |
| Task View / virtual desktops | Not automated | Manual check |
| Win+M (minimise all) | Not tried with the real shortcut | A folder that gets minimised by any tool is shown again at once (checked with ShowWindow); Show Desktop leaves the folders alone |
| Sleep / resume, sign-out / sign-in | Not automated | Phase 4 |
