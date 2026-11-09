using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassFolders.App.UI;

public enum GlassMessageKind { Info, Success, Warning, Error, Question }

public enum GlassMessageButtons { Ok, YesNo, YesNoCancel }

public enum GlassMessageResult { None, Ok, Yes, No, Cancel }

/// <summary>
/// A message box on glass, in place of the grey Windows one. Same idea as <see cref="MessageBox"/>, with labels you can
/// choose ("Replace" and "Merge" instead of "Yes" and "No") and a red main button for actions that can't be undone.
/// </summary>
public partial class GlassMessageBox : GlassWindow
{
    private GlassMessageResult _result = GlassMessageResult.None;
    private readonly GlassMessageButtons _buttons;
    private readonly GlassMessageKind _kind;

    private GlassMessageBox(string title, string message, GlassMessageButtons buttons, GlassMessageKind kind,
        string? yesLabel, string? noLabel, bool dangerYes)
    {
        InitializeComponent();
        UseGlass(Card);
        _buttons = buttons;
        _kind = kind;

        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

        // "Ok" is the Yes button with another label, so there is one main button in every layout.
        BtnYes.Content = yesLabel ?? (buttons == GlassMessageButtons.Ok ? "OK" : "Yes");
        BtnNo.Content = noLabel ?? "No";
        BtnNo.Visibility = buttons == GlassMessageButtons.Ok ? Visibility.Collapsed : Visibility.Visible;
        BtnCancel.Visibility = buttons == GlassMessageButtons.YesNoCancel ? Visibility.Visible : Visibility.Collapsed;
        if (dangerYes) BtnYes.Style = (Style)FindResource("DangerButton");

        BtnYes.IsDefault = !dangerYes;
        // Escape answers the way "close the box" would: the safest answer on screen.
        BtnCancel.IsCancel = buttons == GlassMessageButtons.YesNoCancel;
        BtnNo.IsCancel = buttons == GlassMessageButtons.YesNo;
        BtnYes.IsCancel = buttons == GlassMessageButtons.Ok;

        ApplyKind();
        Loaded += (_, _) => (dangerYes ? BtnNo : BtnYes).Focus();
    }

    protected override void OnAppearanceApplied() => ApplyKind();

    private void ApplyKind()
    {
        (string icon, string ink) = _kind switch
        {
            GlassMessageKind.Success => ("IconCheck", "ChromeOk"),
            GlassMessageKind.Warning => ("IconWarning", "ChromeWarn"),
            GlassMessageKind.Error => ("IconError", "ChromeDanger"),
            GlassMessageKind.Question => ("IconQuestion", "ChromeLink"),
            _ => ("IconAbout", "ChromeLink"),
        };
        KindIcon.Data = (Geometry)FindResource(icon);
        KindIcon.Stroke = (Brush)FindResource(ink);
    }

    private void OnYesClicked(object sender, RoutedEventArgs e) => Finish(_buttons == GlassMessageButtons.Ok ? GlassMessageResult.Ok : GlassMessageResult.Yes);
    private void OnNoClicked(object sender, RoutedEventArgs e) => Finish(GlassMessageResult.No);
    private void OnCancelClicked(object sender, RoutedEventArgs e) => Finish(GlassMessageResult.Cancel);

    private void Finish(GlassMessageResult result)
    {
        _result = result;
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.OriginalSource is not (System.Windows.Controls.Button or System.Windows.Controls.TextBlock))
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Shows the box and waits. Closing it with the window's own close key counts as the safest answer.</summary>
    public static GlassMessageResult Show(Window? owner, string title, string message,
        GlassMessageButtons buttons = GlassMessageButtons.Ok, GlassMessageKind kind = GlassMessageKind.Info,
        string? yesLabel = null, string? noLabel = null, bool dangerYes = false)
    {
        var box = new GlassMessageBox(title, message, buttons, kind, yesLabel, noLabel, dangerYes);
        if (owner is { IsLoaded: true, IsVisible: true }) box.Owner = owner;
        box.ShowDialog();

        if (box._result != GlassMessageResult.None) return box._result;
        return buttons switch
        {
            GlassMessageButtons.Ok => GlassMessageResult.Ok,
            GlassMessageButtons.YesNo => GlassMessageResult.No,
            _ => GlassMessageResult.Cancel,
        };
    }
}
