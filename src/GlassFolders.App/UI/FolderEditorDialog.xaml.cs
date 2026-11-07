using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GlassFolders.Core.Models;

namespace GlassFolders.App.UI;

/// <summary>
/// Dialog for creating or modifying a desktop folder's name, color scheme, and category glyph.
/// Features a real-time live preview of the vector folder tile.
/// </summary>
public partial class FolderEditorDialog : Window
{
    private static readonly (string Name, string ColorA, string ColorB)[] Presets =
    [
        ("Blue", "#7FB2FF", "#5B5CF2"),
        ("Purple", "#C38BFF", "#7B3FE8"),
        ("Red", "#FF9A9A", "#E8455A"),
        ("Green", "#6EE7B7", "#059669"),
        ("Amber", "#FCD34D", "#D97706"),
        ("Teal", "#67E8F9", "#0891B2"),
        ("Pink", "#F472B6", "#DB2777"),
    ];

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
    private string _selectedColorA;
    private string _selectedColorB;
    private string _selectedGlyph;

    public FolderEditorDialog(FolderDef? existing = null)
    {
        InitializeComponent();
        _folder = existing ?? new FolderDef { Name = "New Folder" };

        TxtHeader.Text = existing is null ? "Create New Folder" : "Edit Folder";
        TxtName.Text = _folder.Name;

        _selectedColorA = _folder.ColorA;
        _selectedColorB = _folder.ColorB;
        _selectedGlyph = _folder.Glyph;

        PopulateGlyphs();
        PopulateColorPresets();
        UpdatePreview();
    }

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
            var path = new Path
            {
                Fill = (Brush)FindResource("BrushTextPrimary"),
                Data = (Geometry)FindResource(key)
            };
            canvas.Children.Add(path);
            iconView.Child = canvas;
            btn.Content = iconView;

            string glyphName = name;
            btn.Click += (_, _) =>
            {
                _selectedGlyph = glyphName;
                UpdatePreview();
            };

            GlyphsPanel.Children.Add(btn);
        }
    }

    private void PopulateColorPresets()
    {
        ColorPresetsPanel.Children.Clear();
        foreach (var (name, a, b) in Presets)
        {
            var btn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name
            };

            Color colA = (Color)ColorConverter.ConvertFromString(a);
            Color colB = (Color)ColorConverter.ConvertFromString(b);

            var border = new Border
            {
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.White,
                Background = new LinearGradientBrush(colA, colB, 45)
            };
            btn.Content = border;
            btn.Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
            };

            string cA = a, cB = b;
            btn.Click += (_, _) =>
            {
                _selectedColorA = cA;
                _selectedColorB = cB;
                UpdatePreview();
            };

            ColorPresetsPanel.Children.Add(btn);
        }
    }

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        PreviewLabel.Text = string.IsNullOrWhiteSpace(TxtName.Text) ? "Folder" : TxtName.Text;

        try
        {
            PreviewStopA.Color = (Color)ColorConverter.ConvertFromString(_selectedColorA);
            PreviewStopB.Color = (Color)ColorConverter.ConvertFromString(_selectedColorB);
        }
        catch { }

        try
        {
            string glyphKey = "Glyph" + _selectedGlyph;
            if (TryFindResource(glyphKey) is Geometry geom)
                PreviewGlyph.Data = geom;
        }
        catch { }
    }

    private void OnBtnSaveClicked(object sender, RoutedEventArgs e)
    {
        _folder.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Folder" : TxtName.Text.Trim();
        _folder.ColorA = _selectedColorA;
        _folder.ColorB = _selectedColorB;
        _folder.Glyph = _selectedGlyph;

        DialogResult = true;
        Close();
    }

    private void OnBtnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
