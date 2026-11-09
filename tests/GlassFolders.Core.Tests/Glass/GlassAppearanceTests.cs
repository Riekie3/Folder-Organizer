using GlassFolders.Core.Glass;
using GlassFolders.Core.Models;

namespace GlassFolders.Core.Tests.Glass;

public class GlassAppearanceTests
{
    [Fact]
    public void DefaultsGiveTheLookTunedAgainstTheReference()
    {
        var a = GlassAppearance.Default;
        Assert.Equal(1.0, a.TintStrength, 6);
        Assert.Equal(FrostSettings.DefaultBlurRadius, a.BlurRadius);
        Assert.Equal(0.7, a.RimOpacity, 6);
        Assert.Equal(0.38, a.ShadowOpacity, 6);
        Assert.Equal(40, a.CornerRadius);
        Assert.Equal(96, a.IconSize);
        Assert.Equal(60, a.LowResolutionIconSize);
        // Settings and dialogs were 28 against the popup's 40 before this was a setting.
        Assert.Equal(28, a.ChromeCornerRadius);
    }

    [Fact]
    public void TransparencyScalesTheTintBothWaysAndNeverFlips()
    {
        double previous = double.MaxValue;
        for (int t = 0; t <= 100; t += 5)
        {
            double s = GlassAppearance.TintStrengthFor(t);
            Assert.True(s < previous, $"tint strength did not fall at {t}");
            Assert.True(s > 0);
            previous = s;
        }
        Assert.Equal(1.8, GlassAppearance.TintStrengthFor(0), 6);
        Assert.Equal(0.3, GlassAppearance.TintStrengthFor(100), 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 3)]
    [InlineData(100, 5)]
    [InlineData(-20, 1)]
    [InlineData(400, 5)]
    public void BlurRadiusSpansOneToFiveAndClamps(int slider, int radius) =>
        Assert.Equal(radius, GlassAppearance.BlurRadiusFor(slider));

    [Fact]
    public void ShadowAndBorderCanBeSwitchedOff()
    {
        var a = GlassAppearance.From(new AppSettings { GlassShadow = 0, GlassBorder = 0 });
        Assert.Equal(0, a.ShadowOpacity);
        Assert.Equal(0, a.RimOpacity);
    }

    [Fact]
    public void ShadowNeverGetsHarsherThanSeventyPercent()
    {
        Assert.True(GlassAppearance.From(new AppSettings { GlassShadow = 100 }).ShadowOpacity <= 0.7);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(30, 30)]
    [InlineData(999, 48)]
    public void CornerRadiusStaysInTheRangeThatLooksRight(int setting, double expected) =>
        Assert.Equal(expected, GlassAppearance.From(new AppSettings { CornerRadius = setting }).CornerRadius);

    [Fact]
    public void IconSizesGrowWithTheChoice()
    {
        double small = GlassAppearance.IconSizeFor(PopupIconSize.Small);
        double medium = GlassAppearance.IconSizeFor(PopupIconSize.Medium);
        double large = GlassAppearance.IconSizeFor(PopupIconSize.Large);
        Assert.True(small < medium && medium < large);

        // The popup cell: 10 above the icon, the icon, 9 above the label, two label lines (2 x 23.5) and 4 below.
        Assert.True(10 + large + 9 + 47 + 4 <= GlassFolders.Core.Popup.PopupMetrics.CellHeight);
        Assert.True(GlassAppearance.LowResolutionIconSizeFor(PopupIconSize.Small) < GlassAppearance.LowResolutionIconSizeFor(PopupIconSize.Large));
    }
}

public class LogLevelSettingTests
{
    [Theory]
    [InlineData("Error", "Error")]
    [InlineData("error", "Error")]
    [InlineData(" WARN ", "Warning")]
    [InlineData("warning", "Warning")]
    [InlineData("Info", "Info")]
    [InlineData("debug", "Debug")]
    [InlineData("Verbose", "Debug")]
    public void KnownNamesAreRecognisedInAnyCase(string saved, string expected) =>
        Assert.Equal(expected, LogLevelSetting.Normalize(saved));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("loud")]
    public void UnknownOrMissingNamesMeanInfoSoTheLogIsNeverSilenced(string? saved) =>
        Assert.Equal("Info", LogLevelSetting.Normalize(saved));

    [Fact]
    public void TheDefaultSettingIsInfo() => Assert.Equal("Info", LogLevelSetting.Normalize(new AppSettings().LogLevel));
}
