using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GlassFolders.App.UI;

/// <summary>A one-line text prompt on glass. Used to rename folders and items.</summary>
public partial class GlassPromptDialog : GlassWindow
{
    private GlassPromptDialog(string title, string? label, string initial)
    {
        InitializeComponent();
        UseGlass(Card);
        Title = title;
        TxtTitle.Text = title;
        if (!string.IsNullOrWhiteSpace(label))
        {
            TxtLabel.Text = label;
            TxtLabel.Visibility = Visibility.Visible;
        }

        TxtValue.Text = initial;
        TxtValue.TextChanged += (_, _) => BtnOk.IsEnabled = !string.IsNullOrWhiteSpace(TxtValue.Text);
        BtnOk.IsEnabled = !string.IsNullOrWhiteSpace(initial);
        Loaded += (_, _) =>
        {
            TxtValue.Focus();
            TxtValue.SelectAll();
        };
    }

    private void OnOkClicked(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.OriginalSource is not (Button or TextBox or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Documents.Run))
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Returns the text the user entered, or null if they cancelled.</summary>
    public static string? Show(Window? owner, string title, string initial, string? label = null)
    {
        var dialog = new GlassPromptDialog(title, label, initial);
        if (owner is { IsLoaded: true, IsVisible: true }) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.TxtValue.Text.Trim() : null;
    }
}
