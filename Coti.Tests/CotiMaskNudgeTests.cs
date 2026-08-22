using Coti.Client;
using Coti.Shared;
using Xunit;

public class CotiMaskNudgeTests
{
    private static CotiMaskBlock Gpnvg() => new CotiMaskBlock
    {
        CenterX = 0.525f,
        CenterY = 0.5f,
        Radius = 0.285f,
        Feather = 0.01f,
    };

    [Fact]
    public void NudgingUpRaisesTheValueAndNudgingDownLowersIt()
    {
        var up = CotiMaskNudge.Nudge(Gpnvg(), CotiMaskAxis.Radius, 1, fine: false);
        var down = CotiMaskNudge.Nudge(Gpnvg(), CotiMaskAxis.Radius, -1, fine: false);

        Assert.True(up.Radius > Gpnvg().Radius);
        Assert.True(down.Radius < Gpnvg().Radius);
    }

    [Fact]
    public void AFineNudgeMovesLessFarThanACoarseOne()
    {
        // A property, not the step arithmetic: whatever the divisor is, holding the fine
        // modifier must travel a shorter distance in the same direction.
        var coarse = CotiMaskNudge.Nudge(Gpnvg(), CotiMaskAxis.CenterX, 1, fine: false);
        var fine = CotiMaskNudge.Nudge(Gpnvg(), CotiMaskAxis.CenterX, 1, fine: true);

        Assert.True(fine.CenterX > Gpnvg().CenterX);
        Assert.True(fine.CenterX < coarse.CenterX);
    }

    [Fact]
    public void RadiusCanNeverBeNudgedToZeroOrBelow()
    {
        // The requirement, not the clamp expression: CotiDeviceMerge rejects radius <= 0, so a
        // published file with one would make the device vanish on the server's next load. No
        // amount of holding the key down may reach it.
        var mask = Gpnvg();
        for (var i = 0; i < 500; i++)
            mask = CotiMaskNudge.Nudge(mask, CotiMaskAxis.Radius, -1, fine: false);

        Assert.True(mask.Radius > 0f);
    }

    [Fact]
    public void FeatherCanReachExactlyZeroBecauseAHardEdgeIsLegitimate()
    {
        // MaskGeometry.ComputeCoverage treats feather <= 0 as a hard cut, which is a real
        // choice a device may want - so unlike radius, zero is a valid destination.
        var mask = Gpnvg();
        for (var i = 0; i < 500; i++)
            mask = CotiMaskNudge.Nudge(mask, CotiMaskAxis.Feather, -1, fine: false);

        Assert.Equal(0f, mask.Feather);
    }

    [Fact]
    public void TheCentreStaysOnScreenInBothDirections()
    {
        var low = Gpnvg();
        var high = Gpnvg();
        for (var i = 0; i < 500; i++)
        {
            low = CotiMaskNudge.Nudge(low, CotiMaskAxis.CenterX, -1, fine: false);
            high = CotiMaskNudge.Nudge(high, CotiMaskAxis.CenterY, 1, fine: false);
        }

        Assert.InRange(low.CenterX, 0f, 1f);
        Assert.InRange(high.CenterY, 0f, 1f);
    }

    [Theory]
    [InlineData(CotiMaskAxis.CenterX)]
    [InlineData(CotiMaskAxis.CenterY)]
    [InlineData(CotiMaskAxis.Radius)]
    [InlineData(CotiMaskAxis.Feather)]
    public void NudgingOneAxisLeavesTheOtherThreeAlone(CotiMaskAxis axis)
    {
        var before = Gpnvg();
        var after = CotiMaskNudge.Nudge(before, axis, 1, fine: false);

        var changed = 0;
        if (after.CenterX != before.CenterX) changed++;
        if (after.CenterY != before.CenterY) changed++;
        if (after.Radius != before.Radius) changed++;
        if (after.Feather != before.Feather) changed++;

        Assert.Equal(1, changed);
    }

    [Fact]
    public void NudgeReturnsANewBlockRatherThanMutatingTheOneGivenToIt()
    {
        // The editor nudges from the device's saved mask, so mutating in place would rewrite the state
        // the on-screen delta is measured against.
        var original = Gpnvg();
        var result = CotiMaskNudge.Nudge(original, CotiMaskAxis.Radius, 1, fine: false);

        Assert.NotSame(original, result);
        Assert.Equal(0.285f, original.Radius);
    }
}
