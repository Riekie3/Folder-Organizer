using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GlassFolders.App.Services;
using GlassFolders.Core.Config;
using GlassFolders.Core.Folders;
using GlassFolders.Core.Models;
using GlassFolders.Native;

namespace GlassFolders.App.UI;

/// <summary>
/// Settings window styled with glass sidebar navigation and controls per PLAN.md reference designs.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigurationManager _configManager;
    private readonly FolderManager _folderManager;
    private readonly ExplorerRecoveryService? _recoveryService;
    private readonly AppSettings _settings;

    public event Action? SettingsChanged;
    public event Action<FolderDef>? FolderCreated;

    public SettingsWindow(ConfigurationManager configManager, FolderManager folderManager, ExplorerRecoveryService? recoveryService = null)
    {
        InitializeComponent();
        _configManager = configManager;
        _folderManager = folderManager;
        _recoveryService = recoveryService;
        _settings = _configManager.LoadSettings();

        LoadSettingsToUi();
        RefreshFoldersList();
        UpdateRecoveryUi();

        if (_recoveryService != null)
        {
            _recoveryService.StateChanged += _ => Dispatcher.Invoke(UpdateRecoveryUi);
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

    private void LoadSettingsToUi()
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
    }

    private void RefreshFoldersList()
    {
        FoldersList.ItemsSource = null;
        FoldersList.ItemsSource = _folderManager.Folders;
    }

    private void UpdateRecoveryUi()
    {
        if (_recoveryService == null) return;

        var state = _recoveryService.State;
        TxtRecoveryState.Text = state.ToString();
        TxtRecoveryLastEvent.Text = $"{_recoveryService.LastEventMessage} ({_recoveryService.LastEventTime:HH:mm:ss})";
        TxtReattachCount.Text = _recoveryService.ReattachCount.ToString();

        // Update badge color based on state
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
    private void OnNavRecoveryClicked(object sender, RoutedEventArgs e) => ShowPage("Recovery");
    private void OnNavAboutClicked(object sender, RoutedEventArgs e) => ShowPage("About");

    public void ShowPage(string page)
    {
        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = page == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        PageRecovery.Visibility = page == "Recovery" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        TxtPageTitle.Text = page switch
        {
            "Appearance" => "Appearance & Glass Effects",
            "Layout" => "Desktop Layout & Folders",
            "Recovery" => "Explorer Recovery & Shell",
            "About" => "About GlassFolders",
            _ => "General Settings"
        };

        if (page == "Layout")
            RefreshFoldersList();
        else if (page == "Recovery")
            UpdateRecoveryUi();
    }

    private void OnGridSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtGridVal is not null)
        {
            int val = (int)e.NewValue;
            TxtGridVal.Text = val.ToString();
            _settings.GridSize = val;
            SaveSettings();
        }
    }

    private void OnColumnsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtColumnsVal is not null)
        {
            int val = (int)e.NewValue;
            TxtColumnsVal.Text = val.ToString();
            _settings.PopupColumns = val;
            SaveSettings();
        }
    }

    private void OnRowsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtRowsVal is not null)
        {
            int val = (int)e.NewValue;
            TxtRowsVal.Text = val.ToString();
            _settings.PopupRows = val;
            SaveSettings();
        }
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

    private void SaveSettings()
    {
        _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _settings.CheckMissingTargets = ChkCheckMissing.IsChecked == true;
        _settings.SnapToGrid = ChkSnapToGrid.IsChecked == true;
        _configManager.SaveSettings(_settings);
        SettingsChanged?.Invoke();
    }

    private void OnBtnCloseClicked(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }
}
