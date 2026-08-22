using System;
using Coti.Shared;
using Xunit;

public class CotiMountRoundingTests
{
    // The exact values a real publish produced, before rounding existed.
    private static CotiMountBlock Published() => new CotiMountBlock
    {
        AnchorBone = "axis_2",
        PositionX = 0.007000001f,
        PositionY = -0.04349999f,
        PositionZ = -0.052499957f,
        YawDegrees = 28f,
        Scale = 1.518f,
    };

    [Fact]
    public void TheNoiseARealPublishProducedIsGone()
    {
        var r = CotiMountRounding.Round(Published());

        Assert.Equal(0.007f, r.PositionX, 6);
        Assert.Equal(-0.0435f, r.PositionY, 6);
        Assert.Equal(-0.0525f, r.PositionZ, 6);
    }

    [Fact]
    public void APositionKeepsATenthOfAMillimetre()
    {
        // The requirement, not the constant: the editor's finest position step is 0.4 mm, so
        // rounding must not be able to discard a nudge the editor can make.
        var mount = new CotiMountBlock { PositionX = 0.0001f, Scale = 1f };

        Assert.Equal(0.0001f, CotiMountRounding.Round(mount).PositionX, 6);
    }

    [Fact]
    public void AnAngleKeepsAHundredthOfADegree()
    {
        var mount = new CotiMountBlock { PitchDegrees = -64.01f, Scale = 1f };

        Assert.Equal(-64.01f, CotiMountRounding.Round(mount).PitchDegrees, 4);
    }

    [Fact]
    public void RoundingIsIdempotent()
    {
        // Publish, publish again, and the file must not drift - PublishMask sends back a mount the
        // server already rounded, so a second pass has to be a no-op.
        var once = CotiMountRounding.Round(Published());
        var twice = CotiMountRounding.Round(once);

        Assert.Equal(once.PositionX, twice.PositionX, 6);
        Assert.Equal(once.PositionY, twice.PositionY, 6);
        Assert.Equal(once.PositionZ, twice.PositionZ, 6);
        Assert.Equal(once.Scale, twice.Scale, 6);
    }

    [Fact]
    public void TheAnchorBoneIsCarriedThroughUntouched()
    {
        Assert.Equal("axis_2", CotiMountRounding.Round(Published()).AnchorBone);
    }

    [Fact]
    public void TheBlockGivenInIsNotModified()
    {
        // The caller may be holding the live table's own mount: a publish must not rewrite what
        // the client is currently mounting from.
        var original = Published();
        var result = CotiMountRounding.Round(original);

        Assert.NotSame(original, result);
        Assert.Equal(0.007000001f, original.PositionX, 9);
    }

    [Fact]
    public void ANullMountIsRejectedRatherThanReturningAnEmptyPose()
    {
        // Silently returning a zeroed block would publish a device mounted at its host's origin,
        // which looks like a bad pose rather than a bug.
        Assert.Throws<ArgumentNullException>(() => CotiMountRounding.Round(null!));
    }
}
