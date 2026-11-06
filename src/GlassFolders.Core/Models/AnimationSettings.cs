namespace GlassFolders.Core.Models;

public enum AnimationCategory
{
    FolderOpenClose,
    TileHover,
    ItemPress,
    PageTransition,
    WindowTransition,
    DragGhost,
    Shimmer,
    SettingsNav
}

/// <summary>
/// Granular animation controls conforming to PLAN.md Phase 7 spec.
/// </summary>
public sealed class AnimationSettings
{
    public bool GlobalEnabled { get; set; } = true;
    public bool FollowWindows { get; set; } = true;
    public double SpeedMultiplier { get; set; } = 1.0;

    public bool FolderOpenClose { get; set; } = true;
    public bool TileHover { get; set; } = true;
    public bool ItemPress { get; set; } = true;
    public bool PageTransition { get; set; } = true;
    public bool WindowTransition { get; set; } = true;
    public bool DragGhost { get; set; } = true;
    public bool Shimmer { get; set; } = true;
    public bool SettingsNav { get; set; } = true;

    public bool IsCategoryEnabled(AnimationCategory category) => category switch
    {
        AnimationCategory.FolderOpenClose => FolderOpenClose,
        AnimationCategory.TileHover => TileHover,
        AnimationCategory.ItemPress => ItemPress,
        AnimationCategory.PageTransition => PageTransition,
        AnimationCategory.WindowTransition => WindowTransition,
        AnimationCategory.DragGhost => DragGhost,
        AnimationCategory.Shimmer => Shimmer,
        AnimationCategory.SettingsNav => SettingsNav,
        _ => true
    };
}
