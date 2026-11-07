# GlassFolders — User Guide

Welcome to **GlassFolders**, a native Windows 11 desktop organizer that brings elegant, iOS/Android-style app folders to your desktop background with real DWM Acrylic/Mica effects, multi-monitor intelligence, and zero idle overhead.

---

## Table of Contents
1. [Getting Started](#getting-started)
2. [Folder Basics](#folder-basics)
3. [Managing Shortcuts & Apps](#managing-shortcuts--apps)
4. [Customizing Appearance & Glass Effects](#customizing-appearance--glass-effects)
5. [Multi-Monitor Workflows](#multi-monitor-workflows)
6. [Animations & Accessibility](#animations--accessibility)
7. [Backup, Restore & Portability](#backup-restore--portability)
8. [System Tray & Quick Controls](#system-tray--quick-controls)
9. [Troubleshooting & Recovery](#troubleshooting--recovery)

---

## 1. Getting Started

### Installation
- Run `GlassFolders-Setup-1.0.0.exe`.
- The installer installs per-user into `%LOCALAPPDATA%\Programs\GlassFolders` without requiring administrative privileges (UAC).
- Optionally check **Start GlassFolders when Windows starts** during installation.

### Launching
- Launch **GlassFolders** from your Start Menu or Desktop shortcut.
- GlassFolders runs quietly on your desktop layer and adds a glossy folder icon to your **System Tray** (near the clock in the bottom-right taskbar).
- If you launch `GlassFolders.exe` a second time, it automatically signals the running instance and opens the **Settings** window.

---

## 2. Folder Basics

### Desktop Folders
Folders appear as sleek frosted glass tiles anchored directly to your desktop layer:
- **Never In Your Way:** Desktop folders stay permanently behind normal application windows.
- **Show Desktop (Win+D):** Pressing `Win+D` shows your desktop with your GlassFolders fully visible and interactive.
- **Desktop Marquee & Icons:** You can still click desktop shortcuts, drag marquee selection boxes, and right-click the desktop wallpaper.

### Opening a Folder
- Click any folder tile on your desktop to open its smooth frosted acrylic popup menu.
- Click outside the folder popup or press `Escape` to close it.

### Moving Folders
- Simply **click and hold** on any folder tile and drag it to your desired position on your desktop.
- Dragging works seamlessly across multiple displays with automatic grid snapping.

### Creating a New Folder
1. Right-click the **GlassFolders System Tray icon** and select **New Folder...**, OR open **Settings → Folders → Create New Folder**.
2. Give your folder a name (e.g. *Dev Tools*, *Productivity*, *Games*).
3. Pick a category glyph (folder, code, terminal, gamepad, globe, briefcase, etc.) and a two-stop glass color gradient.
4. Click **Create Folder**. Your new folder tile instantly appears on your primary desktop.

---

## 3. Managing Shortcuts & Apps

### Adding Items to a Folder
1. Click the folder to open its popup window.
2. Click the **+ Add Shortcut** button at the bottom of the popup, or open **Settings → Folders → Edit**.
3. Choose the target file:
   - **Desktop / Start Menu Shortcuts (`.lnk`)**: Automatically resolves target paths, icons, and arguments.
   - **Executables (`.exe`, `.cmd`, `.bat`)**: Direct path execution.
   - **UWP Apps & System Utilities**: Use shell shortcuts (e.g. `shell:AppsFolder\...` or `ms-settings:`).
   - **Web URLs**: Directly enter web links (e.g. `https://github.com`).
4. GlassFolders extracts high-resolution icons automatically and caches them locally.

### Launching an Item
- Open the folder popup and click any item tile.
- The target application launches immediately using Windows Shell Execution.

### Reordering & Removing Items
- In the folder popup, right-click any item to view options:
  - **Launch**: Runs the item.
  - **Edit**: Adjust display name or target path.
  - **Remove**: Deletes the item from the folder (does not delete the application from your PC).

---

## 4. Customizing Appearance & Glass Effects

Open **Settings → Appearance** to customize global and per-folder aesthetics:

- **Backdrop Effect**: Choose between **DWM Acrylic** (deep frosted blur with transient light reflection) or **Mica** (subtle surface tint integrating with your wallpaper).
- **Color Gradients**: Configure dual-stop linear gradients to colorize your folder tiles (Azure Blue, Sunset Orange, Emerald Green, Royal Purple, Rose Gold, or Custom Hex).
- **Opacity & Blur**: Adjust glass opacity (from subtle 30% translucent tint to 95% solid frosted glass) and blur radius.
- **Dark Mode**: Automatically harmonizes with your Windows system theme (Dark or Light).

---

## 5. Multi-Monitor Workflows

GlassFolders includes built-in relative coordinate mapping:

- **Move Between Monitors**: Drag a folder across display boundaries, or right-click the folder and choose **Move to Monitor ▸** to send it directly to a specific display.
- **Disconnecting Monitors**: When a secondary monitor is unplugged or powered down, GlassFolders automatically displaces its folders to your primary display so your shortcuts are never lost off-screen.
- **Reconnecting Monitors**: As soon as Windows detects the secondary monitor again, displaced folders automatically jump back to their saved positions on that display.
- **Heterogeneous DPI**: If you move folders between displays with different scale factors (e.g. 100% and 150%), folder dimensions and icons automatically rescale smoothly.

---

## 6. Animations & Accessibility

Open **Settings → Animations** to adjust motion dynamics:

- **Master Animation Toggle**: Turn all animations on or off with a single click.
- **Windows Reduced Motion**: When "Show animations in Windows" is disabled in Windows Settings, GlassFolders automatically respects your system preference and disables motion.
- **Speed Multiplier**: Adjust animation speed between 0.5× (slow & elegant) and 2.0× (ultra-snappy).
- **Granular Controls**: Independently enable or disable:
  - Folder Popup Open/Close
  - Tile Hover Glow
  - Tile Press Feedback
  - Item Launch Bounce
  - Settings Dialog Fade
  - Drag Physics & Snap
- **Zero-Wakeup Architecture**: When animations are disabled, UI elements snap immediately without running background timer threads, guaranteeing 0.00% idle CPU usage.

---

## 7. Backup, Restore & Portability

Open **Settings → Backup & Restore** to safeguard your desktop configuration:

### Creating an Export Archive
1. Click **Export Backup...**.
2. Choose where to save your `.zip` archive (e.g. your OneDrive, Dropbox, or USB flash drive).
3. The archive contains:
   - `manifest.json`: Configuration metadata and SHA-256 integrity checksums.
   - `folders.json`: All folders, shortcuts, layout coordinates, and appearance settings.
   - `icons/`: All cached custom icons and extracted shortcut graphics.

### Restoring from an Archive
1. Click **Import Backup...**.
2. Select your `.zip` backup archive.
3. Choose an import mode:
   - **Replace All**: Wipes current configuration and replaces it with the archive.
   - **Merge**: Adds new folders and preserves existing ones.
4. **Smart Path Relinker**: If restoring onto a new machine with a different Windows username, GlassFolders automatically detects path discrepancies (e.g. `C:\Users\OldUser\` → `C:\Users\NewUser\`) and repairs shortcut targets.
5. **Safety Backups**: GlassFolders automatically creates a pre-action backup snapshot before every import, allowing you to restore your previous layout at any time.

---

## 8. System Tray & Quick Controls

Right-clicking the **GlassFolders System Tray Icon** provides instant access:

| Menu Item | Action |
|---|---|
| **New Folder...** | Opens the creation dialog to make a new desktop folder |
| **Show/Hide Desktop Folders** | Quickly hides all folders for clean desktop screenshots |
| **Pause GlassFolders** | Suspends desktop rendering and minimizes working set |
| **Settings** | Opens the comprehensive GlassFolders management window |
| **Explorer Recovery ▸ Recover Now** | Manually re-attaches desktop surfaces to Windows Explorer |
| **About** | Displays version, publisher credits (Rie-Sources), and system environment |
| **Exit** | Gracefully cleans up desktop surfaces and shuts down the application |

---

## 9. Troubleshooting & Recovery

### Windows Explorer Restarted or Crashed
- If Windows Explorer restarts (e.g. after a graphics driver update or taskbar crash), GlassFolders' **ExplorerRecoveryService** automatically detects shell re-initialization and re-attaches all folder surfaces within ~750 milliseconds.
- If desktop surfaces ever fail to appear, right-click the system tray icon and select **Explorer Recovery → Recover Now**.

### Checking Diagnostic Logs
- GlassFolders maintains rolling logs in:  
  `%LOCALAPPDATA%\GlassFolders\logs\glassfolders.log`
- Up to 5 rolling files of 1 MB each are preserved.

### Resetting to Defaults
- If configuration files become corrupted, open **Settings → Backup & Restore** and click **Reset to Defaults**.
- Alternatively, launch GlassFolders with the `--reset` command line flag.

---

## 10. Credits & Publisher

- **Publisher**: [Rie-Sources](https://rie-sources.com/)
- **Website**: [https://rie-sources.com/](https://rie-sources.com/)
- **License**: MIT License
