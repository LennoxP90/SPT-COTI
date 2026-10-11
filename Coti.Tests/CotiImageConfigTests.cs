using Coti.Client;
using Xunit;

public class CotiImageConfigTests
{
    [Fact]
    public void DefaultsMatchTheValuesTheSixHostsAllShared()
    {
        // Pinned so a default cannot drift.
        var c = new CotiImageConfig();

        Assert.Equal(0.16f, c.HeatThreshold);
        Assert.Equal(1.0f, c.OutlineMix);
        Assert.Equal(1.5f, c.OutlineWidth);
        Assert.Equal(6.0f, c.OverlayIntensity);
    }

    [Fact]
    public void StartsInOutlineModeWithAFortyFivePercentFillForFull()
    {
        var c = new CotiImageConfig();

        Assert.Equal(Coti.Shared.CotiThermalMode.Outline, c.Mode);
        Assert.Equal(45f, c.FullFillPercent);
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
