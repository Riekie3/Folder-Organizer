using System.Windows;
using System.Windows.Interop;
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
    private readonly AppSettings _settings;

    public event Action? SettingsChanged;
    public event Action<FolderDef>? FolderCreated;

    public SettingsWindow(ConfigurationManager configManager, FolderManager folderManager)
    {
        InitializeComponent();
        _configManager = configManager;
        _folderManager = folderManager;
        _settings = _configManager.LoadSettings();

        LoadSettingsToUi();
        RefreshFoldersList();

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
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
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

    private void OnNavGeneralClicked(object sender, RoutedEventArgs e) => ShowPage("General");
    private void OnNavAppearanceClicked(object sender, RoutedEventArgs e) => ShowPage("Appearance");
    private void OnNavLayoutClicked(object sender, RoutedEventArgs e) => ShowPage("Layout");
    private void OnNavAboutClicked(object sender, RoutedEventArgs e) => ShowPage("About");

    private void ShowPage(string page)
    {
        PageGeneral.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = page == "Layout" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        TxtPageTitle.Text = page switch
        {
            "Appearance" => "Appearance & Glass Effects",
            "Layout" => "Desktop Layout & Folders",
            "About" => "About GlassFolders",
            _ => "General Settings"
        };

        if (page == "Layout")
            RefreshFoldersList();
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
