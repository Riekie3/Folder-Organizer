using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GlassFolders.App.Services;
using GlassFolders.Core.Backup;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Native;
using Microsoft.Win32;

namespace GlassFolders.App.UI;

/// <summary>
/// Settings, drawn on a glass card in the style of reference 3. Appearance changes apply to this window
/// as you make them, and are saved a moment after the last change.
/// </summary>
public partial class SettingsWindow : GlassWindow
{
    // Slider drags change a value many times a second; the file is written once they pause.
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly ExplorerRecoveryService? _recoveryService;
    private readonly MonitorService? _monitorService;
    private readonly BackupService _backupService;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;

    public event Action? SettingsChanged;
    public event Action<FolderDef>? FolderCreated;

    public SettingsWindow(
        ConfigurationManager configManager,
        FolderManager folderManager,
        ExplorerRecoveryService? recoveryService = null,
        MonitorService? monitorService = null)
    {

        _configManager = configManager;
        _folderManager = folderManager;
        _recoveryService = recoveryService;
        _monitorService = monitorService;
        _backupService = new BackupService();
        _settings = _configManager.LoadSettings();
        InitializeComponent();
        _built = true;
        UseGlass(Card);

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushPendingSave();
        Closing += (_, _) => FlushPendingSave();
        OnAppearanceApplied();

        LoadSettingsToUi();
        var ver = typeof(SettingsWindow).Assembly.GetName().Version;
        string vtxt = ver is null ? "" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        TxtSidebarVersion.Text = $"v{vtxt}";
        TxtAboutVersion.Text = $"Version {vtxt}";
        RefreshFoldersList();
        RefreshMonitorsList();
        UpdateRecoveryUi();

        if (_recoveryService != null)
        {
            _recoveryService.StateChanged += _ => Dispatcher.Invoke(UpdateRecoveryUi);
        }

        if (_monitorService != null)
        {
            _monitorService.MonitorsChanged += _ => Dispatcher.Invoke(RefreshMonitorsList);
        }
    }

    private bool _loadingUi;

    // Radio buttons raise Checked while the window is still being built, before the pages exist.
    private bool _built;

    /// <summary>The sidebar's left corners follow the card's, which follow the corner radius setting.</summary>
    protected override void OnAppearanceApplied()
    {
        double r = AppearanceService.Instance.Glass.ChromeCornerRadius;
        Sidebar.CornerRadius = new CornerRadius(r, 0, 0, r);
        UpdateRecoveryUi();
    }

    private void OnDragRegionMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.OriginalSource is not (Button or System.Windows.Shapes.Path))
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void LoadSettingsToUi()
    {
        _loadingUi = true;
        try { LoadSettingsToUiCore(); }
        finally { _loadingUi = false; }
    }

    private void LoadSettingsToUiCore()
    {
        ChkStartWithWindows.IsChecked = StartupService.IsStartupEnabled();
        ChkCheckMissing.IsChecked = _settings.CheckMissingTargets;
        ChkSnapToGrid.IsChecked = _settings.SnapToGrid;
        ChkShowItemCount.IsChecked = _settings.ShowItemCount;

        // Appearance
        RbThemeSystem.IsChecked = _settings.Theme == AppTheme.System;
        RbThemeLight.IsChecked = _settings.Theme == AppTheme.Light;
        RbThemeDark.IsChecked = _settings.Theme == AppTheme.Dark;
        SliderTransparency.Value = _settings.GlassTransparency;
        SliderBlur.Value = _settings.GlassBlur;
        SliderBorder.Value = _settings.GlassBorder;
        SliderShadow.Value = _settings.GlassShadow;
        SliderCorner.Value = Math.Clamp(_settings.CornerRadius, GlassAppearance.MinCornerRadius, GlassAppearance.MaxCornerRadius);
        ShowSliderValues();
        RbIconSmall.IsChecked = _settings.PopupIconSize == PopupIconSize.Small;
        RbIconMedium.IsChecked = _settings.PopupIconSize == PopupIconSize.Medium;
        RbIconLarge.IsChecked = _settings.PopupIconSize == PopupIconSize.Large;

        SliderColumns.Value = _settings.PopupColumns;
        TxtColumnsVal.Text = _settings.PopupColumns.ToString();
        SliderRows.Value = _settings.PopupRows;
        TxtRowsVal.Text = _settings.PopupRows.ToString();

        // Animations page
        var anim = _settings.Animation;
        ChkGlobalAnim.IsChecked = anim.GlobalEnabled;
        ChkFollowWindowsAnim.IsChecked = anim.FollowWindows;
        SliderAnimSpeed.Value = anim.SpeedMultiplier;
        TxtAnimSpeedVal.Text = $"{anim.SpeedMultiplier:0.0}x";

        ChkAnimOpenClose.IsChecked = anim.FolderOpenClose;
        ChkAnimTileHover.IsChecked = anim.TileHover;
        ChkAnimItemPress.IsChecked = anim.ItemPress;
        ChkAnimPageTrans.IsChecked = anim.PageTransition;
        ChkAnimWindowTrans.IsChecked = anim.WindowTransition;
        ChkAnimDragGhost.IsChecked = anim.DragGhost;
        ChkAnimShimmer.IsChecked = anim.Shimmer;
        ChkAnimSettingsNav.IsChecked = anim.SettingsNav;

        CardAnimCategories.IsEnabled = anim.GlobalEnabled;
        AnimationService.Instance.Settings = anim;

        // Diagnostics
        switch (LogLevelSetting.Normalize(_settings.LogLevel))
        {
            case LogLevelSetting.Error: RbLogError.IsChecked = true; break;
            case LogLevelSetting.Warning: RbLogWarning.IsChecked = true; break;
            case LogLevelSetting.Debug: RbLogDebug.IsChecked = true; break;
            default: RbLogInfo.IsChecked = true; break;
        }
    }

    private void ShowSliderValues()
    {
        TxtTransparencyVal.Text = $"{(int)SliderTransparency.Value}%";
        TxtBlurVal.Text = $"{(int)SliderBlur.Value}%";
        TxtBorderVal.Text = $"{(int)SliderBorder.Value}%";
        TxtShadowVal.Text = $"{(int)SliderShadow.Value}%";
        TxtCornerVal.Text = $"{(int)SliderCorner.Value}";
    }

    private void RefreshFoldersList()
    {
        FoldersList.ItemsSource = null;
        FoldersList.ItemsSource = _folderManager.Folders;
    }

    private void RefreshMonitorsList()
    {
        if (_monitorService != null)
        {
            MonitorsList.ItemsSource = null;
            MonitorsList.ItemsSource = _monitorService.Monitors;
        }
    }

    private void UpdateRecoveryUi()
    {
        if (_recoveryService == null) return;

        var state = _recoveryService.State;
        TxtRecoveryState.Text = state.ToString();
        TxtRecoveryLastEvent.Text = $"{_recoveryService.LastEventMessage} ({_recoveryService.LastEventTime:HH:mm:ss})";
        TxtReattachCount.Text = _recoveryService.ReattachCount.ToString();

        string key = state switch
        {
            RecoveryState.Healthy => "ChromeOk",
            RecoveryState.Failed => "ChromeDanger",
            RecoveryState.Paused => "ChromeWarn",
            _ => "ChromeLink",
        };
        var ink = (SolidColorBrush)FindResource(key);
        TxtRecoveryState.Foreground = ink;
        BadgeRecoveryState.Background = new SolidColorBrush(Color.FromArgb(0x2E, ink.Color.R, ink.Color.G, ink.Color.B));
    }

    private void OnNavGeneralClicked(object sender, RoutedEventArgs e) => ShowPage("General");
    private void OnNavAppearanceClicked(object sender, RoutedEventArgs e) => ShowPage("Appearance");
    private void OnNavLayoutClicked(object sender, RoutedEventArgs e) => ShowPage("Layout");
    private void OnNavAnimationClicked(object sender, RoutedEventArgs e) => ShowPage("Animation");
    private void OnNavRecoveryClicked(object sender, RoutedEventArgs e) => ShowPage("Recovery");
    private void OnNavBackupClicked(object sender, RoutedEventArgs e) => ShowPage("Backup");
    private void OnNavAboutClicked(object sender, RoutedEventArgs e) => ShowPage("About");

    public void ShowPage(string page)
    {
        if (!_built) return;

        // The tray menu and the sidebar both name pages; "Animations" and "Animation" are the same page.
        if (page == "Animations") page = "Animation";

        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = page == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        PageAnimation.Visibility = page == "Animation" ? Visibility.Visible : Visibility.Collapsed;
        PageRecovery.Visibility = page == "Recovery" ? Visibility.Visible : Visibility.Collapsed;
        PageBackup.Visibility = page == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        NavGeneral.IsChecked = page == "General";
        NavAppearance.IsChecked = page == "Appearance";
        NavLayout.IsChecked = page == "Layout";
        NavAnimation.IsChecked = page == "Animation";
        NavRecovery.IsChecked = page == "Recovery";
        NavBackup.IsChecked = page == "Backup";
        NavAbout.IsChecked = page == "About";

        TxtPageTitle.Text = page switch
        {
            "Appearance" => "Appearance",
            "Layout" => "Desktop layout",
            "Animation" => "Animations",
            "Recovery" => "Explorer recovery",
            "Backup" => "Backup and restore",
            "About" => "About",
            _ => "General"
        };

        if (page == "Layout")
        {
            RefreshFoldersList();
            RefreshMonitorsList();
        }
        else if (page == "Recovery")
        {
            UpdateRecoveryUi();
        }
    }

    // Folders snap to Windows' own icon grid, so there is no separate grid size to set.
    private void OnDesktopOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || _settings is null) return;
        SaveSettings();
    }

    private void OnColumnsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi || TxtColumnsVal is null || _settings is null) return;
        int val = (int)e.NewValue;
        TxtColumnsVal.Text = val.ToString();
        _settings.PopupColumns = val;
        SaveSettings();
    }

    private void OnRowsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi || TxtRowsVal is null || _settings is null) return;
        int val = (int)e.NewValue;
        TxtRowsVal.Text = val.ToString();
        _settings.PopupRows = val;
        SaveSettings();
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || _settings is null) return;
        _settings.Theme = sender == RbThemeLight ? AppTheme.Light : sender == RbThemeDark ? AppTheme.Dark : AppTheme.System;
        SaveSettings();
    }

    private void OnAppearanceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders raise this while the window is still being built.
        if (_loadingUi || _settings is null || TxtCornerVal is null) return;
        ShowSliderValues();
        SaveSettings();
    }

    private void OnIconSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || _settings is null) return;
        _settings.PopupIconSize = sender == RbIconSmall ? PopupIconSize.Small : sender == RbIconLarge ? PopupIconSize.Large : PopupIconSize.Medium;
        SaveSettings();
    }

    private void OnLogLevelChecked(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || _settings is null) return;
        _settings.LogLevel = sender == RbLogError ? LogLevelSetting.Error
            : sender == RbLogWarning ? LogLevelSetting.Warning
            : sender == RbLogDebug ? LogLevelSetting.Debug
            : LogLevelSetting.Info;
        SaveSettings();
    }

    private void OnBtnResetAppearanceClicked(object sender, RoutedEventArgs e)
    {
        _settings.GlassTransparency = GlassAppearance.DefaultTransparency;
        _settings.GlassBlur = GlassAppearance.DefaultBlur;
        _settings.GlassBorder = GlassAppearance.DefaultBorder;
        _settings.GlassShadow = GlassAppearance.DefaultShadow;
        _settings.CornerRadius = GlassAppearance.DefaultCornerRadius;
        LoadSettingsToUi();
        SaveSettings();
    }

    private void OnAnimationSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        var anim = _settings.Animation;
        anim.GlobalEnabled = ChkGlobalAnim.IsChecked == true;
        anim.FollowWindows = ChkFollowWindowsAnim.IsChecked == true;

        anim.FolderOpenClose = ChkAnimOpenClose.IsChecked == true;
        anim.TileHover = ChkAnimTileHover.IsChecked == true;
        anim.ItemPress = ChkAnimItemPress.IsChecked == true;
        anim.PageTransition = ChkAnimPageTrans.IsChecked == true;
        anim.WindowTransition = ChkAnimWindowTrans.IsChecked == true;
        anim.DragGhost = ChkAnimDragGhost.IsChecked == true;
        anim.Shimmer = ChkAnimShimmer.IsChecked == true;
        anim.SettingsNav = ChkAnimSettingsNav.IsChecked == true;

        if (CardAnimCategories != null)
            CardAnimCategories.IsEnabled = anim.GlobalEnabled;

        AnimationService.Instance.Settings = anim;
        SaveSettings();
    }

    private void OnAnimSpeedSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi || TxtAnimSpeedVal == null) return;
        double speed = Math.Round(e.NewValue, 2);
        TxtAnimSpeedVal.Text = $"{speed:0.0}x";
        _settings.Animation.SpeedMultiplier = speed;
        AnimationService.Instance.Settings.SpeedMultiplier = speed;
        SaveSettings();
    }

    private void OnBtnResetAnimDefaultsClicked(object sender, RoutedEventArgs e)
    {
        _settings.Animation = new AnimationSettings();
        LoadSettingsToUi();
        SaveSettings();
    }

    private void OnBtnNewFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderEditorDialog(null, _folderManager.Folders.Count) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var res = dialog.ResultFolder;
            var created = _folderManager.CreateFolder(res.Name, res.ColorA, res.ColorB, res.Glyph);
            if (res.Size != created.Size) _folderManager.SetFolderSize(created.Id, res.Size);
            if (res.Style != created.Style) _folderManager.SetFolderStyle(created.Id, res.Style);
            RefreshFoldersList();
            FolderCreated?.Invoke(created);
        }
    }

    private void OnStartupCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        bool enable = ChkStartWithWindows.IsChecked == true;
        StartupService.SetStartup(enable);
        _settings.StartWithWindows = enable;
        _configManager.SaveSettings(_settings);
    }

    private void OnBtnRecoverNowClicked(object sender, RoutedEventArgs e)
    {
        _recoveryService?.RecoverNow();
        UpdateRecoveryUi();
    }

    private void OnBtnOpenLogsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = DiagnosticService.Instance.LogDirectory;
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnBtnExportBackupClicked(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export Portable Backup",
            Filter = "GlassFolders Backup (*.zip)|*.zip",
            FileName = $"GlassFolders-Backup-{DateTime.Now:yyyyMMdd}.zip"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var monitorKeys = _monitorService?.Monitors.Select(m => m.Key).ToList();
                string iconCache = AppPaths.IconCacheDir;
                _backupService.Export(sfd.FileName, _configManager, monitorKeys, iconCache);
                GlassMessageBox.Show(this, "Export complete", "Your backup was saved.", kind: GlassMessageKind.Success);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Export backup failed", ex);
                GlassMessageBox.Show(this, "Export failed", $"The backup could not be saved.\n\n{ex.Message}", kind: GlassMessageKind.Error);
            }
        }
    }

    private void OnBtnImportBackupClicked(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Backup to Restore",
            Filter = "GlassFolders Backup (*.zip)|*.zip"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                var plan = ImportPlanner.CreatePlan(ofd.FileName, _folderManager);

                string message = $"This backup holds {plan.TotalFolders} folders and {plan.TotalItems} items, " +
                                 $"made on {plan.Manifest.MachineName} on {plan.Manifest.CreatedAt:yyyy-MM-dd HH:mm}.\n\n" +
                                 "Replace swaps your current folders for these. Merge adds these next to your current ones.\n\n" +
                                 "A safety backup of your current setup is made first.";

                var result = GlassMessageBox.Show(this, "Restore backup", message, GlassMessageButtons.YesNoCancel, GlassMessageKind.Question,
                    yesLabel: "Replace", noLabel: "Merge");
                if (result is GlassMessageResult.Cancel or GlassMessageResult.None) return;

                var mode = result == GlassMessageResult.Yes ? ImportMode.Replace : ImportMode.Merge;
                _backupService.ApplyImport(plan, mode, _configManager, _folderManager);

                RefreshFoldersList();
                GlassMessageBox.Show(this, "Restore complete", "Your backup was restored.", kind: GlassMessageKind.Success);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Import backup failed", ex);
                GlassMessageBox.Show(this, "Restore failed", $"The backup could not be restored.\n\n{ex.Message}", kind: GlassMessageKind.Error);
            }
        }
    }

    private void OnBtnResetDefaultsClicked(object sender, RoutedEventArgs e)
    {
        var res = GlassMessageBox.Show(this, "Reset to defaults",
            "Put all folders and settings back to the defaults?\n\nA safety backup is made first. Your files and applications are not touched.",
            GlassMessageButtons.YesNo, GlassMessageKind.Warning, yesLabel: "Reset", noLabel: "Cancel", dangerYes: true);

        if (res == GlassMessageResult.Yes)
        {
            try
            {
                _saveTimer.Stop();
                _backupService.CreateAutoBackup(_configManager, "reset");
                var defaults = ConfigurationManager.CreateDefaultFolders();
                _configManager.SaveFolders(defaults);
                var fresh = new AppSettings();
                _configManager.SaveSettings(fresh);
                CopySettings(fresh, _settings);
                _folderManager.Reload();
                RefreshFoldersList();
                LoadSettingsToUi();
                ApplySettingsNow();
                SettingsChanged?.Invoke();
                GlassMessageBox.Show(this, "Reset complete", "Folders and settings are back to the defaults.", kind: GlassMessageKind.Success);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Reset to defaults failed", ex);
                GlassMessageBox.Show(this, "Reset failed", $"Settings could not be reset.\n\n{ex.Message}", kind: GlassMessageKind.Error);
            }
        }
    }

    private static void CopySettings(AppSettings from, AppSettings to)
    {
        to.PopupColumns = from.PopupColumns;
        to.PopupRows = from.PopupRows;
        to.SnapToGrid = from.SnapToGrid;
        to.StartWithWindows = from.StartWithWindows;
        to.LogLevel = from.LogLevel;
        to.CheckMissingTargets = from.CheckMissingTargets;
        to.ShowItemCount = from.ShowItemCount;
        to.Theme = from.Theme;
        to.GlassTransparency = from.GlassTransparency;
        to.GlassBlur = from.GlassBlur;
        to.GlassBorder = from.GlassBorder;
        to.GlassShadow = from.GlassShadow;
        to.CornerRadius = from.CornerRadius;
        to.PopupIconSize = from.PopupIconSize;
        to.Animation = from.Animation;
    }

    /// <summary>Takes the settings on screen into effect now, and saves them shortly after the last change.</summary>
    private void SaveSettings()
    {
        // Toggles that start checked raise Checked while the window is being built.
        if (!_built) return;

        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.CheckMissingTargets = ChkCheckMissing.IsChecked == true;
        _settings.SnapToGrid = ChkSnapToGrid.IsChecked == true;
        _settings.ShowItemCount = ChkShowItemCount.IsChecked == true;
        _settings.GlassTransparency = (int)SliderTransparency.Value;
        _settings.GlassBlur = (int)SliderBlur.Value;
        _settings.GlassBorder = (int)SliderBorder.Value;
        _settings.GlassShadow = (int)SliderShadow.Value;
        _settings.CornerRadius = (int)SliderCorner.Value;

        ApplySettingsNow();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ApplySettingsNow()
    {
        AppearanceService.Instance.Apply(_settings);
        DiagnosticService.Instance.CurrentLogLevel = DiagnosticService.LevelFor(_settings.LogLevel);
    }

    private void FlushPendingSave()
    {
        bool pending = _saveTimer.IsEnabled;
        _saveTimer.Stop();
        if (!pending) return;
        _configManager.SaveSettings(_settings);
        SettingsChanged?.Invoke();
    }

    private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch { }
    }

    private void OnBrandUrlClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://rie-sources.com/") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnBtnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
