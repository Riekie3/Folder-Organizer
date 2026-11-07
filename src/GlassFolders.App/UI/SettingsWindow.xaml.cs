using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GlassFolders.App.Services;
using GlassFolders.Core.Backup;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Native;
using Microsoft.Win32;

namespace GlassFolders.App.UI;

/// <summary>
/// Settings window styled with glass sidebar navigation and controls per PLAN.md reference designs.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly ExplorerRecoveryService? _recoveryService;
    private readonly MonitorService? _monitorService;
    private readonly BackupService _backupService;
    private readonly AppSettings _settings;

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

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            DwmHelpers.EnableMica(hwnd, darkMode: true);
        };

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        };
    }

    private bool _loadingUi;

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
        SliderGrid.Value = _settings.GridSize;
        TxtGridVal.Text = _settings.GridSize.ToString();

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

        switch (state)
        {
            case RecoveryState.Healthy:
                BadgeRecoveryState.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x35, 0xD0, 0x7F));
                TxtRecoveryState.Foreground = new SolidColorBrush(Color.FromRgb(0x35, 0xD0, 0x7F));
                break;
            case RecoveryState.Failed:
                BadgeRecoveryState.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x45, 0x3A));
                TxtRecoveryState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
                break;
            case RecoveryState.Paused:
                BadgeRecoveryState.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x0A));
                TxtRecoveryState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                break;
            default:
                BadgeRecoveryState.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF));
                TxtRecoveryState.Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
                break;
        }
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
        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = page == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        PageAnimation.Visibility = page == "Animation" ? Visibility.Visible : Visibility.Collapsed;
        PageRecovery.Visibility = page == "Recovery" ? Visibility.Visible : Visibility.Collapsed;
        PageBackup.Visibility = page == "Backup" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        TxtPageTitle.Text = page switch
        {
            "Appearance" => "Appearance & Glass Effects",
            "Layout" => "Desktop Layout & Displays",
            "Animation" => "Animation Controls",
            "Recovery" => "Explorer Recovery & Shell",
            "Backup" => "Portable Backup & Restore",
            "About" => "About GlassFolders",
            _ => "General Settings"
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

    private void OnGridSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtGridVal is not null && _settings is not null)
        {
            int val = (int)e.NewValue;
            TxtGridVal.Text = val.ToString();
            _settings.GridSize = val;
            SaveSettings();
        }
    }

    private void OnColumnsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtColumnsVal is not null && _settings is not null)
        {
            int val = (int)e.NewValue;
            TxtColumnsVal.Text = val.ToString();
            _settings.PopupColumns = val;
            SaveSettings();
        }
    }

    private void OnRowsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtRowsVal is not null && _settings is not null)
        {
            int val = (int)e.NewValue;
            TxtRowsVal.Text = val.ToString();
            _settings.PopupRows = val;
            SaveSettings();
        }
    }

    private void OnAnimationSettingChanged(object sender, RoutedEventArgs e)
    {
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
        if (TxtAnimSpeedVal != null)
        {
            double speed = Math.Round(e.NewValue, 2);
            TxtAnimSpeedVal.Text = $"{speed:0.0}x";
            _settings.Animation.SpeedMultiplier = speed;
            AnimationService.Instance.Settings.SpeedMultiplier = speed;
            SaveSettings();
        }
    }

    private void OnBtnResetAnimDefaultsClicked(object sender, RoutedEventArgs e)
    {
        _settings.Animation = new AnimationSettings();
        LoadSettingsToUi();
        SaveSettings();
    }

    private void OnBtnNewFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderEditorDialog();
        if (dialog.ShowDialog() == true)
        {
            var res = dialog.ResultFolder;
            var created = _folderManager.CreateFolder(res.Name, res.ColorA, res.ColorB, res.Glyph);
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
                MessageBox.Show("Backup successfully exported!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Export backup failed", ex);
                MessageBox.Show($"Failed to export backup:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                string message = $"Backup archive contents:\n" +
                                 $"• Total Folders: {plan.TotalFolders}\n" +
                                 $"• Total Items: {plan.TotalItems}\n" +
                                 $"• Created on PC: {plan.Manifest.MachineName} ({plan.Manifest.CreatedAt:yyyy-MM-dd HH:mm})\n\n" +
                                 $"Choose an import action:\n" +
                                 $"• Yes: Replace your existing folders completely.\n" +
                                 $"• No: Merge imported folders into your existing setup.\n" +
                                 $"• Cancel: Abort restore.\n\n" +
                                 $"(A safety auto-backup of your current state will be created first.)";

                var result = MessageBox.Show(message, "Restore Backup", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;

                var mode = result == MessageBoxResult.Yes ? ImportMode.Replace : ImportMode.Merge;
                _backupService.ApplyImport(plan, mode, _configManager, _folderManager);

                RefreshFoldersList();
                MessageBox.Show("Backup successfully restored!", "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Import backup failed", ex);
                MessageBox.Show($"Failed to restore backup:\n\n{ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnBtnResetDefaultsClicked(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Are you sure you want to reset all folders and settings to factory defaults?\n\nAn automatic safety backup will be created first.",
            "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                _backupService.CreateAutoBackup(_configManager, "reset");
                var defaults = ConfigurationManager.CreateDefaultFolders();
                _configManager.SaveFolders(defaults);
                _configManager.SaveSettings(new AppSettings());
                _folderManager.Reload();
                RefreshFoldersList();
                LoadSettingsToUi();
                MessageBox.Show("Configuration reset to defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticService.Instance.LogError("Reset to defaults failed", ex);
            }
        }
    }

    private void SaveSettings()
    {
        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.CheckMissingTargets = ChkCheckMissing.IsChecked == true;
        _settings.SnapToGrid = ChkSnapToGrid.IsChecked == true;
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
    private void OnBtnCloseClicked(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }
}
