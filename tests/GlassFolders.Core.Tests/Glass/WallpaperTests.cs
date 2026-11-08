using GlassFolders.Core.Glass;
using GlassFolders.Core.Layout;

namespace GlassFolders.Core.Tests.Glass;

public class WallpaperMappingTests
{
    private static readonly RectInt Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void FillCropsTheOverhangingSides()
    {
        // A 4:3 picture filling a 16:9 screen is scaled to 1920 x 1440, with 180 px cut off top and bottom.
        var region = WallpaperMapping.SourceRegion(WallpaperFit.Fill, 1600, 1200, Monitor, Monitor, new RectInt(0, 0, 192, 108))!.Value;

        Assert.Equal(0, region.X, 3);
        Assert.Equal(150, region.Y, 3);
        Assert.Equal(160, region.Width, 3);
        Assert.Equal(90, region.Height, 3);
        Assert.False(region.Partial);
    }

    [Fact]
    public void StretchMapsEachAxisOnItsOwn()
    {
        var region = WallpaperMapping.SourceRegion(WallpaperFit.Stretch, 960, 1080, Monitor, Monitor, new RectInt(960, 540, 480, 270))!.Value;
        Assert.Equal(new ImageRegion(480, 540, 240, 270), region);
    }

    [Fact]
    public void FitLeavesBarsOfBackgroundColour()
    {
        // A square picture fitted to 16:9 is 1080 wide, centred, leaving 420 px bars at each side.
        Assert.Null(WallpaperMapping.SourceRegion(WallpaperFit.Fit, 1000, 1000, Monitor, Monitor, new RectInt(0, 0, 400, 400)));

        var straddling = WallpaperMapping.SourceRegion(WallpaperFit.Fit, 1080, 1080, Monitor, Monitor, new RectInt(400, 0, 40, 40))!.Value;
        Assert.True(straddling.Partial);
        Assert.Equal(0, straddling.X, 3);
        Assert.Equal(20, straddling.Width, 3);
    }

    [Fact]
    public void CenterUsesThePictureAtItsOwnSize()
    {
        var region = WallpaperMapping.SourceRegion(WallpaperFit.Center, 3840, 2160, Monitor, Monitor, new RectInt(0, 0, 100, 100))!.Value;
        Assert.Equal(new ImageRegion(960, 540, 100, 100), region);
    }

    [Fact]
    public void TileRepeatsFromTheMonitorCorner()
    {
        var region = WallpaperMapping.SourceRegion(WallpaperFit.Tile, 256, 256, Monitor, Monitor, new RectInt(300, 600, 50, 50))!.Value;
        Assert.Equal(new ImageRegion(44, 88, 50, 50), region);
    }

    [Fact]
    public void SpanUsesTheWholeDesktopAcrossMonitors()
    {
        var left = new RectInt(-1920, 0, 1920, 1080);
        var all = new RectInt(-1920, 0, 3840, 1080);

        var region = WallpaperMapping.SourceRegion(WallpaperFit.Span, 3840, 1080, left, all, new RectInt(-1920, 0, 100, 100))!.Value;
        Assert.Equal(new ImageRegion(0, 0, 100, 100), region);
    }

    [Fact]
    public void ScaleFollowsADownsizedDecode()
    {
        var region = new ImageRegion(100, 50, 40, 20, Partial: true).Scale(0.25);
        Assert.Equal(new ImageRegion(25, 12.5, 10, 5, true), region);
    }
}

public class LabelContrastTests
{
    [Fact]
    public void LabelsOnTheWallpaperStayWhiteLikeWindows()
    {
        Assert.Equal(LabelInk.Light, LabelContrast.OnWallpaper(0.05).Ink);
        Assert.Equal(LabelInk.Light, LabelContrast.OnWallpaper(0.9).Ink);
    }

    [Fact]
    public void BrightWallpaperGetsAFirmerShadow()
    {
        var dark = LabelContrast.OnWallpaper(0.1);
        var bright = LabelContrast.OnWallpaper(0.7);
        Assert.True(bright.ShadowOpacity > dark.ShadowOpacity);
        Assert.True(bright.ShadowBlur < dark.ShadowBlur);
    }

    [Fact]
    public void ThePlateTurnsTheLabelDarkOnALightWallpaper()
    {
        // The reference: a light blue wallpaper, "Work" in dark ink on its frosted plate.
        Assert.Equal(LabelInk.Dark, LabelContrast.OnPlate(0.7).Ink);
        // A near-black wallpaper keeps the plate dim, so the label stays white.
        Assert.Equal(LabelInk.Light, LabelContrast.OnPlate(0.0).Ink);
    }

    [Fact]
    public void DarkInkIsChosenOnlyWhereItReadsBetter()
    {
        for (double l = 0; l <= 1.0001; l += 0.05)
        {
            double plate = LabelContrast.PlateLuminance(l);
            double white = 1.05 / (plate + 0.05);
            double dark = (plate + 0.05) / (0.016 + 0.05);
            if (LabelContrast.OnPlate(l).Ink == LabelInk.Dark) Assert.True(dark > white, $"at {l:0.00}");
        }
    }
}
