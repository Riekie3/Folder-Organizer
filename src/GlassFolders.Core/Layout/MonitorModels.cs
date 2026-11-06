namespace GlassFolders.Core.Layout;

public readonly record struct RectInt(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(RectInt other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

public sealed class MonitorDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RectInt Bounds { get; set; }
    public RectInt WorkArea { get; set; }
    public bool IsPrimary { get; set; }
    public double DpiScale { get; set; } = 1.0;

    public override string ToString() => $"{Name} [{Key}] {(IsPrimary ? "(Primary) " : "")}{WorkArea}";
}

public sealed class FolderPlacement
{
    public string FolderId { get; set; } = string.Empty;
    public string HomeMonitorKey { get; set; } = string.Empty;
    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
    public int AbsoluteX { get; set; }
    public int AbsoluteY { get; set; }
    public bool IsDisplaced { get; set; }
}
