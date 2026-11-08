using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GlassFolders.Core.Layout;
using GlassFolders.Core.Models;

namespace GlassFolders.App.UI;

/// <summary>
/// Creates a folder or edits its name, colour, glyph, style and size, with a live preview of the desktop art.
/// </summary>
public partial class FolderEditorDialog : Window
{
    private static readonly (string Name, string GlyphKey)[] Glyphs =
    [
        ("Briefcase", "GlyphBriefcase"),
        ("Gamepad", "GlyphGamepad"),
        ("Play", "GlyphPlay"),
        ("Gear", "GlyphGear"),
        ("Code", "GlyphCode"),
        ("Palette", "GlyphPalette"),
        ("Document", "GlyphDocument"),
        ("Folder", "GlyphFolder"),
    ];

    private readonly FolderDef _folder;
    private readonly FolderDef _preview;
    private bool _ready;

    /// <param name="existing">The folder to edit, or null to create one.</param>
    /// <param name="existingFolders">How many folders there are, so a new one takes the next reference colour.</param>
    public FolderEditorDialog(FolderDef? existing = null, int existingFolders = 0)
    {
        InitializeComponent();
        if (existing is null)
        {
            var color = FolderPalette.ForNewFolder(existingFolders);
            _folder = new FolderDef { Name = "New Folder", ColorA = color.Front, ColorB = color.Back };
        }
        else
        {
            _folder = existing;
        }

        // Edits go to a copy until Save, so Cancel leaves the folder as it was.
        _preview = new FolderDef
        {
            Name = _folder.Name,
            ColorA = _folder.ColorA,
            ColorB = _folder.ColorB,
            Glyph = _folder.Glyph,
            Style = _folder.Style,
            Size = _folder.Size,
        };

        TxtHeader.Text = existing is null ? "Create New Folder" : "Edit Folder";
        TxtName.Text = _folder.Name;

        PopulateGlyphs();
        PopulateColorPresets();
        (_preview.Style == FolderStyle.Glass ? RadioGlass : RadioSolid).IsChecked = true;
        (_preview.Size switch { FolderSize.Small => RadioSmall, FolderSize.Large => RadioLarge, _ => RadioMedium }).IsChecked = true;
        _ready = true;
        UpdatePreview();
    }

    /// <summary>The folder with the chosen settings. For a new folder this is a fresh object to create from.</summary>
    public FolderDef ResultFolder => _folder;

    private void PopulateGlyphs()
    {
        GlyphsPanel.Children.Clear();
        foreach (var (name, key) in Glyphs)
        {
            var btn = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)FindResource("GlassIconButton"),
                ToolTip = name
            };

            var iconView = new Viewbox { Width = 18, Height = 18 };
            var canvas = new Canvas { Width = 64, Height = 64 };
            canvas.Children.Add(new Path { Fill = (Brush)FindResource("BrushTextPrimary"), Data = (Geometry)FindResource(key) });
            iconView.Child = canvas;
            btn.Content = iconView;

            string glyphName = name;
            btn.Click += (_, _) =>
            {
                _preview.Glyph = glyphName;
                UpdatePreview();
            };

            GlyphsPanel.Children.Add(btn);
        }
    }

    private void PopulateColorPresets()
    {
        ColorPresetsPanel.Children.Clear();
        foreach (var color in FolderPalette.All)
        {
            var btn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = color.Name
            };

            btn.Content = new Border
            {
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.White,
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(color.Front),
                    (Color)ColorConverter.ConvertFromString(color.Back), 90)
            };
            btn.Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
            };

            btn.Click += (_, _) =>
            {
                _preview.ColorA = color.Front;
                _preview.ColorB = color.Back;
                UpdatePreview();
            };

            ColorPresetsPanel.Children.Add(btn);
        }
    }

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnStyleChecked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preview.Style = RadioGlass.IsChecked == true ? FolderStyle.Glass : FolderStyle.Solid;
        UpdatePreview();
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _preview.Size = RadioSmall.IsChecked == true ? FolderSize.Small : RadioLarge.IsChecked == true ? FolderSize.Large : FolderSize.Medium;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_ready) return;
        PreviewLabel.Text = string.IsNullOrWhiteSpace(TxtName.Text) ? "Folder" : TxtName.Text;

        // Drawn at the size the desktop will use.
        double icon = FolderTileMetrics.IconSizeFor(_preview.Size);
        PreviewArt.Width = icon;
        PreviewArt.Height = icon * FolderTileMetrics.ArtAspect;
        PreviewArt.Show(_preview);
    }

    private void OnBtnSaveClicked(object sender, RoutedEventArgs e)
    {
        _folder.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Folder" : TxtName.Text.Trim();
        _folder.ColorA = _preview.ColorA;
        _folder.ColorB = _preview.ColorB;
        _folder.Glyph = _preview.Glyph;
        _folder.Style = _preview.Style;
        _folder.Size = _preview.Size;

        DialogResult = true;
        Close();
    }

    private void OnBtnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
