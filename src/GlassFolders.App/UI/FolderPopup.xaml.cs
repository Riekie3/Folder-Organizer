using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using GlassFolders.App.Services;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using Microsoft.Win32;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.UI;

/// <summary>
/// The liquid glass folder popup window displaying a paged grid of apps, shortcuts, and files.
/// Items can be launched, added via drag &amp; drop, or removed from the folder without deleting source files.
/// Drawn on a <see cref="GlassCard"/>, using a snapshot of the screen taken just before it appears.
/// </summary>
public partial class FolderPopup : Window
{
    // A window that just closed can still be on screen for one composition frame.
    private const double RecentCloseMs = 250;

    // Captured beyond the card, so the blur pulls in real pixels at its edges and the card has room to grow.
    private const double CaptureOverscan = 64;

    private static long s_lastClosedTimestamp;

    private const int PageSize = 12; // 4 columns x 3 rows
    private readonly FolderDef _folder;
    private readonly FolderManager _folderManager;
    private readonly IconCacheService _iconCache;
    private readonly IShortcutResolver _resolver;
    private readonly RECT _anchor;
    private int _currentPage = 0;
    private bool _closing;
    private bool _isShowingDialog;

    public FolderPopup(FolderDef folder, FolderManager folderManager, IconCacheService iconCache, IShortcutResolver resolver, RECT anchor)
    {
        InitializeComponent();
        _folder = folder;
        Title = "GlassFolders – " + folder.Name;
        _folderManager = folderManager;
        _iconCache = iconCache;
        _resolver = resolver;
        _anchor = anchor;

        TxtTitle.Text = folder.Name;
        UpdateCount();
        ApplyGlyph();

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) =>
        {
            if (!_closing && !_isShowingDialog) Close();
        };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            s_lastClosedTimestamp = Stopwatch.GetTimestamp();
            PopupCard.SetBackdrop(null);
        };

        // Runs while the window is still hidden, so the snapshot can't include the popup itself.
        SourceInitialized += (_, _) => PrepareGlass();
        ContentRendered += (_, _) => { PlaceWindow(); Activate(); };
        SizeChanged += (_, _) => { if (IsLoaded) PlaceWindow(); };
        MouseDown += OnWindowMouseDown;

        RenderPage();
    }

    public string FolderId => _folder.Id;

    public void RefreshItems() => RenderPage();

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_folder.Items.Count / (double)PageSize));

    private void ApplyGlyph()
    {
        try
        {
            string glyphKey = "Glyph" + _folder.Glyph;
            if (TryFindResource(glyphKey) is Geometry geom)
                HeaderGlyph.Data = geom;
        }
        catch { }
    }

    private void PrepareGlass()
    {
        nint monitor = MonitorFromPoint(_anchor.Center, MONITOR_DEFAULTTONEAREST);
        double scale = ScreenCapture.MonitorScale(monitor);

        // Content is measured here because the window has no size of its own yet.
        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        RECT card = PlaceWindow(monitor, scale, (int)Math.Ceiling(content.DesiredSize.Width * scale), (int)Math.Ceiling(content.DesiredSize.Height * scale));

        BackdropSnapshot? snapshot = null;
        if (BackdropSnapshot.IsGlassAllowed())
        {
            if (Stopwatch.GetElapsedTime(s_lastClosedTimestamp).TotalMilliseconds < RecentCloseMs)
                DwmHelpers.WaitForNextFrame();

            RECT screen = Win32.MonitorInfo(monitor).rcMonitor;
            int overscan = (int)Math.Round(CaptureOverscan * scale);
            var area = new RECT(
                Math.Max(screen.Left, card.Left - overscan),
                Math.Max(screen.Top, card.Top - overscan),
                Math.Min(screen.Right, card.Right + overscan),
                Math.Min(screen.Bottom, card.Bottom + overscan));

            snapshot = BackdropSnapshot.Capture(area, scale);
            if (snapshot is not null)
                DiagnosticService.Instance.LogDebug($"Backdrop snapshot took {snapshot.ElapsedMilliseconds:F1} ms");
        }
        PopupCard.SetBackdrop(snapshot);
    }

    private void UpdateCount()
    {
        int count = _folder.Items.Count;
        TxtCount.Text = count == 1 ? "1 item" : $"{count} items";
    }

    private void RenderPage()
    {
        ItemsGrid.Children.Clear();
        UpdateCount();

        if (_folder.Items.Count == 0)
        {
            ItemsGrid.Visibility = Visibility.Collapsed;
            EmptyNotice.Visibility = Visibility.Visible;
            PaginationBar.Visibility = Visibility.Collapsed;
            return;
        }

        ItemsGrid.Visibility = Visibility.Visible;
        EmptyNotice.Visibility = Visibility.Collapsed;

        int totalPages = TotalPages;
        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

        PaginationBar.Visibility = totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnPrevPage.IsEnabled = _currentPage > 0;
        BtnNextPage.IsEnabled = _currentPage < totalPages - 1;

        // Render page dots
        PageDots.Children.Clear();
        if (totalPages > 1)
        {
            for (int p = 0; p < totalPages; p++)
            {
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Margin = new Thickness(3, 0, 3, 0),
                    Fill = p == _currentPage ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF))
                };
                PageDots.Children.Add(dot);
            }
        }

        var pageItems = _folder.Items.Skip(_currentPage * PageSize).Take(PageSize).ToList();
        foreach (var item in pageItems)
        {
            ItemsGrid.Children.Add(CreateItemView(item));
        }
    }

    private UIElement CreateItemView(FolderItem item)
    {
        var btn = new Button
        {
            Style = (Style)Resources["ItemTileStyle"],
            Width = 92,
            Height = 84,
            Margin = new Thickness(4),
            ToolTip = item.TargetPath
        };

        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var iconContainer = new Grid { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        var iconSource = _iconCache.GetIcon(item);

        if (iconSource is not null)
        {
            var img = new Image
            {
                Source = iconSource,
                Width = 44,
                Height = 44,
                Stretch = Stretch.Uniform
            };
            iconContainer.Children.Add(img);
        }
        else
        {
            var generic = new TextBlock
            {
                Text = item.Type == FolderItemType.Folder ? "📁" : (item.Type == FolderItemType.Url ? "🌐" : "📄"),
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconContainer.Children.Add(generic);
        }

        // Warning badge if target does not exist
        if (!_resolver.TargetExists(item))
        {
            var badge = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = "Target file or application was not found"
            };
            var exclamation = new TextBlock
            {
                Text = "!",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = exclamation;
            iconContainer.Children.Add(badge);
        }

        content.Children.Add(iconContainer);

        var lbl = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 11,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 84,
            TextAlignment = TextAlignment.Center
        };
        lbl.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black };
        content.Children.Add(lbl);

        btn.Content = content;

        // Left Click -> Launch
        btn.Click += (_, _) =>
        {
            ShellLauncher.Launch(item);
            Close();
        };

        // Right Click -> Context Menu
        btn.ContextMenu = BuildItemContextMenu(item);

        return btn;
    }

    private ContextMenu BuildItemContextMenu(FolderItem item)
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => { ShellLauncher.Launch(item); Close(); };
        menu.Items.Add(open);

        var loc = new MenuItem { Header = "Open file location" };
        loc.Click += (_, _) => { ShellLauncher.OpenLocation(item); Close(); };
        if (item.Type == FolderItemType.Url)
        {
            loc.IsEnabled = false;
        }
        menu.Items.Add(loc);

        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) =>
        {
            _isShowingDialog = true;
            try
            {
                string? newName = PromptRename(item.DisplayName, "Rename Item", this);
                if (!string.IsNullOrWhiteSpace(newName) && newName != item.DisplayName)
                {
                    item.DisplayName = newName.Trim();
                    _folderManager.SetFolderPosition(_folder.Id, _folder.X, _folder.Y); // triggers save
                    RenderPage();
                }
            }
            finally
            {
                _isShowingDialog = false;
                Activate();
            }
        };
        menu.Items.Add(rename);

        var remove = new MenuItem { Header = "Remove from folder" };
        remove.Click += (_, _) =>
        {
            _folderManager.RemoveItem(_folder.Id, item.Id);
            RenderPage();
        };
        menu.Items.Add(remove);

        return menu;
    }

    private void PlaceWindow()
    {
        nint monitor = MonitorFromPoint(_anchor.Center, MONITOR_DEFAULTTONEAREST);
        var size = Win32.Rect(new WindowInteropHelper(this).Handle);
        PlaceWindow(monitor, ScreenCapture.MonitorScale(monitor), size.Width, size.Height);
    }

    /// <summary>Keeps the card, not its shadow, inside the work area. The shadow may extend past the edge.</summary>
    private RECT PlaceWindow(nint monitor, double scale, int windowWidth, int windowHeight)
    {
        const int gap = 12;
        var work = Win32.MonitorInfo(monitor).rcWork;
        int margin = (int)Math.Round(GlassCard.ShadowMargin * scale);
        int cardWidth = windowWidth - margin * 2, cardHeight = windowHeight - margin * 2;

        int x = _anchor.Right + gap + cardWidth <= work.Right ? _anchor.Right + gap : _anchor.Left - gap - cardWidth;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - cardWidth));
        int y = Math.Clamp(_anchor.Top, work.Top, Math.Max(work.Top, work.Bottom - cardHeight));

        SetWindowPos(new WindowInteropHelper(this).Handle, 0, x - margin, y - margin, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return new RECT(x, y, x + cardWidth, y + cardHeight);
    }

    // The shadow belongs to this window, so a click on it counts as a click outside the popup.
    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.Source, this)) Close();
    }

    private void OnBtnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnBtnPrevPageClicked(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            RenderPage();
        }
    }

    private void OnBtnNextPageClicked(object sender, RoutedEventArgs e)
    {
        if (_currentPage < TotalPages - 1)
        {
            _currentPage++;
            RenderPage();
        }
    }

    private void OnBtnAddClicked(object sender, RoutedEventArgs e)
    {
        _isShowingDialog = true;
        try
        {
            var ofd = new OpenFileDialog
            {
                Title = $"Add shortcuts or files to {_folder.Name}",
                Filter = "All Files (*.*)|*.*|Shortcuts (*.lnk)|*.lnk|Applications (*.exe)|*.exe",
                Multiselect = true
            };

            if (ofd.ShowDialog(this) == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    var item = _resolver.Resolve(file);
                    _folderManager.AddItem(_folder.Id, item);
                }
                RenderPage();
            }
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Error in OnBtnAddClicked", ex);
        }
        finally
        {
            _isShowingDialog = false;
            Activate();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            try
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    foreach (string file in files)
                    {
                        var item = _resolver.Resolve(file);
                        _folderManager.AddItem(_folder.Id, item);
                    }
                    RenderPage();
                }
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Error in FolderPopup OnDrop", ex);
            }
            e.Handled = true;
        }
    }

    public static string? PromptRename(string currentName, string title = "Rename", Window? owner = null)
    {
        var win = new Window
        {
            Title = title,
            Width = 340,
            Height = 160,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x38))
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        var txt = new TextBox
        {
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 14,
            Padding = new Thickness(6, 4, 6, 4)
        };
        var btnOk = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 28,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        btnOk.Click += (_, _) => win.DialogResult = true;
        sp.Children.Add(txt);
        sp.Children.Add(btnOk);
        win.Content = sp;
        txt.SelectAll();
        txt.Focus();

        return win.ShowDialog() == true ? txt.Text : null;
    }
}
