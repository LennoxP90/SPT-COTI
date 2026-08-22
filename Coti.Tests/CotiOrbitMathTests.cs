using Coti.Client;
using Xunit;

public class CotiOrbitMathTests
{
    [Fact]
    public void PitchClampsAtTheUpperLimit()
    {
        Assert.Equal(CotiOrbitMath.MaxPitchDegrees, CotiOrbitMath.ClampPitch(200f));
    }

    [Fact]
    public void PitchClampsAtTheLowerLimit()
    {
        Assert.Equal(CotiOrbitMath.MinPitchDegrees, CotiOrbitMath.ClampPitch(-200f));
    }

    [Fact]
    public void PitchWithinRangeIsUnchanged()
    {
        Assert.Equal(10f, CotiOrbitMath.ClampPitch(10f));
    }

    [Fact]
    public void DistanceClampsAtTheFarLimit()
    {
        Assert.Equal(CotiOrbitMath.MaxDistanceMetres, CotiOrbitMath.ClampDistance(999f));
    }

    [Fact]
    public void DistanceClampsAtTheNearLimit()
    {
        // This is the whole point of building a dedicated camera: closer than EFT's own inspect
        // window allows. A near limit that clamped away before that point would defeat it.
        Assert.Equal(CotiOrbitMath.MinDistanceMetres, CotiOrbitMath.ClampDistance(-5f));
    }

    [Fact]
    public void YawWrapsPastAFullTurn()
    {
        Assert.Equal(10f, CotiOrbitMath.WrapYaw(370f), 3);
    }

    [Fact]
    public void YawWrapsNegativeValuesIntoRange()
    {
        Assert.Equal(350f, CotiOrbitMath.WrapYaw(-10f), 3);
    }

    [Fact]
    public void YawAtExactlyZeroStaysZero()
    {
        Assert.Equal(0f, CotiOrbitMath.WrapYaw(0f), 3);
    }

    [Fact]
    public void DraggingRightTurnsYawUp()
    {
        CotiOrbitMath.ApplyDrag(0f, 0f, 10f, 0f, 1f, out var yaw, out var pitch);
        Assert.Equal(10f, yaw, 3);
        Assert.Equal(0f, pitch, 3);
    }

    [Fact]
    public void DraggingDownTurnsTheViewTheSameWayTheInspectWindowDoes()
    {
        // The requirement is a direction, not a formula: IMGUI reports a POSITIVE delta.y for a
        // downward drag, and the preview must turn the same way EFT's own inspect window turns an
        // item under the same drag. The previous version of this test asserted -10 purely because
        // the implementation subtracted, and it passed happily while the axis was inverted in game.
        CotiOrbitMath.ApplyDrag(0f, 0f, 0f, 10f, 1f, out var yaw, out var pitch);
        Assert.Equal(0f, yaw, 3);
        Assert.True(pitch > 0f, "a downward drag must raise pitch, matching the inspect window");
    }

    [Fact]
    public void ADragCannotPushPitchPastItsClamp()
    {
        // Positive delta.y now drives pitch upward, so the clamp is reached by dragging DOWN.
        CotiOrbitMath.ApplyDrag(0f, 80f, 0f, 100f, 1f, out _, out var pitch);
        Assert.Equal(CotiOrbitMath.MaxPitchDegrees, pitch);
    }

    [Fact]
    public void ADragCannotLeaveYawUnwrapped()
    {
        CotiOrbitMath.ApplyDrag(350f, 0f, 20f, 0f, 1f, out var yaw, out _);
        Assert.Equal(10f, yaw, 3);
    }

    [Fact]
    public void ScrollingInReducesDistance()
    {
        var distance = CotiOrbitMath.ApplyZoom(1f, 1f, 0.1f);
        Assert.Equal(0.9f, distance, 3);
    }

    [Fact]
    public void ScrollingOutIncreasesDistance()
    {
        var distance = CotiOrbitMath.ApplyZoom(1f, -1f, 0.1f);
        Assert.Equal(1.1f, distance, 3);
    }

    [Fact]
    public void ZoomCannotPushDistancePastEitherClamp()
    {
        var tooClose = CotiOrbitMath.ApplyZoom(CotiOrbitMath.MinDistanceMetres, 100f, 1f);
        Assert.Equal(CotiOrbitMath.MinDistanceMetres, tooClose);

        var tooFar = CotiOrbitMath.ApplyZoom(CotiOrbitMath.MaxDistanceMetres, -100f, 1f);
        Assert.Equal(CotiOrbitMath.MaxDistanceMetres, tooFar);
    }

    [Fact]
    public void FramingDistanceMatchesTheHalfAngleFormulaAt90Degrees()
    {
        // Derived from the requirement, not from the implementation. An object of size s must
        // occupy fraction f of the frame's height, so the frame must be s/f tall and its
        // half-height (s/2)/f. At a 90-degree vertical field of view tan(45 degrees) is exactly 1,
        // so the camera's distance equals that half-height.
        var size = 0.4f;
        var expected = size * 0.5f / CotiOrbitMath.FramingFillFraction;

        var distance = CotiOrbitMath.FramingDistance(size, 90f);

        Assert.Equal(expected, distance, 3);
    }

    [Fact]
    public void FramingLeavesAMarginRatherThanCroppingTheObject()
    {
        // The direction of the fill fraction, stated so a sign error cannot pass: framing to
        // 70 percent of the frame must sit FURTHER out than framing to 100 percent, which is what
        // an object exactly filling the frame would need. Multiplying by the fraction instead of
        // dividing inverts this and crops.
        var size = 0.4f;
        var exactlyFilling = size * 0.5f;

        var framed = CotiOrbitMath.FramingDistance(size, 90f);

        Assert.True(framed > exactlyFilling,
            $"framed ({framed}) should sit further out than exactly filling ({exactlyFilling})");
    }

    [Fact]
    public void ANarrowerFieldOfViewNeedsMoreDistanceToFrameTheSameObject()
    {
        var wide = CotiOrbitMath.FramingDistance(0.3f, 90f);
        var narrow = CotiOrbitMath.FramingDistance(0.3f, 30f);

        Assert.True(narrow > wide, $"narrow ({narrow}) should need more distance than wide ({wide})");
    }

    [Fact]
    public void FramingDistanceIsClampedLikeAManualZoomWouldBe()
    {
        // A huge object at a very narrow field of view would otherwise ask for a distance no
        // manual zoom control could ever reach.
        var distance = CotiOrbitMath.FramingDistance(50f, 1f);
        Assert.Equal(CotiOrbitMath.MaxDistanceMetres, distance);
    }

    [Fact]
    public void FramingDistanceFallsBackToTheNearLimitForAZeroSizedBounds()
    {
        Assert.Equal(CotiOrbitMath.MinDistanceMetres, CotiOrbitMath.FramingDistance(0f, 35f));
    }
}
