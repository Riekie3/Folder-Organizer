namespace GlassFolders.Core.Models;

/// <summary>Which colours Settings, menus and dialogs use. The popup keeps the reference's blue glass in every mode.</summary>
public enum AppTheme
{
    /// <summary>Follows the Windows "Choose your mode" setting, and changes with it while the app runs.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Size of app icons in the folder popup.</summary>
public enum PopupIconSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// User settings for GlassFolders.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int PopupColumns { get; set; } = 4;
    public int PopupRows { get; set; } = 2;
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 20;
    public bool StartWithWindows { get; set; } = true;
    public int StartupDelaySeconds { get; set; } = 0;
    public string LogLevel { get; set; } = "Info";
    public bool CheckMissingTargets { get; set; } = true;

    /// <summary>Shows how many items each desktop folder holds. Off by default, as in the reference.</summary>
    public bool ShowItemCount { get; set; }

    // Appearance. The numbers are 0–100 sliders, and their defaults give the look tuned against the reference.
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>How see-through the glass is. 0 is nearly solid, 100 is mostly clear.</summary>
    public int GlassTransparency { get; set; } = GlassAppearance.DefaultTransparency;

    /// <summary>How soft the blurred wallpaper behind the glass is.</summary>
    public int GlassBlur { get; set; } = GlassAppearance.DefaultBlur;

    /// <summary>How strong the light edge around the glass is. 0 hides it.</summary>
    public int GlassBorder { get; set; } = GlassAppearance.DefaultBorder;

    /// <summary>How strong the shadow under the glass is. 0 hides it.</summary>
    public int GlassShadow { get; set; } = GlassAppearance.DefaultShadow;

    /// <summary>Corner radius of the folder popup, in device-independent pixels.</summary>
    public int CornerRadius { get; set; } = GlassAppearance.DefaultCornerRadius;

    public PopupIconSize PopupIconSize { get; set; } = PopupIconSize.Medium;
    public AnimationSettings Animation { get; set; } = new();
}
