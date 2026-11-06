using System.Windows;
using GlassFolders.Core.Models;

namespace GlassFolders.App.Services;

/// <summary>
/// Central animation resolution service. Respects global toggle, per-category settings,
/// animation speed scaling, and Windows system reduced-motion preferences.
/// </summary>
public sealed class AnimationService
{
    private static readonly Lazy<AnimationService> _instance = new(() => new AnimationService());
    public static AnimationService Instance => _instance.Value;

    public AnimationSettings Settings { get; set; } = new();

    public bool ShouldAnimate(AnimationCategory category)
    {
        if (!Settings.GlobalEnabled) return false;

        // Respect Windows "Animation effects" setting (reduced motion)
        if (Settings.FollowWindows && !SystemParameters.ClientAreaAnimation)
            return false;

        return Settings.IsCategoryEnabled(category);
    }

    public TimeSpan GetDuration(TimeSpan baseDuration, AnimationCategory category)
    {
        if (!ShouldAnimate(category)) return TimeSpan.Zero;

        double mult = Math.Clamp(Settings.SpeedMultiplier, 0.25, 3.0);
        double ms = baseDuration.TotalMilliseconds / mult;
        return TimeSpan.FromMilliseconds(Math.Max(10, ms));
    }
}
