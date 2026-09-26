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
        // Holding a button for a fixed duration moves the same total distance whatever the frame
        // rate. A step derived from a modulus of the total held time would be frame-rate
        // dependent: many samples inside one repeat interval would each return a full step, and a
        // coarse sample could miss an interval entirely. The accumulator makes both rates agree.
        const float step = 2f;
        const float totalHeld = CotiTunerStep.InitialDelaySeconds + 10f * CotiTunerStep.RepeatIntervalSeconds;
        const float expected = step * 11f; // one tap, then ten repeat intervals

        var fine = Simulate(totalHeld, 850, step);
        var coarse = Simulate(totalHeld, 85, step);

        // Tolerance covers one interval's worth of floating-point boundary rounding at the end of
        // the simulated hold.
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
