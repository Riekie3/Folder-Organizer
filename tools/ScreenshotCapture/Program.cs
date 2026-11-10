using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GlassFolders.App.Poc;
using GlassFolders.App.Services;
using GlassFolders.App.UI;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using static GlassFolders.Native.NativeMethods;
using Path = System.IO.Path;

namespace ScreenshotCapture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Never run as a startup program: this tool only produces README images.
        if (args.Contains("--tray")) return;

        try
        {
            Console.WriteLine("Initializing WPF application context...");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/Tokens.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/FolderGlyphs.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/Controls.xaml") });

            string outputDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindDefaultOutputDir();
            Directory.CreateDirectory(outputDir);

            // Isolated, throw-away config: this tool must never touch the user's real folders or settings.
            string sandbox = Path.Combine(Path.GetTempPath(), "GlassFolders-ScreenshotCapture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            var configManager = new ConfigurationManager(sandbox);
            var folderManager = new FolderManager(configManager);
            var iconCache = new IconCacheService();
            var resolver = new ShellShortcutResolver();

            // Setup sample folders with realistic shortcuts
            var workFolder = SetupWorkFolder(folderManager);
            var gamesFolder = SetupGamesFolder(folderManager);
            var mediaFolder = SetupMediaFolder(folderManager);

            AppearanceService.Instance.Apply(new AppSettings { Theme = AppTheme.Dark });
            nint primary = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
            RECT monitor = Win32.MonitorInfo(primary).rcMonitor;
            double scale = ScreenCapture.MonitorScale(primary);

            // Needs the screen for about ten seconds: do not touch the mouse or keyboard meanwhile.
            Console.WriteLine("Showing the neutral wallpaper window...");
            var wallpaper = CreateWallpaper(monitor, scale);
            wallpaper.Show();
            wallpaper.Activate();
            Pump(700);

            Console.WriteLine("Capturing the desktop and the folder popup...");
            CaptureDesktop(wallpaper, [workFolder, gamesFolder, mediaFolder], scale, monitor, folderManager, iconCache, resolver,
                Path.Combine(outputDir, "hero-desktop.png"), Path.Combine(outputDir, "folder-popup.png"));

            Console.WriteLine("Capturing Settings (General, Animations, About)...");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings.png"), "General");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings-animations.png"), "Animations");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings-about.png"), "About");

            Console.WriteLine("Capturing the folder editor...");
            CaptureFolderEditor(workFolder, Path.Combine(outputDir, "folder-customization.png"));

            wallpaper.Close();
            Console.WriteLine("All screenshots generated successfully in: " + outputDir);
            app.Shutdown();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL ERROR: " + ex);
        }
    }

    private static string FindDefaultOutputDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "GlassFolders.sln")))
                return Path.Combine(dir.FullName, "docs", "screenshots");
        return Path.Combine(Environment.CurrentDirectory, "screenshots");
    }

    private static FolderDef SetupWorkFolder(FolderManager fm)
    {
        var existing = fm.Folders.FirstOrDefault(f => f.Name == "Work");
        var folder = existing ?? fm.CreateFolder("Work", "#7FB2FF", "#5B5CF2", "Briefcase");
        folder.ColorA = "#7FB2FF";
        folder.ColorB = "#5B5CF2";
        folder.Glyph = "Briefcase";

        if (folder.Items.Count < 6)
        {
            folder.Items.Clear();
            // Windows' own programs only, so every icon is real on any PC and none shows the missing badge.
            AddSystemApp(folder, "Notepad", @"notepad.exe", "notepad.exe");
            AddSystemApp(folder, "Calculator", @"calc.exe", "calc.exe");
            AddSystemApp(folder, "Command Prompt", @"cmd.exe", "cmd.exe");
            AddSystemApp(folder, "Task Manager", @"taskmgr.exe", "taskmgr.exe");
            AddSystemApp(folder, "Remote Desktop", @"mstsc.exe", "mstsc.exe");
            AddSystemApp(folder, "Character Map", @"charmap.exe", "charmap.exe");
            AddSystemApp(folder, "Magnifier", @"magnify.exe", "magnify.exe");
            AddSystemApp(folder, "On-Screen Keyboard", @"osk.exe", "osk.exe");
            AddSystemApp(folder, "File Explorer", @"explorer.exe", "explorer.exe", windowsDir: true);
            AddSystemApp(folder, "System Info", @"msinfo32.exe", "msinfo32.exe");
            AddSystemApp(folder, "Resource Monitor", @"resmon.exe", "resmon.exe");
            AddSystemApp(folder, "Disk Cleanup", @"cleanmgr.exe", "cleanmgr.exe");
        }
        return folder;
    }

    private static FolderDef SetupGamesFolder(FolderManager fm)
    {
        var existing = fm.Folders.FirstOrDefault(f => f.Name == "Games");
        var folder = existing ?? fm.CreateFolder("Games", "#C38BFF", "#7B3FE8", "Gamepad");
        folder.ColorA = "#C38BFF";
        folder.ColorB = "#7B3FE8";
        folder.Glyph = "Gamepad";
        if (folder.Items.Count == 0)
        {
            AddSystemApp(folder, "Steam", @"Steam\steam.exe", "steam.exe");
            AddSystemApp(folder, "Discord", @"Discord\Update.exe", "discord.exe");
            AddSystemApp(folder, "Epic Games", @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", "EpicGamesLauncher.exe");
            AddSystemApp(folder, "Xbox", @"XboxApp.exe", "XboxApp.exe");
        }
        return folder;
    }

    private static FolderDef SetupMediaFolder(FolderManager fm)
    {
        var existing = fm.Folders.FirstOrDefault(f => f.Name == "Media");
        var folder = existing ?? fm.CreateFolder("Media", "#67E8F9", "#0891B2", "Play");
        folder.ColorA = "#67E8F9";
        folder.ColorB = "#0891B2";
        folder.Glyph = "Play";
        if (folder.Items.Count == 0)
        {
            AddSystemApp(folder, "Spotify", @"Spotify\Spotify.exe", "Spotify.exe");
            AddSystemApp(folder, "VLC Player", @"VideoLAN\VLC\vlc.exe", "vlc.exe");
            AddSystemApp(folder, "OBS Studio", @"obs-studio\bin\64bit\obs64.exe", "obs64.exe");
            AddSystemApp(folder, "Photos", @"Photos.exe", "Photos.exe");
        }
        return folder;
    }

    private static void AddSystemApp(FolderDef folder, string name, string relPath, string candidateName, bool windowsDir = false)
    {
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sys32Path = Path.Combine(windowsDir ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : sys32, candidateName);
        string progFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relPath);
        string path = File.Exists(sys32Path) ? sys32Path : (File.Exists(progFilesPath) ? progFilesPath : sys32Path);

        folder.Items.Add(new FolderItem
        {
            DisplayName = name,
            ShortcutPath = path,
            TargetPath = path,
            Type = FolderItemType.Executable
        });
    }

    // ---- Real on-screen captures --------------------------------------------------------------------------
    // A full-screen window paints a neutral wallpaper drawn here (no personal or third-party image). The real
    // GlassFolders windows then open on top of it and take their glass from the screen like they do on a desktop,
    // and the result is read back from the screen. Nothing is drawn to imitate the glass.

    private static Window CreateWallpaper(RECT monitor, double scale)
    {
        double w = monitor.Width / scale, h = monitor.Height / scale;
        var canvas = new Canvas { Width = w, Height = h, Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x1E, 0x3C), Color.FromRgb(0x2A, 0x16, 0x4A), 35) };
        void Bloom(double cx, double cy, double rx, double ry, Color c)
        {
            var e = new Ellipse
            {
                Width = rx * 2, Height = ry * 2,
                Fill = new RadialGradientBrush(Color.FromArgb(0xB0, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry);
            canvas.Children.Add(e);
        }
        Bloom(w * 0.30, h * 0.35, w * 0.34, h * 0.55, Color.FromRgb(0x3B, 0x82, 0xF6));
        Bloom(w * 0.72, h * 0.30, w * 0.30, h * 0.50, Color.FromRgb(0xA8, 0x55, 0xF7));
        Bloom(w * 0.55, h * 0.85, w * 0.40, h * 0.40, Color.FromRgb(0x06, 0xB6, 0xD4));
        Bloom(w * 0.12, h * 0.90, w * 0.25, h * 0.35, Color.FromRgb(0xEC, 0x48, 0x99));

        return new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = monitor.Left / scale, Top = monitor.Top / scale, Width = w, Height = h,
            Content = canvas,
        };
    }

    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void SaveScreen(RECT area, string path)
    {
        // Never read past the monitor, which would add black bars.
        RECT screen = Win32.MonitorInfo(MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY)).rcMonitor;
        area = new RECT(Math.Max(area.Left, screen.Left), Math.Max(area.Top, screen.Top), Math.Min(area.Right, screen.Right), Math.Min(area.Bottom, screen.Bottom));
        bool ok = ScreenCapture.TryRead(area, (ReadOnlySpan<byte> px, int w, int h, int stride) =>
        {
            var copy = new byte[px.Length];
            px.CopyTo(copy);
            for (int i = 3; i < copy.Length; i += 4) copy[i] = 0xFF; // GDI leaves alpha undefined
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, copy, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            encoder.Save(fs);
            return true;
        }, out _);
        Console.WriteLine(ok ? $"Saved: {path} ({area.Width}x{area.Height})" : $"FAILED to capture {path}");
    }

    private static RECT WindowArea(Window win, int margin)
    {
        GetWindowRect(new System.Windows.Interop.WindowInteropHelper(win).Handle, out RECT r);
        return new RECT(r.Left - margin, r.Top - margin, r.Right + margin, r.Bottom + margin);
    }

    private static void CaptureSettingsWindow(ConfigurationManager cm, FolderManager fm, string outputPath, string page)
    {
        var win = new SettingsWindow(cm, fm);
        win.Show();
        string? handler = page switch
        {
            "Animations" => "OnNavAnimationClicked",
            "About" => "OnNavAboutClicked",
            _ => null
        };
        if (handler != null)
        {
            win.GetType().GetMethod(handler, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .Invoke(win, [win, new RoutedEventArgs()]);
        }
        Pump(900);
        SaveScreen(WindowArea(win, 24), outputPath);
        win.Close();
        Pump(150);
    }

    private static void CaptureFolderEditor(FolderDef folder, string outputPath)
    {
        var win = new FolderEditorDialog(folder);
        win.Show();
        Pump(900);
        SaveScreen(WindowArea(win, 24), outputPath);
        win.Close();
        Pump(150);
    }

    private static void CapturePopup(FolderDef folder, FolderManager fm, IconCacheService iconCache, IShortcutResolver resolver, RECT anchor, string popupPath, string? heroPath, RECT heroArea)
    {
        var popup = new FolderPopup(folder, fm, iconCache, resolver, anchor);
        popup.Show();
        Pump(1100); // the open animation
        if (heroPath is not null) SaveScreen(heroArea, heroPath);
        // The card is the visible part; the window around it is transparent and only holds the shadow.
        RECT crop = WindowArea(popup, 0);
        if (typeof(FolderPopup).GetField("_cardRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(popup) is GlassFolders.Core.Layout.RectInt card && card.Width > 0)
            crop = new RECT(card.Left - 48, card.Top - 48, card.Right + 72, card.Bottom + 96);
        SaveScreen(crop, popupPath);
        popup.Close();
        Pump(150);
    }

    private static void CaptureDesktop(Window wallpaper, FolderDef[] folders, double scale, RECT monitor, FolderManager fm, IconCacheService iconCache, IShortcutResolver resolver, string heroPath, string popupPath)
    {
        var canvas = (Canvas)wallpaper.Content;
        RECT anchor = default;
        int y = 300; // low enough that the popup clears whatever the user keeps at the top edge of their screen
        foreach (var f in folders)
        {
            var metrics = GlassFolders.Core.Layout.FolderTileMetrics.For(f.Size, 76, 97.6);
            var tile = new FolderTile(f.Id);
            tile.Apply(f, metrics, showCount: false);
            tile.Measure(new Size(metrics.WindowWidth, metrics.WindowHeight));
            tile.Arrange(new Rect(0, 0, metrics.WindowWidth, metrics.WindowHeight));
            Canvas.SetLeft(tile, 40);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
            if (anchor.Right == 0)
                anchor = new RECT(
                    monitor.Left + (int)Math.Round(40 * scale), monitor.Top + (int)Math.Round(y * scale),
                    monitor.Left + (int)Math.Round((40 + metrics.WindowWidth) * scale),
                    monitor.Top + (int)Math.Round((y + metrics.WindowHeight) * scale));
            y += (int)Math.Ceiling(metrics.WindowHeight) + 24;
        }
        Pump(400);
        // Everything above the taskbar, which belongs to the user's PC and isn't part of the app.
        RECT work = Win32.MonitorInfo(MonitorFromPoint(monitor.Center, MONITOR_DEFAULTTOPRIMARY)).rcWork;
        CapturePopup(folders[0], fm, iconCache, resolver, anchor, popupPath, heroPath, work);
    }
}
