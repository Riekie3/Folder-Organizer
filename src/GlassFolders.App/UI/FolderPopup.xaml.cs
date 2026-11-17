using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GlassFolders.App.Services;
using GlassFolders.App.Themes;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Icons;
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

    // While an item is dragged, resting it in a turning zone flips the page after this hold. A window
    // timer is used because DragDrop.DoDragDrop pumps a native loop that never ticks a DispatcherTimer.
    private const uint PageFlipTimerId = 0x4746; // "GF"
    private const uint PageFlipDelayMs = 550;

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

    private HwndSource? _hwndSource;
    private nint _hwnd;
    private bool _pageFlipTimerRunning;
    private PopupDragPaging.Turn? _pageFlipPending;
    private int? _pageFlipZone;
    private bool _dragOverPopup;

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
        Closing += (_, _) =>
        {
            _closing = true;
            EndDragPaging();
            CancelDragVisuals();
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        };
        Closed += (_, _) =>
        {
            _resizeFrame?.Invoke(completed: true);
            s_lastClosedTimestamp = Stopwatch.GetTimestamp();
            PopupCard.SetBackdrop(null);
        };

        // Runs while the window is still hidden, so the snapshot can't include the popup itself.
        SourceInitialized += (_, _) => OnSourceInitialized();
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
        // The window is in its final place now; before this the glass could have been measured at the screen's corner.
        PopupCard.RealignBackdrop();
        Activate();
        Animate.PopupOpen(PopupCard, AnimationOrigin());
    }

    /// <summary>The folder's centre, relative to the card. Outside 0–1 when the folder is outside the card.</summary>
    private Point AnimationOrigin() => new(
        (_anchor.Left + _anchor.Width / 2.0 - _cardRect.Left) / Math.Max(1, _cardRect.Width),
        (_anchor.Top + _anchor.Height / 2.0 - _cardRect.Top) / Math.Max(1, _cardRect.Height));

    private void OnSourceInitialized()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        PrepareWindow();
    }

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
        // Fresh tiles start in their cells; a drag that is still running reopens its gap on the next move.
        _reflowOffsets.Clear();
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

    private void GoToPage(int page, bool animate = true)
    {
        int target = PopupPaging.ClampPage(page, _folder.Items.Count, ActiveGrid.PageSize);
        if (target == _currentPage) return;
        int direction = Math.Sign(target - _currentPage);
        _currentPage = target;
        RenderPage(animate ? direction : 0);
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
        var button = new Button { Style = (Style)Resources["ItemTileStyle"], ToolTip = item.TargetPath, Tag = item.Id };
        // A page can be rebuilt under a drag (page turn or drop); its source tile stays out of the way.
        if (item.Id == _dragItemId) button.Opacity = _dragGhost is null ? 0.4 : 0;

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
            BeginDragVisuals(button, item.Id, e.GetPosition(Stage));
            try
            {
                DragDrop.DoDragDrop(button, new DataObject(DragItemFormat, item.Id), DragDropEffects.Move);
            }
            finally
            {
                EndDragVisuals(button);
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
                    _folderManager.RenameItem(_folder.Id, item.Id, newName.Trim());
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

        var changeIcon = new MenuItem { Header = "Change icon..." };
        changeIcon.Click += (_, _) => ChangeItemIcon(item);
        menu.Items.Add(changeIcon);

        if (!string.IsNullOrEmpty(item.CustomIconPath))
        {
            var resetIcon = new MenuItem { Header = "Reset icon" };
            resetIcon.Click += (_, _) =>
            {
                _folderManager.SetItemIcon(_folder.Id, item.Id, null);
                RenderPage();
            };
            menu.Items.Add(resetIcon);
        }

        var remove = new MenuItem { Header = "Remove from folder" };
        remove.Click += (_, _) =>
        {
            _folderManager.RemoveItem(_folder.Id, item.Id);
            if (!CanExpand) _expanded = false;
            RenderItemsAndLayout();
        };
        menu.Items.Add(remove);
    }

    /// <summary>
    /// Change icon...: copies a picture the user picks into the app's own folder and shows it for this item. The file is only
    /// ever decoded as a picture; nothing is run. A picture under 128 px gets a warning but can still be used.
    /// </summary>
    private void ChangeItemIcon(FolderItem item)
    {
        _isShowingDialog = true;
        string? copy = null;
        try
        {
            var ofd = new OpenFileDialog
            {
                Title = $"Choose an icon for {item.DisplayName}. {ItemIconFiles.RecommendationText}",
                Filter = "Pictures (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
            };
            if (ofd.ShowDialog(this) != true) return;

            // Copy first (this also checks the type and the size limit), then make sure the copy really is a picture.
            copy = ItemIconFiles.Store(ofd.FileName, item.Id, _folderManager.ItemIconsDirectory);

            int largest;
            using (var stream = File.OpenRead(copy))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                largest = decoder.Frames.Max(f => Math.Max(f.PixelWidth, f.PixelHeight));
            }
            if (largest <= 0) throw new InvalidDataException("The file has no picture in it.");

            if (largest < ItemIconFiles.MinRecommendedPixels)
            {
                var answer = GlassMessageBox.Show(this, "Small picture",
                    $"This picture is only {largest} px across, so it may look soft.\n\n{ItemIconFiles.RecommendationText}\n\nUse it anyway?",
                    GlassMessageButtons.YesNo, GlassMessageKind.Question, yesLabel: "Use it", noLabel: "Cancel");
                if (answer != GlassMessageResult.Yes) return; // the copy is removed below
            }

            if (_folderManager.SetItemIcon(_folder.Id, item.Id, copy))
            {
                DiagnosticService.Instance.LogInfo($"Item '{item.DisplayName}' now shows a chosen icon");
                copy = null; // now owned by the item
                RenderPage();
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FileFormatException)
        {
            DiagnosticService.Instance.LogWarn($"Icon for '{item.DisplayName}' rejected: {ex.Message}");
            GlassMessageBox.Show(this, "Change icon", $"That picture can't be used.\n\n{ex.Message}", kind: GlassMessageKind.Warning);
        }
        finally
        {
            ItemIconFiles.DeleteCopy(copy, _folderManager.ItemIconsDirectory); // only set when the icon was not applied
            _isShowingDialog = false;
            Activate();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryMoveItemFocus(e.Key))
        {
            e.Handled = true;
            return;
        }

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

    /// <summary>
    /// Arrows and Home/End move focus between the tiles when a tile already has it. With no tile focused
    /// they keep their old job of changing pages (Left/Right/Home/End), so both ways of browsing still work.
    /// </summary>
    private bool TryMoveItemFocus(Key key)
    {
        if (Keyboard.FocusedElement is not Button focused || !ItemsGrid.Children.Contains(focused)) return false;

        PopupMove? move = key switch
        {
            Key.Left => PopupMove.Left,
            Key.Right => PopupMove.Right,
            Key.Up => PopupMove.Up,
            Key.Down => PopupMove.Down,
            Key.Home => PopupMove.Home,
            Key.End => PopupMove.End,
            _ => null,
        };
        if (move is null) return false;

        int pageSize = ActiveGrid.PageSize;
        int pageStart = _currentPage * pageSize;
        int localIndex = ItemsGrid.Children.IndexOf(focused);
        int target = PopupNavigation.Move(pageStart + localIndex, move.Value,
            ItemsGrid.Columns, pageSize, _folder.Items.Count, pageStart);

        int targetLocal = target - pageStart;
        if (targetLocal != localIndex && targetLocal >= 0 && targetLocal < ItemsGrid.Children.Count
            && ItemsGrid.Children[targetLocal] is Button targetButton)
        {
            targetButton.Focus();
            Keyboard.Focus(targetButton);
        }
        return true;
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

    // While a tile is dragged, a captured copy of it rides above the card and the tiles on the page
    // move apart to open the cell it would land in.
    private string? _dragItemId;
    private FrameworkElement? _dragGhost;
    private Point _dragGrab;
    private readonly Dictionary<string, Vector> _reflowOffsets = [];
    private Rect? _dropSettle;

    private static readonly IEasingFunction DragEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>The item index under the pointer while an item from this folder is being dragged.</summary>
    private int DropIndexAt(DragEventArgs e)
    {
        Point p = e.GetPosition(ItemsGrid);
        return PopupPaging.DropIndex(p.X, p.Y, ItemsGrid.ActualWidth, ItemsGrid.ActualHeight,
            ItemsGrid.Columns, ItemsGrid.Rows, _currentPage, ActiveGrid.PageSize, _folder.Items.Count);
    }

    /// <summary>The page dot the pointer is over, or null. The transparent padding around a dot counts.</summary>
    private int? PageDotAt(DragEventArgs e)
    {
        if (TotalPages <= 1) return null;
        for (int page = 0; page < PageDots.Children.Count; page++)
        {
            if (PageDots.Children[page] is not FrameworkElement dot) continue;
            Point p = e.GetPosition(dot);
            if (p.X >= 0 && p.Y >= 0 && p.X < dot.ActualWidth && p.Y < dot.ActualHeight) return page;
        }
        return null;
    }

    /// <summary>
    /// Starts, keeps or cancels the page turn for the point the drag is over. An edge or dot turns the
    /// page once per entry; the pointer has to leave it before it can turn another page.
    /// </summary>
    private void UpdatePageFlip(DragEventArgs e)
    {
        Point p = e.GetPosition(ItemsGrid);
        var turn = PopupDragPaging.Target(p.X, p.Y, ItemsGrid.ActualWidth, ItemsGrid.ActualHeight,
            _currentPage, TotalPages, PageDotAt(e));

        if (turn is not { } t || t.Zone == _pageFlipZone)
        {
            if (turn is null) _pageFlipZone = null;
            StopPageFlipTimer();
            return;
        }

        ArmPageFlip(t);
    }

    private void ArmPageFlip(PopupDragPaging.Turn turn)
    {
        if (_pageFlipPending == turn) return; // already counting down to this page
        StopPageFlipTimer();
        if (_hwnd == 0) return;

        _pageFlipPending = turn;
        _pageFlipTimerRunning = SetTimer(_hwnd, (nint)PageFlipTimerId, PageFlipDelayMs, 0) != 0;
        if (!_pageFlipTimerRunning)
        {
            DiagnosticService.Instance.LogDebug("Could not start the drag page-flip timer.");
            _pageFlipPending = null;
        }
    }

    private void StopPageFlipTimer()
    {
        if (_pageFlipTimerRunning && _hwnd != 0)
            KillTimer(_hwnd, (nint)PageFlipTimerId);
        _pageFlipTimerRunning = false;
        _pageFlipPending = null;
    }

    private void EndDragPaging()
    {
        StopPageFlipTimer();
        _pageFlipZone = null;
        _dragOverPopup = false;
    }

    /// <summary>The hold ran out: turn to the page the pointer asked for, without the page-in slide.</summary>
    private void OnPageFlipTimer()
    {
        var turn = _pageFlipPending;
        _pageFlipPending = null;
        _pageFlipTimerRunning = false;
        if (turn is not { } t || !_dragOverPopup || _closing) return;

        _pageFlipZone = t.Zone;
        // The grid is rebuilt under the pointer, so the old marker no longer points at a real cell.
        HideDropMarker();
        GoToPage(t.Page, animate: false);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == WM_TIMER && (uint)wParam == PageFlipTimerId)
        {
            KillTimer(hwnd, (nint)PageFlipTimerId);
            _pageFlipTimerRunning = false;
            OnPageFlipTimer();
            handled = true;
        }
        return 0;
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
                IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed,
            };
            GridHost.Children.Add(_dropMarker);
        }
        _dropMarker.Width = cellW - 20;
        _dropMarker.Height = cellH - 4;
        var target = new Thickness((local % ItemsGrid.Columns) * cellW + 10, (local / ItemsGrid.Columns) * cellH, 0, 0);
        if (_dropMarker.Visibility == Visibility.Visible && _dropMarker.Opacity > 0)
        {
            if (_dropMarker.Margin != target)
                Animate.SlideMargin(_dropMarker, target, TimeSpan.FromMilliseconds(140), AnimationCategory.DragGhost);
        }
        else
        {
            // First appearance: place it where it belongs and fade it in.
            _dropMarker.BeginAnimation(FrameworkElement.MarginProperty, null);
            _dropMarker.Margin = target;
            _dropMarker.Visibility = Visibility.Visible;
            Animate.Fade(_dropMarker, 1, TimeSpan.FromMilliseconds(120), AnimationCategory.DragGhost);
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        HideDropMarker();
        ResetDragReflow();
        EndDragPaging();
    }

    private void HideDropMarker()
    {
        if (_dropMarker is null) return;
        _dropMarker.BeginAnimation(FrameworkElement.MarginProperty, null);
        _dropMarker.BeginAnimation(OpacityProperty, null);
        _dropMarker.Opacity = 0;
        _dropMarker.Visibility = Visibility.Collapsed;
    }

    /// <summary>Captures the tile and starts a lifted copy of it under the pointer.</summary>
    private void BeginDragVisuals(FrameworkElement source, string itemId, Point pointer)
    {
        _dragItemId = itemId;
        _reflowOffsets.Clear();
        _dropSettle = null;

        double fit = CardRoot.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1;
        double width = source.ActualWidth * fit, height = source.ActualHeight * fit;
        Point topLeft = source.TranslatePoint(new Point(0, 0), Stage);

        if (CaptureTile(source) is { } capture && width > 1 && height > 1)
        {
            var image = new Image { Source = capture, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var ghost = new Grid
            {
                Width = width,
                Height = height,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 9,
                    Direction = 270,
                    Opacity = 0.45,
                    Color = Color.FromRgb(0x02, 0x0A, 0x24),
                },
            };
            ghost.Children.Add(image);

            _dragGrab = new Point(pointer.X - topLeft.X, pointer.Y - topLeft.Y);
            ghost.RenderTransformOrigin = new Point(
                Math.Clamp(_dragGrab.X / width, 0, 1), Math.Clamp(_dragGrab.Y / height, 0, 1));
            ghost.RenderTransform = new ScaleTransform(1, 1);
            Canvas.SetLeft(ghost, topLeft.X);
            Canvas.SetTop(ghost, topLeft.Y);

            _dragGhost = ghost;
            DragLayer.Children.Add(ghost);
            if (AnimationService.Instance.ShouldAnimate(AnimationCategory.DragGhost))
                Animate.Scale(ghost, 1.06, TimeSpan.FromMilliseconds(140), AnimationCategory.DragGhost);
        }

        source.Opacity = _dragGhost is null ? 0.4 : 0;
    }

    private static ImageSource? CaptureTile(FrameworkElement source)
    {
        try
        {
            if (source.ActualWidth < 1 || source.ActualHeight < 1) return null;
            double dpi = VisualTreeHelper.GetDpi(source).DpiScaleX;
            int width = Math.Max(1, (int)Math.Ceiling(source.ActualWidth * dpi));
            int height = Math.Max(1, (int)Math.Ceiling(source.ActualHeight * dpi));
            var bitmap = new RenderTargetBitmap(width, height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(source);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Could not capture the dragged tile", ex);
            return null;
        }
    }

    private void PlaceDragGhost(Point pointer)
    {
        if (_dragGhost is null) return;
        Canvas.SetLeft(_dragGhost, pointer.X - _dragGrab.X);
        Canvas.SetTop(_dragGhost, pointer.Y - _dragGrab.Y);
    }

    /// <summary>
    /// Opens the drop cell by sliding the page's tiles along, and hides the dragged tile's own cell.
    /// The offsets are kept so a pointer that moves back and forth only animates tiles that move.
    /// </summary>
    private void ApplyDragReflow(int dropIndex)
    {
        if (_dragItemId is null || _dragGhost is null) return;

        int tiles = ItemsGrid.Children.Count;
        if (tiles == 0) return;

        int source = -1;
        for (int i = 0; i < tiles; i++)
        {
            if (ItemsGrid.Children[i] is FrameworkElement { Tag: string id } && id == _dragItemId)
            {
                source = i;
                break;
            }
        }

        int[] slots = PopupDragPaging.ReflowSlots(tiles, source, dropIndex - _currentPage * ActiveGrid.PageSize);
        double cellW = ItemsGrid.ActualWidth / Math.Max(1, ItemsGrid.Columns);
        double cellH = ItemsGrid.ActualHeight / Math.Max(1, ItemsGrid.Rows);

        for (int i = 0; i < tiles; i++)
        {
            if (ItemsGrid.Children[i] is not FrameworkElement { Tag: string id } tile) continue;
            if (i == source) { tile.Opacity = 0; continue; }
            if (slots[i] < 0) continue;

            int dx = slots[i] % ItemsGrid.Columns - i % ItemsGrid.Columns;
            int dy = slots[i] / ItemsGrid.Columns - i / ItemsGrid.Columns;
            int cells = ItemsGrid.Columns * ItemsGrid.Rows;
            var offset = slots[i] >= cells
                ? new Vector(cellW, 0) // pushed past the last cell, onto the next page
                : new Vector(dx * cellW, dy * cellH);
            if (_reflowOffsets.TryGetValue(id, out Vector was) && was == offset) continue;

            _reflowOffsets[id] = offset;
            Animate.Shift(tile, offset, TimeSpan.FromMilliseconds(170), AnimationCategory.DragGhost);
        }
    }

    /// <summary>Lets the page's tiles glide back into their cells when the drop target goes away.</summary>
    private void ResetDragReflow()
    {
        if (_reflowOffsets.Count == 0) return;

        foreach (UIElement child in ItemsGrid.Children)
        {
            if (child is FrameworkElement tile && tile.RenderTransform is TranslateTransform)
                Animate.Shift(tile, default, TimeSpan.FromMilliseconds(160), AnimationCategory.DragGhost);
        }
        _reflowOffsets.Clear();
    }

    /// <summary>Finds the tile on the current page for an item, if it is there.</summary>
    private FrameworkElement? FindTile(string itemId)
    {
        foreach (UIElement child in ItemsGrid.Children)
            if (child is FrameworkElement { Tag: string id } tile && id == itemId) return tile;
        return null;
    }

    /// <summary>The tile's rectangle on the stage, including the popup's fit-to-screen scale.</summary>
    private Rect TileRect(FrameworkElement tile)
    {
        double fit = CardRoot.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1;
        return new Rect(tile.TranslatePoint(new Point(0, 0), Stage),
            new Size(tile.ActualWidth * fit, tile.ActualHeight * fit));
    }

    /// <summary>
    /// Ends the drag: the ghost either settles into a cell or fades where it was released, while the
    /// real tile fades in underneath and the rest of the page finishes its move.
    /// </summary>
    private void EndDragVisuals(FrameworkElement source)
    {
        HideDropMarker();
        EndDragPaging();
        ResetDragReflow();

        Rect? settle = _dropSettle;
        _dropSettle = null;
        FrameworkElement? tile = _dragItemId is { } id ? FindTile(id) : null;

        // A cancelled drag goes back to the tile's own cell when that page is still showing.
        if (settle is null && tile is not null) settle = TileRect(tile);

        _dragItemId = null;

        if (_dragGhost is null)
        {
            if (tile is not null) tile.Opacity = 1;
            else source.Opacity = 1;
            return;
        }

        if (settle is { } rect) SettleGhost(rect, tile);
        else FadeGhostAway(tile);
    }

    /// <summary>Flies the lifted tile into its cell and fades it out as the real tile fades in.</summary>
    private void SettleGhost(Rect target, FrameworkElement? tile)
    {
        var ghost = _dragGhost;
        _dragGhost = null;
        if (ghost is null)
        {
            if (tile is not null) tile.Opacity = 1;
            return;
        }

        if (tile is not null) tile.Opacity = 0;
        if (!AnimationService.Instance.ShouldAnimate(AnimationCategory.DragGhost))
        {
            DragLayer.Children.Remove(ghost);
            if (tile is not null) tile.Opacity = 1;
            return;
        }

        var duration = AnimationService.Instance.GetDuration(TimeSpan.FromMilliseconds(190), AnimationCategory.DragGhost);
        var top = new DoubleAnimation(target.Top, duration) { EasingFunction = DragEase };
        top.Completed += (_, _) => DragLayer.Children.Remove(ghost);
        ghost.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(target.Left, duration) { EasingFunction = DragEase });
        ghost.BeginAnimation(Canvas.TopProperty, top);
        Animate.Scale(ghost, 1, duration, AnimationCategory.DragGhost);
        Animate.Fade(ghost, 0, duration, AnimationCategory.DragGhost);
        if (tile is not null) Animate.Fade(tile, 1, duration, AnimationCategory.DragGhost);
    }

    /// <summary>Fades the lifted tile where it is, for a drop that has no cell to return to.</summary>
    private void FadeGhostAway(FrameworkElement? tile)
    {
        var ghost = _dragGhost;
        _dragGhost = null;
        if (ghost is null)
        {
            if (tile is not null) tile.Opacity = 1;
            return;
        }

        if (!AnimationService.Instance.ShouldAnimate(AnimationCategory.DragGhost))
        {
            DragLayer.Children.Remove(ghost);
            if (tile is not null) tile.Opacity = 1;
            return;
        }

        Animate.Fade(ghost, 0, TimeSpan.FromMilliseconds(140), AnimationCategory.DragGhost,
            () => DragLayer.Children.Remove(ghost));
        if (tile is not null) Animate.Fade(tile, 1, TimeSpan.FromMilliseconds(140), AnimationCategory.DragGhost);
    }

    /// <summary>Drops every drag visual at once, for a popup that is closing.</summary>
    private void CancelDragVisuals()
    {
        if (_dragGhost is not null)
        {
            DragLayer.Children.Remove(_dragGhost);
            _dragGhost = null;
        }
        _dropSettle = null;
        _dragItemId = null;
        _reflowOffsets.Clear();
    }

    /// <summary>
    /// Where each tile on this page sits now, by item, so a reorder can glide them to their new places.
    /// <see cref="UIElement.TranslatePoint"/> leaves out the tile's own translation, so it is added here.
    /// </summary>
    private Dictionary<string, Point> TilePositions()
    {
        var map = new Dictionary<string, Point>();
        foreach (UIElement child in ItemsGrid.Children)
        {
            if (child is not FrameworkElement { Tag: string id } tile) continue;
            Point at = tile.TranslatePoint(new Point(0, 0), ItemsGrid);
            if (tile.RenderTransform is TranslateTransform move) at += new Vector(move.X, move.Y);
            map[id] = at;
        }
        return map;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragItemFormat))
        {
            _dragOverPopup = true;
            e.Effects = DragDropEffects.Move;
            int dropIndex = DropIndexAt(e);
            ShowDropMarker(dropIndex);
            ApplyDragReflow(dropIndex);
            PlaceDragGhost(e.GetPosition(Stage));
            UpdatePageFlip(e);
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
        HideDropMarker();
        EndDragPaging();
        if (e.Data.GetDataPresent(DragItemFormat))
        {
            try
            {
                if (e.Data.GetData(DragItemFormat) is string id)
                {
                    int from = _folder.Items.FindIndex(i => i.Id == id);
                    int to = DropIndexAt(e);
                    if (from >= 0)
                    {
                        var before = TilePositions();
                        bool moved = from != to && _folderManager.ReorderItem(_folder.Id, from, to);
                        if (moved)
                        {
                            RenderItemsAndLayout();
                            ItemsGrid.UpdateLayout();
                            _reflowOffsets.Clear();
                            foreach (UIElement child in ItemsGrid.Children)
                            {
                                if (child is not FrameworkElement { Tag: string movedId } tile
                                    || movedId == id
                                    || !before.TryGetValue(movedId, out Point old)) continue;
                                Animate.Glide(tile, old - tile.TranslatePoint(new Point(0, 0), ItemsGrid),
                                    TimeSpan.FromMilliseconds(260), AnimationCategory.DragGhost);
                            }
                        }

                        // The lifted tile lands in the cell the drop marker was holding open.
                        if (FindTile(id) is { } settled) _dropSettle = TileRect(settled);
                    }
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
