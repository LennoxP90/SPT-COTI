using Coti.Client;
using Xunit;

public class CotiImageConfigTests
{
    [Fact]
    public void DefaultsMatchTheValuesTheSixHostsAllShared()
    {
        // Pinned so a default cannot drift.
        var c = new CotiImageConfig();

        Assert.Equal(0.25f, c.MinimumTemperatureValue);
        Assert.Equal(0.2f, c.MainTexColorCoef);
        Assert.Equal(0.03f, c.DepthFade);
        Assert.Equal(5.0f, c.UnsharpRadiusBlur);
        Assert.Equal(2.0f, c.UnsharpBias);
        Assert.Equal(0.0f, c.RampShift);
        Assert.Equal(0.16f, c.HeatThreshold);
        Assert.Equal(1.0f, c.OutlineMix);
        Assert.Equal(1.5f, c.OutlineWidth);
        Assert.Equal(6.0f, c.OverlayIntensity);
        Assert.False(c.IsPixelated);
        Assert.False(c.IsNoisy);
        Assert.False(c.IsMotionBlurred);
        Assert.Equal("", c.Palette);
    }

    [Fact]
    public void StartsInOutlineModeWithAFiftyFivePercentFillForFull()
    {
        var c = new CotiImageConfig();

        Assert.Equal(Coti.Shared.CotiThermalMode.Outline, c.Mode);
        Assert.Equal(55f, c.FullFillPercent);
    }

    [Fact]
    public void OutlineMixFollowsTheMode()
    {
        var c = new CotiImageConfig { Mode = Coti.Shared.CotiThermalMode.Full, FullFillPercent = 70f };

        Assert.Equal(0.3f, c.OutlineMix, 5);
    }

    [Fact]
    public void OutlineThicknessDefaultsToTwoCentimetres()
    {
        Assert.Equal(2.0f, new CotiImageConfig().OutlineThicknessCm);
    }
}
