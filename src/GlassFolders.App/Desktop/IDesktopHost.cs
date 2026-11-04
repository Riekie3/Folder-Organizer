using System.Windows;
using GlassFolders.Native;

namespace GlassFolders.App.Desktop;

/// <summary>
/// Puts folder surfaces on the Windows desktop layer. All undocumented Explorer-specific behaviour
/// (window classes, ownership, parenting, z-order tricks) lives behind this interface.
/// </summary>
public interface IDesktopHost
{
    string Name { get; }

    /// <summary>Current Explorer desktop handles, or null when the shell is unavailable.</summary>
    DesktopWindows? Desktop { get; }

    /// <summary>Re-reads Explorer's desktop handles. Returns false when the shell isn't ready.</summary>
    bool RefreshDesktop();

    /// <summary>Creates a surface hosting <paramref name="content"/> with its top-left at screen pixel (x, y).</summary>
    DesktopSurface CreateSurface(string id, UIElement content, int x, int y);

    /// <summary>Re-connects an existing surface to the (possibly new) desktop. Returns false if it must be recreated.</summary>
    bool Reattach(DesktopSurface surface);

    /// <summary>Moves a surface so its top-left is at screen pixel (x, y).</summary>
    void Move(DesktopSurface surface, int x, int y);

    /// <summary>True when the surface is still connected to the current desktop the way this host expects.</summary>
    bool IsAttached(DesktopSurface surface);
}

public enum DesktopHostKind { Owned, ChildDefView, ChildProgman }

public static class DesktopHostFactory
{
    public static IDesktopHost Create(DesktopHostKind kind) => kind switch
    {
        DesktopHostKind.Owned => new ProgmanOwnedHost(),
        DesktopHostKind.ChildDefView => new DesktopChildHost(useDefView: true),
        DesktopHostKind.ChildProgman => new DesktopChildHost(useDefView: false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static DesktopHostKind Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "child" or "child-defview" => DesktopHostKind.ChildDefView,
        "child-progman" => DesktopHostKind.ChildProgman,
        _ => DesktopHostKind.Owned,
    };
}
