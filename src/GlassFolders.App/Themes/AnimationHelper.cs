using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassFolders.App.Services;
using GlassFolders.Core.Models;

namespace GlassFolders.App.Themes;

/// <summary>
/// Storyboard and property animation helper. Ensures all UI animations route through
/// a single gatekeeper that immediately snaps to the final value when disabled or when
/// reduced motion is active. Guarantees no UX functionality is blocked by animation completion.
/// </summary>
public static class Animate
{
    // Roughly a desktop folder's width against the card's.
    private const double PopupStartScale = 0.14;

    private static readonly IEasingFunction DefaultEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void Fade(UIElement element, double to, TimeSpan baseDuration, AnimationCategory category, Action? onCompleted = null)
    {
        if (!AnimationService.Instance.ShouldAnimate(category))
        {
            // Release any fade still holding the value, or the new value wouldn't show.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            onCompleted?.Invoke();
            return;
        }

        var duration = AnimationService.Instance.GetDuration(baseDuration, category);
        var anim = new DoubleAnimation(to, duration)
        {
            EasingFunction = DefaultEase
        };

        if (onCompleted != null)
        {
            anim.Completed += (_, _) => onCompleted();
        }

        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void Scale(UIElement element, double to, TimeSpan baseDuration, AnimationCategory category, Action? onCompleted = null)
    {
        if (element.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1.0, 1.0);
            element.RenderTransform = st;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if (!AnimationService.Instance.ShouldAnimate(category))
        {
            st.ScaleX = to;
            st.ScaleY = to;
            onCompleted?.Invoke();
            return;
        }

        var duration = AnimationService.Instance.GetDuration(baseDuration, category);
        var anim = new DoubleAnimation(to, duration)
        {
            EasingFunction = DefaultEase
        };

        if (onCompleted != null)
        {
            anim.Completed += (_, _) => onCompleted();
        }

        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>Starts an element offset from where it now is and lets it glide into place.</summary>
    public static void Glide(UIElement element, Vector from, TimeSpan baseDuration, AnimationCategory category)
    {
        if (from.X == 0 && from.Y == 0) return;
        var move = new TranslateTransform();
        element.RenderTransform = move;
        if (!AnimationService.Instance.ShouldAnimate(category)) { element.RenderTransform = Transform.Identity; return; }

        var duration = AnimationService.Instance.GetDuration(baseDuration, category);
        var x = new DoubleAnimation(from.X, 0, duration) { EasingFunction = DefaultEase };
        var y = new DoubleAnimation(from.Y, 0, duration) { EasingFunction = DefaultEase };
        y.Completed += (_, _) => element.RenderTransform = Transform.Identity;
        move.BeginAnimation(TranslateTransform.XProperty, x);
        move.BeginAnimation(TranslateTransform.YProperty, y);
    }

    /// <summary>Slides an element's margin to a new value, or sets it at once when animations are off.</summary>
    public static void SlideMargin(FrameworkElement element, Thickness to, TimeSpan baseDuration, AnimationCategory category)
    {
        if (!AnimationService.Instance.ShouldAnimate(category))
        {
            element.BeginAnimation(FrameworkElement.MarginProperty, null);
            element.Margin = to;
            return;
        }
        var duration = AnimationService.Instance.GetDuration(baseDuration, category);
        element.BeginAnimation(FrameworkElement.MarginProperty,
            new ThicknessAnimation(to, duration) { EasingFunction = DefaultEase, FillBehavior = FillBehavior.HoldEnd });
    }

    /// <summary>Grows a popup card out of a point, given relative to the card. Text is left crisp afterwards.</summary>
    public static void PopupOpen(FrameworkElement card, Point origin, Action? onCompleted = null)
    {
        if (!AnimationService.Instance.ShouldAnimate(AnimationCategory.FolderOpenClose))
        {
            card.Opacity = 1;
            onCompleted?.Invoke();
            return;
        }

        var duration = AnimationService.Instance.GetDuration(TimeSpan.FromMilliseconds(280), AnimationCategory.FolderOpenClose);
        var scale = new ScaleTransform(PopupStartScale, PopupStartScale);
        card.RenderTransformOrigin = origin;
        card.RenderTransform = scale;

        var grow = new DoubleAnimation(PopupStartScale, 1, duration)
        {
            EasingFunction = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut }
        };
        grow.Completed += (_, _) =>
        {
            if (!ReferenceEquals(card.RenderTransform, scale)) return; // already closing
            card.RenderTransform = null;
            card.Opacity = 1;
            card.BeginAnimation(UIElement.OpacityProperty, null);
            onCompleted?.Invoke();
        };

        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromTicks(duration.Ticks * 2 / 5)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    /// <summary>The reverse of <see cref="PopupOpen"/>, and quicker, so closing never feels slow.</summary>
    public static void PopupClose(FrameworkElement card, Point origin, Action onCompleted)
    {
        if (!AnimationService.Instance.ShouldAnimate(AnimationCategory.FolderOpenClose))
        {
            onCompleted();
            return;
        }

        var duration = AnimationService.Instance.GetDuration(TimeSpan.FromMilliseconds(170), AnimationCategory.FolderOpenClose);
        var scale = new ScaleTransform(1, 1);
        card.RenderTransformOrigin = origin;
        card.RenderTransform = scale;

        var shrink = new DoubleAnimation(1, PopupStartScale, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        shrink.Completed += (_, _) => onCompleted();

        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromTicks(duration.Ticks * 2 / 3))
        {
            BeginTime = TimeSpan.FromTicks(duration.Ticks / 3),
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    /// <summary>Slides and fades new page content in from the side it is coming from.</summary>
    public static void PageIn(UIElement element, int direction)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.RenderTransform = null;
        if (direction == 0 || !AnimationService.Instance.ShouldAnimate(AnimationCategory.PageTransition)) return;

        var duration = AnimationService.Instance.GetDuration(TimeSpan.FromMilliseconds(200), AnimationCategory.PageTransition);
        var slide = new TranslateTransform();
        element.RenderTransform = slide;
        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = DefaultEase };
        fade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(element.RenderTransform, slide)) return; // another page has started
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.RenderTransform = null;
        };
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 36, 0, duration) { EasingFunction = DefaultEase });
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public static void Shimmer(UIElement element)
    {
        if (!AnimationService.Instance.ShouldAnimate(AnimationCategory.Shimmer)) return;

        // Subtle one-shot gloss highlight pulse (never loops)
        var anim = new DoubleAnimation(1.0, 0.8, TimeSpan.FromMilliseconds(400))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}
