using Coti.Shared;
using Xunit;

public class CotiTunerStepTests
{
    [Fact]
    public void ATapGivesExactlyOneStep()
    {
        var accumulator = 0f;
        Assert.Equal(2f, CotiTunerStep.Step(0f, 0f, ref accumulator, 2f, false));
    }

    [Fact]
    public void HoldingInsideTheInitialDelayGivesNothingMore()
    {
        // Without a delay a tap becomes a slide, and every nudge overshoots.
        var accumulator = 0f;
        var held = CotiTunerStep.InitialDelaySeconds * 0.5f;
        Assert.Equal(0f, CotiTunerStep.Step(held, 0.01f, ref accumulator, 2f, false));
    }

    [Fact]
    public void PastTheDelayItRepeatsAtTheRepeatInterval()
    {
        var accumulator = 0f;
        var held = CotiTunerStep.InitialDelaySeconds + CotiTunerStep.RepeatIntervalSeconds;
        var result = CotiTunerStep.Step(held, CotiTunerStep.RepeatIntervalSeconds, ref accumulator, 2f, false);
        Assert.Equal(2f, result);
    }

    [Fact]
    public void FineDividesTheStepAndNotTheRate()
    {
        // Shift must make each step smaller, not the repeat slower - a slower repeat feels
        // like the tuner has stopped responding.
        var accumulator = 0f;
        var held = CotiTunerStep.InitialDelaySeconds + CotiTunerStep.RepeatIntervalSeconds;
        var result = CotiTunerStep.Step(held, CotiTunerStep.RepeatIntervalSeconds, ref accumulator, 2f, true);
        Assert.Equal(2f / CotiTunerStep.FineDivisor, result);
    }

    [Fact]
    public void ANegativeHeldTimeIsTreatedAsATap()
    {
        var accumulator = 0f;
        Assert.Equal(2f, CotiTunerStep.Step(-1f, 0f, ref accumulator, 2f, false));
    }

    [Fact]
    public void TotalDistanceOverAHoldDoesNotDependOnHowOftenStepIsSampled()
    {
        // The old shape sampled a modulus of the total held time on every call, which is
        // frame-rate dependent: sampled often enough (a high frame rate), many consecutive calls
        // land inside the "on" half of the same repeat interval and EACH ONE returns a full step,
        // so the total distance moved balloons with the sample rate instead of tracking wall-clock
        // time. Sampled coarsely (a low frame rate), a single call can land entirely inside the
        // "off" half and the repeat misses its window instead of catching up.
        //
        // This asserts the property that actually matters in raid: holding a button for a fixed
        // duration moves the same total distance whether the frame rate sampling it was high or
        // low. Simulated at two very different sample rates over the identical hold duration, the
        // old per-instant modulus would produce wildly different totals here; the accumulator
        // design produces the same one.
        const float step = 2f;
        const float totalHeld = CotiTunerStep.InitialDelaySeconds + 10f * CotiTunerStep.RepeatIntervalSeconds;
        const float expected = step * 11f; // one tap, then ten repeat intervals

        var fine = Simulate(totalHeld, 850, step);
        var coarse = Simulate(totalHeld, 85, step);

        // Tolerance covers a single interval's worth of floating-point boundary rounding at the
        // very end of the simulated hold - it stays far tighter than what the old per-instant
        // modulus would have produced here, which was a whole extra order of magnitude out.
        var tolerance = step * 1.5f;

        Assert.True(System.Math.Abs(fine - expected) < tolerance, $"fine sampling moved {fine}, expected close to {expected}");
        Assert.True(System.Math.Abs(coarse - expected) < tolerance, $"coarse sampling moved {coarse}, expected close to {expected}");
        Assert.True(System.Math.Abs(fine - coarse) < tolerance, $"fine sampling moved {fine} but coarse sampling moved {coarse} for the same hold");
    }

    /// <summary>
    /// Holds a control for totalHeld seconds, sampled in sampleCount equal steps (plus the initial
    /// tap), and returns the sum of every value Step returned - the total distance a caller adding
    /// each return value into a running position would have moved.
    /// </summary>
    private static float Simulate(float totalHeld, int sampleCount, float step)
    {
        var dt = totalHeld / sampleCount;
        var accumulator = 0f;
        var distance = 0f;

        for (var i = 0; i <= sampleCount; i++)
        {
            var held = i * dt;
            distance += CotiTunerStep.Step(held, dt, ref accumulator, step, false);
        }

        return distance;
    }
}
