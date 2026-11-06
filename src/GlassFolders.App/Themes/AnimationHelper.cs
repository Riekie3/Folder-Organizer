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
    private static readonly IEasingFunction DefaultEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void Fade(UIElement element, double to, TimeSpan baseDuration, AnimationCategory category, Action? onCompleted = null)
    {
        if (!AnimationService.Instance.ShouldAnimate(category))
        {
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
