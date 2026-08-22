using Coti.Client;
using Xunit;

public class CotiImageConfigTests
{
    [Fact]
    public void DefaultsMatchTheValuesTheSixHostsAllShared()
    {
        // The values every shipped host ran with, pinned so a default cannot drift.
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

    // CompositeMode, OverlayContrast and OverlayExposure were all removed rather than migrated:
    // none of the three was ever branched on anywhere in the codebase (no shader property, no
    // ThermalVision field write) - each was a value that only ever changed a number in a log
    // line. See task-6-report.md.
}
