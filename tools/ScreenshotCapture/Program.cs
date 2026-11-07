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
using Path = System.IO.Path;

namespace ScreenshotCapture;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Console.WriteLine("Initializing WPF application context...");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/Tokens.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/FolderGlyphs.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GlassFolders;component/Themes/Controls.xaml") });

            string outputDir = @"D:\Rie\Antigravity\Folder Organizer\docs\screenshots";
            Directory.CreateDirectory(outputDir);

            var configManager = new ConfigurationManager();
            var folderManager = new FolderManager(configManager);
            var iconCache = new IconCacheService();
            var resolver = new ShellShortcutResolver();

            // Setup sample folders with realistic shortcuts
            var workFolder = SetupWorkFolder(folderManager);
            var gamesFolder = SetupGamesFolder(folderManager);
            var mediaFolder = SetupMediaFolder(folderManager);

            // 1. Settings Window (General tab)
            Console.WriteLine("Capturing Settings Window (General)...");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings.png"), "General");

            // 2. Settings Window (Animations tab)
            Console.WriteLine("Capturing Settings Window (Animations)...");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings-animations.png"), "Animations");

            // 2b. Settings Window (About tab)
            Console.WriteLine("Capturing Settings Window (About)...");
            CaptureSettingsWindow(configManager, folderManager, Path.Combine(outputDir, "settings-about.png"), "About");

            // 3. Folder Customization Dialog
            Console.WriteLine("Capturing Folder Customization Dialog...");
            CaptureFolderEditor(workFolder, Path.Combine(outputDir, "folder-customization.png"));

            // 4. Folder Popup
            Console.WriteLine("Capturing Folder Popup...");
            CaptureFolderPopup(workFolder, folderManager, iconCache, resolver, Path.Combine(outputDir, "folder-popup.png"));

            // 5. Hero Desktop Showcase
            Console.WriteLine("Capturing Hero Desktop Showcase...");
            CaptureHeroDesktop(workFolder, gamesFolder, mediaFolder, folderManager, iconCache, resolver, Path.Combine(outputDir, "hero-desktop.png"));

            Console.WriteLine("All screenshots generated successfully in: " + outputDir);
            app.Shutdown();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL ERROR: " + ex);
        }
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
            AddSystemApp(folder, "Visual Studio Code", @"Microsoft VS Code\Code.exe", "code.exe");
            AddSystemApp(folder, "Windows Terminal", @"wt.exe", "wt.exe");
            AddSystemApp(folder, "Google Chrome", @"Google\Chrome\Application\chrome.exe", "chrome.exe");
            AddSystemApp(folder, "File Explorer", @"explorer.exe", "explorer.exe");
            AddSystemApp(folder, "Notepad", @"notepad.exe", "notepad.exe");
            AddSystemApp(folder, "Command Prompt", @"cmd.exe", "cmd.exe");
            AddSystemApp(folder, "PowerShell", @"powershell.exe", "powershell.exe");
            AddSystemApp(folder, "Calculator", @"calc.exe", "calc.exe");
            AddSystemApp(folder, "Task Manager", @"taskmgr.exe", "taskmgr.exe");
            AddSystemApp(folder, "Snipping Tool", @"SnippingTool.exe", "SnippingTool.exe");
            AddSystemApp(folder, "Settings", @"SystemSettings.exe", "SystemSettings.exe");
            AddSystemApp(folder, "Remote Desktop", @"mstsc.exe", "mstsc.exe");
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

    private static void AddSystemApp(FolderDef folder, string name, string relPath, string candidateName)
    {
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sys32Path = Path.Combine(sys32, candidateName);
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

    private static void CaptureSettingsWindow(ConfigurationManager cm, FolderManager fm, string outputPath, string page)
    {
        var win = new SettingsWindow(cm, fm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false
        };

        win.Show();
        if (page == "Animations")
        {
            win.GetType().GetMethod("OnNavAnimationClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .Invoke(win, [win, new RoutedEventArgs()]);
        }
        win.UpdateLayout();

        RenderElementToPng(win, (int)win.ActualWidth, (int)win.ActualHeight, outputPath);
        win.Close();
    }

    private static void CaptureFolderEditor(FolderDef folder, string outputPath)
    {
        var win = new FolderEditorDialog(folder)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false
        };

        win.Show();
        win.UpdateLayout();

        RenderElementToPng(win, (int)win.ActualWidth, (int)win.ActualHeight, outputPath);
        win.Close();
    }

    private static void CaptureFolderPopup(FolderDef folder, FolderManager fm, IconCacheService iconCache, IShortcutResolver resolver, string outputPath)
    {
        var anchor = new RECT { Left = 200, Top = 200, Right = 304, Bottom = 312 };
        var win = new FolderPopup(folder, fm, iconCache, resolver, anchor)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false
        };

        win.Show();
        win.UpdateLayout();

        RenderElementToPng(win, (int)win.ActualWidth, (int)win.ActualHeight, outputPath);
        win.Close();
    }

    private static void CaptureHeroDesktop(
        FolderDef workFolder, FolderDef gamesFolder, FolderDef mediaFolder,
        FolderManager fm, IconCacheService iconCache, IShortcutResolver resolver, string outputPath)
    {
        int canvasW = 1600;
        int canvasH = 900;

        var canvas = new Canvas
        {
            Width = canvasW,
            Height = canvasH,
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0C, 0x14, 0x24),
                Color.FromRgb(0x03, 0x07, 0x12),
                45)
        };

        // Ambient glowing orb in top center
        var glow1 = new Ellipse
        {
            Width = 900,
            Height = 700,
            Fill = new RadialGradientBrush(
                Color.FromArgb(0x40, 0x25, 0x63, 0xEB),
                Color.FromArgb(0x00, 0x0C, 0x14, 0x24))
        };
        Canvas.SetLeft(glow1, 350);
        Canvas.SetTop(glow1, 80);
        canvas.Children.Add(glow1);

        var glow2 = new Ellipse
        {
            Width = 600,
            Height = 500,
            Fill = new RadialGradientBrush(
                Color.FromArgb(0x28, 0x06, 0xB6, 0xD4),
                Color.FromArgb(0x00, 0x0C, 0x14, 0x24))
        };
        Canvas.SetLeft(glow2, 100);
        Canvas.SetTop(glow2, 200);
        canvas.Children.Add(glow2);

        // Render desktop folder tiles
        var tiles = new List<(FolderDef Folder, int X, int Y)>
        {
            (workFolder, 140, 160),
            (gamesFolder, 140, 310),
            (mediaFolder, 140, 460)
        };

        foreach (var (f, x, y) in tiles)
        {
            Color colorA = (Color)ColorConverter.ConvertFromString(f.ColorA);
            Color colorB = (Color)ColorConverter.ConvertFromString(f.ColorB);
            var tile = new FolderTile(f.Id, f.Name, colorA, colorB);
            tile.SetLabel(f.Name);
            tile.SetGlyph(f.Glyph);
            tile.SetItemCount(f.Items.Count);

            tile.Measure(new Size(104, 112));
            tile.Arrange(new Rect(0, 0, 104, 112));
            tile.UpdateLayout();

            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
        }

        // Render open Liquid Glass popup for Work folder
        var anchor = new RECT { Left = 140, Top = 160, Right = 244, Bottom = 272 };
        var popup = new FolderPopup(workFolder, fm, iconCache, resolver, anchor);
        popup.Measure(new Size(420, 380));
        popup.Arrange(new Rect(0, 0, 420, 380));
        popup.UpdateLayout();

        var popupRtb = new RenderTargetBitmap(420, 380, 96, 96, PixelFormats.Pbgra32);
        popupRtb.Render(popup);

        var popupImage = new Image
        {
            Source = popupRtb,
            Width = 420,
            Height = 380,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 12,
                Opacity = 0.55,
                Color = Colors.Black
            }
        };

        Canvas.SetLeft(popupImage, 280);
        Canvas.SetTop(popupImage, 140);
        canvas.Children.Add(popupImage);

        // Windows 11 centered taskbar mockup at bottom
        var taskbar = new Border
        {
            Width = canvasW,
            Height = 48,
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0F, 0x17, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var tbGrid = new Grid();
        var centerIcons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        for (int i = 0; i < 7; i++)
        {
            var iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))
            };
            centerIcons.Children.Add(iconBox);
        }
        tbGrid.Children.Add(centerIcons);

        // System tray at bottom right
        var trayPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        try
        {
            var trayIconImg = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/GlassFolders;component/assets/app-icon.png")),
                Width = 18,
                Height = 18,
                Margin = new Thickness(6, 0, 8, 0)
            };
            trayPanel.Children.Add(trayIconImg);
        }
        catch { }

        var timeText = new TextBlock
        {
            Text = DateTime.Now.ToString("h:mm tt\nM/d/yyyy"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        trayPanel.Children.Add(timeText);
        tbGrid.Children.Add(trayPanel);

        taskbar.Child = tbGrid;
        Canvas.SetLeft(taskbar, 0);
        Canvas.SetTop(taskbar, canvasH - 48);
        canvas.Children.Add(taskbar);

        canvas.Measure(new Size(canvasW, canvasH));
        canvas.Arrange(new Rect(0, 0, canvasW, canvasH));
        canvas.UpdateLayout();

        RenderElementToPng(canvas, canvasW, canvasH, outputPath);
    }

    private static void RenderElementToPng(FrameworkElement element, int width, int height, string outputPath)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(outputPath);
        encoder.Save(fs);
        Console.WriteLine($"Saved: {outputPath} ({width}x{height})");
    }
}
