using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GlassFolders.App.Services;
using GlassFolders.App.Themes;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;
using GlassFolders.Core.Popup;
using GlassFolders.Native;
using GlassFolders.Native.Shell;
using Microsoft.Win32;
using static GlassFolders.Native.NativeMethods;

namespace GlassFolders.App.UI;

/// <summary>
/// The folder popup: a glass card holding a paged grid of apps, shortcuts and files.
/// Items can be launched, added by drag and drop, or removed from the folder without deleting the source.
/// Drawn on a <see cref="GlassCard"/>, using a snapshot of the screen taken just before it appears.
/// </summary>
public partial class FolderPopup : Window
{
    // A window that just closed can still be on screen for one composition frame.
    private const double RecentCloseMs = 250;

    // Captured beyond the card so the blur pulls in real pixels at its edges. Capture time follows the area.
    private const double CaptureOverscan = 24;

    private const double LabelWidth = 128;
    private const double LabelLineHeight = 23.5;

    private const int WheelNotch = 120;

    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(200);

    private static readonly Effect LabelShadow = CreateLabelShadow();
    private static readonly Brush TileBackground = CreateTileBackground();
    private static readonly Brush TileBorder = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)));
    private static readonly Brush DotActive = Brushes.White;
    private static readonly Brush DotInactive = Frozen(new SolidColorBrush(Color.FromArgb(0x4D, 255, 255, 255)));

    private static long s_lastClosedTimestamp;

    private readonly FolderDef _folder;
    private readonly FolderManager _folderManager;
    private readonly IconCacheService _iconCache;
    private readonly IShortcutResolver _resolver;
    private readonly RECT _anchor;
    private readonly PopupGrid _grid;
    private readonly Dictionary<string, Grid> _iconHosts = [];
    private readonly GlassAppearance _appearance = AppearanceService.Instance.Glass;
    private readonly bool _showMissingBadge;
    private double IconSize => _appearance.IconSize;
    private double LowResolutionIconSize => _appearance.LowResolutionIconSize;

    private delegate void ResizeFrame(bool completed);

    private ResizeFrame? _resizeFrame;
    private RectInt _windowRect;
    private RectInt _cardRect;
    private double _scale = 1;
    private bool _expanded;
    private bool _opened;
    private int _currentPage;
    private int _wheelDelta;
    private bool _closing;
    private bool _isShowingDialog;

    public FolderPopup(FolderDef folder, FolderManager folderManager, IconCacheService iconCache, IShortcutResolver resolver, RECT anchor, PopupGrid? grid = null, bool showMissingBadge = true)
    {
        InitializeComponent();
        _showMissingBadge = showMissingBadge;
        // The popup keeps its blue glass in light and dark mode; the sliders in Settings still shape it.
        PopupCard.ApplyAppearance(_appearance, light: false);
        _folder = folder;
        Title = "GlassFolders – " + folder.Name;
        _folderManager = folderManager;
        _iconCache = iconCache;
        _resolver = resolver;
        _anchor = anchor;
        _grid = grid ?? PopupGrid.Default;
        _scale = ScreenCapture.MonitorScale(Monitor());

        HeaderRow.Height = new GridLength(PopupMetrics.HeaderHeight);
        FooterRow.Height = new GridLength(PopupMetrics.FooterHeight);
        GridHost.Margin = new Thickness(PopupMetrics.GridLeft, PopupMetrics.GridTopGap, PopupMetrics.GridRight, 0);

        TxtTitle.Text = folder.Name;
        HeaderIcon.Show(folder);
        UpdateExpandButton();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        Deactivated += (_, _) =>
        {
            if (!_isShowingDialog) Dismiss();
        };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _resizeFrame?.Invoke(completed: true);
            s_lastClosedTimestamp = Stopwatch.GetTimestamp();
            PopupCard.SetBackdrop(null);
        };

        // Runs while the window is still hidden, so the snapshot can't include the popup itself.
        SourceInitialized += (_, _) => PrepareWindow();
        ContentRendered += (_, _) => RevealCard();
        DpiChanged += (_, e) =>
        {
            // Every Image raises this too, and it bubbles up to here.
            if (ReferenceEquals(e.Source, this)) ApplyLayout(animate: false);
        };
        MouseDown += OnWindowMouseDown;

        Header.MouseEnter += (_, _) => Animate.Fade(BtnMore, 1, TimeSpan.FromMilliseconds(120), AnimationCategory.TileHover);
        Header.MouseLeave += (_, _) => Animate.Fade(BtnMore, 0, TimeSpan.FromMilliseconds(120), AnimationCategory.TileHover);

        PopupCard.ContextMenu = BuildCardMenu();

        RenderPage();
    }

    public string FolderId => _folder.Id;

    public void RefreshItems() => RenderItemsAndLayout();

    private PopupGrid ActiveGrid => _expanded ? _grid.Expanded : _grid;

    private bool CanExpand => _folder.Items.Count > _grid.PageSize;

    private int TotalPages => PopupPaging.TotalPages(_folder.Items.Count, ActiveGrid.PageSize);

    /// <summary>Closes with the animation. Used for everything the user does in the popup. <see cref="Window.Close"/> stays immediate.</summary>
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        PopupCard.IsHitTestVisible = false;
        Animate.PopupClose(PopupCard, AnimationOrigin(), Close);
    }

    private void RevealCard()
    {
        if (_opened || _closing) return;
        _opened = true;
        Activate();
        Animate.PopupOpen(PopupCard, AnimationOrigin());
    }

    /// <summary>The folder's centre, relative to the card. Outside 0–1 when the folder is outside the card.</summary>
    private Point AnimationOrigin() => new(
        (_anchor.Left + _anchor.Width / 2.0 - _cardRect.Left) / Math.Max(1, _cardRect.Width),
        (_anchor.Top + _anchor.Height / 2.0 - _cardRect.Top) / Math.Max(1, _cardRect.Height));

    private void PrepareWindow()
    {
        ApplyLayout(animate: false);

        BackdropSnapshot? snapshot = null;
        if (BackdropSnapshot.IsGlassAllowed())
        {
            if (Stopwatch.GetElapsedTime(s_lastClosedTimestamp).TotalMilliseconds < RecentCloseMs)
                DwmHelpers.WaitForNextFrame();

            snapshot = BackdropSnapshot.Capture(CaptureArea(), _scale, _appearance.BlurRadius);
            if (snapshot is not null)
                DiagnosticService.Instance.LogDebug($"Backdrop snapshot took {snapshot.ElapsedMilliseconds:F1} ms");
        }
        PopupCard.SetBackdrop(snapshot);
    }

    // The popup can't capture itself once open, so this already covers the expanded card.
    private RECT CaptureArea()
    {
        RectInt card = PlaceCard(LargestRowsThisFolderCanShow(), out _);
        RectInt area = Union(card, _cardRect);
        int overscan = (int)Math.Round(CaptureOverscan * _scale);
        RECT screen = Win32.MonitorInfo(Monitor()).rcMonitor;
        return new RECT(
            Math.Max(screen.Left, area.Left - overscan),
            Math.Max(screen.Top, area.Top - overscan),
            Math.Min(screen.Right, area.Right + overscan),
            Math.Min(screen.Bottom, area.Bottom + overscan));
    }

    private int LargestRowsThisFolderCanShow() =>
        PopupPaging.VisibleRows(_folder.Items.Count, CanExpand ? _grid.Expanded : _grid);

    private nint Monitor() => MonitorFromPoint(_anchor.Center, MONITOR_DEFAULTTONEAREST);

    private RectInt PlaceCard(int rows, out double fit)
    {
        var work = ToRectInt(Win32.MonitorInfo(Monitor()).rcWork);
        double width = PopupMetrics.CardWidth(ActiveGrid.Columns), height = PopupMetrics.CardHeight(rows);
        fit = PopupMetrics.FitScale(width, height, work, _scale);

        return PopupPlacement.Place(
            ToRectInt(_anchor),
            work,
            (int)Math.Round(width * fit * _scale),
            (int)Math.Round(height * fit * _scale),
            _scale);
    }

    /// <summary>Sizes and positions the card for the current page size, growing the window around it if needed.</summary>
    private void ApplyLayout(bool animate)
    {
        _scale = ScreenCapture.MonitorScale(Monitor());
        var grid = ActiveGrid;
        int rows = PopupPaging.VisibleRows(_folder.Items.Count, grid);

        RectInt card = PlaceCard(rows, out double fit);
        RectInt largest = PlaceCard(LargestRowsThisFolderCanShow(), out _);
        _cardRect = card;

        ItemsGrid.Columns = grid.Columns;
        ItemsGrid.Rows = rows;
        ItemsGrid.Width = grid.Columns * PopupMetrics.CellWidth;
        ItemsGrid.Height = rows * PopupMetrics.CellHeight;

        CardRoot.LayoutTransform = fit < 1 ? new ScaleTransform(fit, fit) : Transform.Identity;

        PlaceWindow(Union(Union(card, largest), ToRectInt(_anchor)));

        double left = (card.Left - _windowRect.Left) / _scale, top = (card.Top - _windowRect.Top) / _scale;
        PopupCard.Width = card.Width / _scale;
        Canvas.SetLeft(PopupCard, left);
        MoveCardTo(card.Height / _scale, top, animate);
    }

    /// <summary>Changes the card's height and top. Driven per frame because it changes layout, and stops when done.</summary>
    private void MoveCardTo(double height, double top, bool animate)
    {
        _resizeFrame?.Invoke(completed: true);

        double fromHeight = PopupCard.Height, fromTop = Canvas.GetTop(PopupCard);
        if (!animate || double.IsNaN(fromHeight) || double.IsNaN(fromTop) || !AnimationService.Instance.ShouldAnimate(AnimationCategory.WindowTransition))
        {
            PopupCard.Height = height;
            Canvas.SetTop(PopupCard, top);
            return;
        }

        double duration = AnimationService.Instance.GetDuration(ResizeDuration, AnimationCategory.WindowTransition).TotalMilliseconds;
        long start = 0;

        EventHandler? onFrame = null;
        ResizeFrame frame = completed =>
        {
            // Timed from the first frame, in case building the new page held up the UI thread.
            if (start == 0) start = Stopwatch.GetTimestamp();
            double progress = completed ? 1 : Math.Min(1, Stopwatch.GetElapsedTime(start).TotalMilliseconds / duration);
            double eased = 1 - Math.Pow(1 - progress, 3);
            PopupCard.Height = fromHeight + (height - fromHeight) * eased;
            Canvas.SetTop(PopupCard, fromTop + (top - fromTop) * eased);
            if (progress >= 1)
            {
                CompositionTarget.Rendering -= onFrame;
                _resizeFrame = null;
            }
        };
        onFrame = (_, _) => frame(completed: false);
        _resizeFrame = frame;
        CompositionTarget.Rendering += onFrame;
    }

    /// <summary>The window reaches the folder so the card can grow from it, and leaves room for the shadow.</summary>
    private void PlaceWindow(RectInt content)
    {
        int margin = (int)Math.Round(GlassCard.ShadowMargin * _scale);
        var screen = ToRectInt(Win32.MonitorInfo(Monitor()).rcMonitor);

        int left = Math.Max(screen.Left, content.Left - margin);
        int top = Math.Max(screen.Top, content.Top - margin);
        int right = Math.Min(screen.Right, content.Right + margin);
        int bottom = Math.Min(screen.Bottom, content.Bottom + margin);

        // Windows treats a topmost window that fills a monitor as a full-screen app, and hides the taskbar.
        if (left == screen.Left && top == screen.Top && right == screen.Right && bottom == screen.Bottom) top++;

        var rect = new RectInt(left, top, right - left, bottom - top);
        if (rect == _windowRect) return;
        _windowRect = rect;

        SetWindowPos(new WindowInteropHelper(this).EnsureHandle(), 0, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void RenderItemsAndLayout()
    {
        RenderPage();
        if (IsLoaded) ApplyLayout(animate: true);
    }

    private void RenderPage(int direction = 0)
    {
        ItemsGrid.Children.Clear();
        PageDots.Children.Clear();
        _iconHosts.Clear();
        BtnExpand.IsEnabled = CanExpand;
        UpdateExpandButton();

        int count = _folder.Items.Count;
        if (count == 0)
        {
            ItemsGrid.Visibility = Visibility.Collapsed;
            EmptyNotice.Visibility = Visibility.Visible;
            return;
        }

        ItemsGrid.Visibility = Visibility.Visible;
        EmptyNotice.Visibility = Visibility.Collapsed;

        int pageSize = ActiveGrid.PageSize;
        _currentPage = PopupPaging.ClampPage(_currentPage, count, pageSize);

        var pageItems = _folder.Items.Skip(_currentPage * pageSize).Take(pageSize).ToList();
        var missing = new List<FolderItem>();
        foreach (var item in pageItems)
            ItemsGrid.Children.Add(CreateItemView(item, missing));

        LoadMissingIcons(missing);
        RenderPageDots();
        Animate.PageIn(ItemsGrid, direction);
    }

    private void RenderPageDots()
    {
        int totalPages = TotalPages;
        if (totalPages <= 1) return;

        for (int p = 0; p < totalPages; p++)
        {
            int page = p;
            var dot = new Border
            {
                // Transparent padding makes the small dots easier to click.
                Background = Brushes.Transparent,
                Padding = new Thickness(5.5, 6, 5.5, 6),
                Cursor = Cursors.Hand,
                Child = new Ellipse { Width = 10, Height = 10, Fill = p == _currentPage ? DotActive : DotInactive },
            };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                GoToPage(page);
                e.Handled = true;
            };
            PageDots.Children.Add(dot);
        }
    }

    private void GoToPage(int page)
    {
        int target = PopupPaging.ClampPage(page, _folder.Items.Count, ActiveGrid.PageSize);
        if (target == _currentPage) return;
        int direction = Math.Sign(target - _currentPage);
        _currentPage = target;
        RenderPage(direction);
    }

    private void ToggleExpanded()
    {
        if (!CanExpand && !_expanded) return;

        int firstVisible = _currentPage * ActiveGrid.PageSize;
        _expanded = !_expanded;
        _currentPage = PopupPaging.PageOfItem(firstVisible, ActiveGrid.PageSize);
        RenderItemsAndLayout();
    }

    private void UpdateExpandButton()
    {
        ExpandGlyph.Data = (Geometry)FindResource(_expanded ? "IconCollapse" : "IconExpand");
        BtnExpand.ToolTip = _expanded ? "Show fewer" : CanExpand ? "Show more" : "Everything already fits";
    }

    private int IconPixelSize => (int)Math.Round(IconSize * _scale);

    private UIElement CreateItemView(FolderItem item, List<FolderItem> missing)
    {
        var button = new Button { Style = (Style)Resources["ItemTileStyle"], ToolTip = item.TargetPath };

        var iconHost = new Grid { Width = IconSize, Height = IconSize, HorizontalAlignment = HorizontalAlignment.Center };
        _iconHosts[item.Id] = iconHost;

        var icon = _iconCache.TryGetCached(item, IconPixelSize);
        if (icon is not null) ShowIcon(iconHost, icon, fadeIn: false);
        else missing.Add(item);

        if (_showMissingBadge && !_resolver.TargetExists(item)) iconHost.Children.Add(CreateMissingBadge());

        var label = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 20,
            FontFamily = (FontFamily)FindResource("FontBody"),
            Foreground = Brushes.White,
            Width = LabelWidth,
            MaxHeight = LabelLineHeight * 2,
            Margin = new Thickness(0, 9, 0, 0),
            LineHeight = LabelLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Effect = LabelShadow,
        };

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(iconHost);
        content.Children.Add(label);
        button.Content = content;

        button.Click += (_, _) => LaunchAndDismiss(item);

        // Press and drag an item to move it to another place in the folder.
        button.PreviewMouseLeftButtonDown += (_, e) => _dragOrigin = e.GetPosition(this);
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragOrigin is not { } origin) return;
            Vector moved = e.GetPosition(this) - origin;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragOrigin = null;
            iconHost.Opacity = 0.4;
            try
            {
                DragDrop.DoDragDrop(button, new DataObject(DragItemFormat, item.Id), DragDropEffects.Move);
            }
            finally
            {
                iconHost.Opacity = 1;
                HideDropMarker();
                button.ReleaseMouseCapture();
            }
        };
        button.PreviewMouseLeftButtonUp += (_, _) => _dragOrigin = null;

        button.PreviewMouseLeftButtonDown += (_, _) => Animate.Scale(iconHost, 0.94, TimeSpan.FromMilliseconds(90), AnimationCategory.ItemPress);
        button.PreviewMouseLeftButtonUp += (_, _) => Animate.Scale(iconHost, 1, TimeSpan.FromMilliseconds(90), AnimationCategory.ItemPress);
        button.MouseLeave += (_, _) => Animate.Scale(iconHost, 1, TimeSpan.FromMilliseconds(90), AnimationCategory.ItemPress);

        // Filled when first opened. Building menus for a whole page would slow every page change.
        var menu = new ContextMenu();
        button.ContextMenu = menu;
        button.ContextMenuOpening += (_, _) =>
        {
            if (menu.Items.Count == 0) FillItemMenu(menu, item);
        };

        return button;
    }

    private void LaunchAndDismiss(FolderItem item)
    {
        ShellLauncher.Launch(item);
        Dismiss();
    }

    private void LoadMissingIcons(List<FolderItem> items)
    {
        if (items.Count == 0) return;
        _iconCache.LoadMissing(items, IconPixelSize, (item, icon) =>
            Dispatcher.BeginInvoke(() =>
            {
                // The user may have changed page or closed the popup by now. The icon stays cached either way.
                if (_iconHosts.TryGetValue(item.Id, out var host))
                {
                    if (icon is not null) ShowIcon(host, icon, fadeIn: true);
                    else ShowGenericIcon(host, item);
                }
            }));
    }

    private void ShowIcon(Grid host, AppIcon icon, bool fadeIn)
    {
        var image = new Image
        {
            Source = icon.Image,
            Width = icon.IsLowResolution ? LowResolutionIconSize : IconSize,
            Height = icon.IsLowResolution ? LowResolutionIconSize : IconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        if (icon.IsLowResolution) host.Children.Insert(0, CreateTile());
        host.Children.Insert(icon.IsLowResolution ? 1 : 0, image);

        if (fadeIn)
        {
            image.Opacity = 0;
            Animate.Fade(image, 1, TimeSpan.FromMilliseconds(140), AnimationCategory.PageTransition);
        }
    }

    private static void ShowGenericIcon(Grid host, FolderItem item)
    {
        host.Children.Insert(0, CreateTile());
        host.Children.Insert(1, new TextBlock
        {
            Text = item.Type == FolderItemType.Folder ? "📁" : item.Type == FolderItemType.Url ? "🌐" : "📄",
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    // From the reference's icon grid. It makes small app icons look intended instead of blurry.
    private static Border CreateTile() => new()
    {
        CornerRadius = new CornerRadius(22),
        Background = TileBackground,
        BorderBrush = TileBorder,
        BorderThickness = new Thickness(1),
    };

    private static Border CreateMissingBadge() => new()
    {
        Width = 26,
        Height = 26,
        CornerRadius = new CornerRadius(13),
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        ToolTip = "Target file or application was not found",
        Child = new TextBlock
        {
            Text = "!",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private ContextMenu BuildCardMenu()
    {
        var menu = new ContextMenu();

        var add = new MenuItem { Header = "Add items…" };
        add.Click += OnBtnAddClicked;
        menu.Items.Add(add);

        var expand = new MenuItem();
        expand.Click += (_, _) => ToggleExpanded();
        menu.Items.Add(expand);

        menu.Opened += (_, _) =>
        {
            expand.Header = _expanded ? "Show fewer" : "Show more";
            expand.IsEnabled = CanExpand || _expanded;
        };
        return menu;
    }

    private void FillItemMenu(ContextMenu menu, FolderItem item)
    {
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => LaunchAndDismiss(item);
        menu.Items.Add(open);

        var location = new MenuItem { Header = "Open file location", IsEnabled = item.Type != FolderItemType.Url };
        location.Click += (_, _) => { ShellLauncher.OpenLocation(item); Dismiss(); };
        menu.Items.Add(location);

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
            if (!CanExpand) _expanded = false;
            RenderItemsAndLayout();
        };
        menu.Items.Add(remove);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                break;
            case Key.Left or Key.PageUp:
                GoToPage(_currentPage - 1);
                break;
            case Key.Right or Key.PageDown:
                GoToPage(_currentPage + 1);
                break;
            case Key.Home:
                GoToPage(0);
                break;
            case Key.End:
                GoToPage(TotalPages - 1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Touchpads send many small steps, so they add up to one notch before the page changes.
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _wheelDelta += e.Delta;
        if (Math.Abs(_wheelDelta) < WheelNotch) return;
        GoToPage(_currentPage - Math.Sign(_wheelDelta));
        _wheelDelta = 0;
        e.Handled = true;
    }

    // The shadow belongs to this window, so a click on it counts as a click outside the popup.
    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.Source, this)) Dismiss();
    }

    private void OnBtnCloseClicked(object sender, RoutedEventArgs e) => Dismiss();

    private void OnBtnExpandClicked(object sender, RoutedEventArgs e) => ToggleExpanded();

    private void OnBtnMoreClicked(object sender, RoutedEventArgs e)
    {
        var menu = PopupCard.ContextMenu;
        menu.PlacementTarget = BtnMore;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        // Right-clicking the card later should open the menu at the pointer again.
        menu.Closed += ResetMenuPlacement;
    }

    private void ResetMenuPlacement(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Closed -= ResetMenuPlacement;
        menu.ClearValue(ContextMenu.PlacementProperty);
        menu.ClearValue(ContextMenu.PlacementTargetProperty);
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
                    _folderManager.AddItem(_folder.Id, _resolver.Resolve(file));
                RenderItemsAndLayout();
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

    private const string DragItemFormat = "GlassFolders.FolderItemId";
    private Point? _dragOrigin;
    private Border? _dropMarker;

    /// <summary>The item index under the pointer while an item from this folder is being dragged.</summary>
    private int DropIndexAt(DragEventArgs e)
    {
        Point p = e.GetPosition(ItemsGrid);
        return PopupPaging.DropIndex(p.X, p.Y, ItemsGrid.ActualWidth, ItemsGrid.ActualHeight,
            ItemsGrid.Columns, ItemsGrid.Rows, _currentPage, ActiveGrid.PageSize, _folder.Items.Count);
    }

    private void ShowDropMarker(int index)
    {
        int pageSize = ActiveGrid.PageSize;
        int local = index - _currentPage * pageSize;
        if (local < 0 || local >= pageSize) { HideDropMarker(); return; }

        double cellW = ItemsGrid.ActualWidth / ItemsGrid.Columns, cellH = ItemsGrid.ActualHeight / ItemsGrid.Rows;
        if (_dropMarker is null)
        {
            _dropMarker = new Border
            {
                CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            GridHost.Children.Add(_dropMarker);
        }
        _dropMarker.Width = cellW - 20;
        _dropMarker.Height = cellH - 4;
        _dropMarker.Margin = new Thickness((local % ItemsGrid.Columns) * cellW + 10, (local / ItemsGrid.Columns) * cellH, 0, 0);
        _dropMarker.Visibility = Visibility.Visible;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => HideDropMarker();

    private void HideDropMarker()
    {
        if (_dropMarker is not null) _dropMarker.Visibility = Visibility.Collapsed;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragItemFormat))
        {
            e.Effects = DragDropEffects.Move;
            ShowDropMarker(DropIndexAt(e));
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragItemFormat))
        {
            HideDropMarker();
            try
            {
                if (e.Data.GetData(DragItemFormat) is string id)
                {
                    int from = _folder.Items.FindIndex(i => i.Id == id);
                    int to = DropIndexAt(e);
                    if (from >= 0 && from != to && _folderManager.ReorderItem(_folder.Id, from, to))
                        RenderItemsAndLayout();
                }
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Error reordering in FolderPopup", ex);
            }
            e.Handled = true;
            return;
        }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                foreach (string file in files)
                    _folderManager.AddItem(_folder.Id, _resolver.Resolve(file));
                RenderItemsAndLayout();
            }
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Error in FolderPopup OnDrop", ex);
        }
        e.Handled = true;
    }

    /// <summary>Asks for a new name on a glass prompt. Returns null if cancelled.</summary>
    public static string? PromptRename(string currentName, string title = "Rename", Window? owner = null) =>
        GlassPromptDialog.Show(owner, title, currentName);

    private static RectInt ToRectInt(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    private static RectInt Union(RectInt a, RectInt b)
    {
        int left = Math.Min(a.Left, b.Left), top = Math.Min(a.Top, b.Top);
        return new RectInt(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private static Effect CreateLabelShadow()
    {
        var shadow = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Direction = 270, Opacity = 0.5, Color = Color.FromRgb(0x06, 0x12, 0x3A) };
        shadow.Freeze();
        return shadow;
    }

    private static Brush CreateTileBackground() => Frozen(new LinearGradientBrush(
        Color.FromArgb(0x47, 255, 255, 255), Color.FromArgb(0x1F, 255, 255, 255), 90));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
