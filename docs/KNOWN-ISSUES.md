# Known issues and limitations

## Desktop integration

- **Undocumented shell structure.** Desktop integration relies on Explorer's `Progman` / `SHELLDLL_DefView` windows, which Microsoft doesn't document and changed in 24H2. All of it is isolated in `GlassFolders.Native/DesktopWindows.cs` and the `IDesktopHost` implementations; `tools/DesktopProbe` shows the tree on any new build.
- **Brief window during Explorer restart.** Between Explorer dying and the folders being re-owned (~0.75 s), the folder windows have no owner. They may briefly show above other windows during that gap.
- **Folders can overlap desktop icons.** The PoC places folders at fixed default spots, which can cover existing icons (seen on the dev PC's right-hand icon column). Phase 2 must place new folders in free space by reading icon positions from the desktop list view.
- **"Child of Progman" variant is broken on 25H2.** Folders are invisible but still capture clicks. Rejected; kept only as `--host=child-progman` for future comparison.
- **Verifier can't see pixels.** `PocVerifier` checks window structure and hit-testing, not what's drawn. It passed the invisible variant above. Rendering must be confirmed from the screenshots it saves (`--shot=`; kept local under `docs/poc/`, git-ignored).

## Glass

- **The glass is a snapshot.** It's taken when the popup opens and doesn't update while it stays open, so a video behind the popup looks frozen in the blur.
- **No open or close animation?** GlassFolders follows Windows' animation effects setting by default. Turn that on in Windows, or turn off **Respect Windows 'Animation effects' setting** under **Settings → Animations**.
- **Protected video may capture as black.** If the whole capture is black, the popup uses the solid panel. If only part of it is, that part of the glass is dark.
- **No blur in Settings or the folder editor yet.** They use a tinted panel until Stage 4 of the parity plan.

## Not yet verified (Phase 1)

| Item | Why | Plan |
|---|---|---|
| Two or three monitors | Dev PC has one screen | Virtual display driver or a second screen (decision D1); Phase 5 |
| Display scaling change at runtime (100/125/150 %) | Needs manual Settings change | Manual check; full handling in Phase 5 |
| Task View / virtual desktops | Not automated | Manual check |
| Win+M (minimise all) | Not automated | Manual check; expected unaffected (no minimise box) |
| Sleep / resume, sign-out / sign-in | Not automated | Phase 4 |
