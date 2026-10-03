using Coti.Shared;
using Xunit;

// The power sequence is the only thing deciding whether the thermal renders, so every
// transition, reversal and skip is pinned. Timings in these tests are exact binary fractions
// (1.25, 0.25, 1.5) so a threshold comparison can never flip on float rounding.
public class CotiPowerSequenceTests
{
    private static CotiPowerTimings ExactTimings()
    {
        // No mode label, so the boot tests below time Initializing straight into the warm-up gap.
        return new CotiPowerTimings { InitializingSeconds = 1.25, WarmingSeconds = 0.25, PowerOffSeconds = 1.5, ModeSeconds = 0 };
    }

    private static CotiPowerSequence StartingOff()
    {
        return new CotiPowerSequence( ExactTimings(), startOn: false );
    }

    [Fact]
    public void DefaultTimingsMatchTheSpec()
    {
        var timings = new CotiPowerTimings();
        Assert.Equal( 1.2, timings.InitializingSeconds );
        Assert.Equal( 0.3, timings.WarmingSeconds );
        Assert.Equal( 1.5, timings.PowerOffSeconds );
    }

    [Fact]
    public void StartsOnWithNoClickAndNoBoost()
    {
        var frame = new CotiPowerSequence().Advance( 0 );

        Assert.Equal( CotiPowerPhase.On, frame.Phase );
        Assert.True( frame.ThermalOn );
        Assert.Equal( CotiDisplayMessage.None, frame.Message );
        Assert.False( frame.ClickNow );
        Assert.Equal( 1f, frame.GainBoost );
    }

    [Fact]
    public void StartsOffWhenAsked()
    {
        var frame = StartingOff().Advance( 0 );

        Assert.Equal( CotiPowerPhase.Off, frame.Phase );
        Assert.False( frame.ThermalOn );
        Assert.Equal( CotiDisplayMessage.None, frame.Message );
    }

    [Fact]
    public void PressWhileOnCutsTheThermalAndShowsPowerOffAtOnce()
    {
        var sequence = new CotiPowerSequence( ExactTimings() );
        sequence.Press( 10 );
        var frame = sequence.Advance( 10 );

        Assert.Equal( CotiPowerPhase.PoweringOff, frame.Phase );
        Assert.False( frame.ThermalOn );
        Assert.Equal( CotiDisplayMessage.PowerOff, frame.Message );
    }

    [Fact]
    public void PowerOffEndsAfterItsDuration()
    {
        var sequence = new CotiPowerSequence( ExactTimings() );
        sequence.Press( 10 );

        Assert.Equal( CotiPowerPhase.PoweringOff, sequence.Advance( 11.25 ).Phase );

        var frame = sequence.Advance( 11.5 );
        Assert.Equal( CotiPowerPhase.Off, frame.Phase );
        Assert.Equal( CotiDisplayMessage.None, frame.Message );
    }

    [Fact]
    public void BootShowsInitializingThenABlankDisplayThenTheThermalWithOneClick()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );

        var initializing = sequence.Advance( 0 );
        Assert.Equal( CotiPowerPhase.Initializing, initializing.Phase );
        Assert.Equal( CotiDisplayMessage.Initializing, initializing.Message );
        Assert.False( initializing.ThermalOn );

        Assert.Equal( CotiPowerPhase.Initializing, sequence.Advance( 1.0 ).Phase );

        var warming = sequence.Advance( 1.25 );
        Assert.Equal( CotiPowerPhase.Warming, warming.Phase );
        Assert.Equal( CotiDisplayMessage.Blank, warming.Message );
        Assert.False( warming.ThermalOn );
        Assert.False( warming.ClickNow );

        // The click leads the image by ClickLeadSeconds (0.15): 1.5 - 0.15 = 1.35.
        var click = sequence.Advance( 1.375 );
        Assert.Equal( CotiPowerPhase.Warming, click.Phase );
        Assert.True( click.ClickNow );
        Assert.False( click.ThermalOn );

        var on = sequence.Advance( 1.5 );
        Assert.Equal( CotiPowerPhase.On, on.Phase );
        Assert.True( on.ThermalOn );
        Assert.Equal( CotiDisplayMessage.None, on.Message );
        Assert.False( on.ClickNow );

        Assert.False( sequence.Advance( 1.6 ).ClickNow );
    }

    [Fact]
    public void ClickFiresOnceWhenOneFrameSkipsTheWholeBoot()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );

        var frame = sequence.Advance( 5 );
        Assert.Equal( CotiPowerPhase.On, frame.Phase );
        Assert.True( frame.ClickNow );

        Assert.False( sequence.Advance( 5.1 ).ClickNow );
        Assert.False( sequence.Advance( 9 ).ClickNow );
    }

    [Fact]
    public void PressDuringInitializingReversesIntoPowerOff()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );
        sequence.Advance( 0.5 );

        sequence.Press( 0.5 );
        Assert.Equal( CotiPowerPhase.PoweringOff, sequence.Advance( 0.5 ).Phase );
        Assert.Equal( CotiPowerPhase.Off, sequence.Advance( 2.0 ).Phase );
    }

    [Fact]
    public void PressDuringWarmingReversesWithoutEverClicking()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );
        Assert.Equal( CotiPowerPhase.Warming, sequence.Advance( 1.3 ).Phase );

        sequence.Press( 1.3 );
        var reversed = sequence.Advance( 1.3 );
        Assert.Equal( CotiPowerPhase.PoweringOff, reversed.Phase );
        Assert.False( reversed.ClickNow );

        var settled = sequence.Advance( 10 );
        Assert.Equal( CotiPowerPhase.Off, settled.Phase );
        Assert.False( settled.ClickNow );
    }

    [Fact]
    public void PressDuringPowerOffRestartsTheBoot()
    {
        var sequence = new CotiPowerSequence( ExactTimings() );
        sequence.Press( 0 );
        sequence.Advance( 0.5 );

        sequence.Press( 0.5 );
        Assert.Equal( CotiPowerPhase.Initializing, sequence.Advance( 0.5 ).Phase );

        var on = sequence.Advance( 2.0 );
        Assert.Equal( CotiPowerPhase.On, on.Phase );
        Assert.True( on.ClickNow );
    }

    [Fact]
    public void DisabledTogglesInstantlyWithNoMessageAndNoClick()
    {
        var sequence = new CotiPowerSequence( ExactTimings() ) { Enabled = false };

        sequence.Press( 0 );
        var off = sequence.Advance( 0 );
        Assert.Equal( CotiPowerPhase.Off, off.Phase );
        Assert.Equal( CotiDisplayMessage.None, off.Message );

        sequence.Press( 1 );
        var on = sequence.Advance( 1 );
        Assert.Equal( CotiPowerPhase.On, on.Phase );
        Assert.False( on.ClickNow );
        Assert.Equal( 1f, on.GainBoost );
    }

    [Fact]
    public void DisablingMidBootSnapsToOnWithoutAClick()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );
        sequence.Advance( 0.5 );

        sequence.Enabled = false;
        var frame = sequence.Advance( 0.6 );

        Assert.Equal( CotiPowerPhase.On, frame.Phase );
        Assert.False( frame.ClickNow );
        Assert.Equal( 1f, frame.GainBoost );
    }

    [Fact]
    public void DisablingMidPowerOffSnapsToOff()
    {
        var sequence = new CotiPowerSequence( ExactTimings() );
        sequence.Press( 0 );
        sequence.Advance( 0.5 );

        sequence.Enabled = false;
        Assert.Equal( CotiPowerPhase.Off, sequence.Advance( 0.6 ).Phase );
    }

    [Fact]
    public void TimingChangesApplyToThePhaseAlreadyRunning()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );
        sequence.Advance( 0.5 );

        // 0.375, not 0.25: at 0.25 the warm-up would also be over by 0.5 and the device On.
        sequence.Timings.InitializingSeconds = 0.375;
        Assert.Equal( CotiPowerPhase.Warming, sequence.Advance( 0.5 ).Phase );
    }

    [Fact]
    public void ZeroAndInvalidDurationsCountAsZero()
    {
        var timings = new CotiPowerTimings { InitializingSeconds = -1, ModeSeconds = double.NaN, WarmingSeconds = double.NaN, PowerOffSeconds = 0 };
        var sequence = new CotiPowerSequence( timings, startOn: false );

        sequence.Press( 0 );
        var on = sequence.Advance( 0 );
        Assert.Equal( CotiPowerPhase.On, on.Phase );
        Assert.True( on.ClickNow );

        sequence.Press( 1 );
        Assert.Equal( CotiPowerPhase.Off, sequence.Advance( 1 ).Phase );
    }

    [Fact]
    public void ThermalArrivesOverBrightAndSettlesOverSixTenthsOfASecond()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );

        Assert.Equal( 1.25f, sequence.Advance( 1.5 ).GainBoost, 4 );
        Assert.Equal( 1.0625f, sequence.Advance( 1.8 ).GainBoost, 4 );
        Assert.Equal( 1f, sequence.Advance( 2.1 ).GainBoost, 4 );
        Assert.Equal( 1f, sequence.Advance( 5 ).GainBoost, 4 );
    }

    [Fact]
    public void BoostIsOneOutsideTheSettle()
    {
        var sequence = StartingOff();
        Assert.Equal( 1f, sequence.Advance( 0 ).GainBoost );

        sequence.Press( 0 );
        Assert.Equal( 1f, sequence.Advance( 0.5 ).GainBoost );
        Assert.Equal( 1f, sequence.Advance( 1.3 ).GainBoost );
    }

    [Fact]
    public void SteadyFrameCarriesNoClickAndNoBoost()
    {
        var frame = CotiPowerFrame.Steady( CotiPowerPhase.On );

        Assert.True( frame.ThermalOn );
        Assert.False( frame.ClickNow );
        Assert.Equal( 1f, frame.GainBoost );
    }

    [Fact]
    public void ClickLeadsTheThermalBySetSeconds()
    {
        Assert.Equal( 0.15, CotiPowerSequence.ClickLeadSeconds );

        var sequence = StartingOff();
        sequence.Press( 0 );

        Assert.False( sequence.Advance( 1.3125 ).ClickNow );   // 1.5 - 0.1875: not yet

        var click = sequence.Advance( 1.375 );
        Assert.True( click.ClickNow );
        Assert.Equal( CotiPowerPhase.Warming, click.Phase );
    }

    [Fact]
    public void AGapShorterThanTheLeadClicksAsTheGapBegins()
    {
        var timings = new CotiPowerTimings { InitializingSeconds = 1.25, WarmingSeconds = 0.0625, PowerOffSeconds = 1.5, ModeSeconds = 0 };
        var sequence = new CotiPowerSequence( timings, startOn: false );
        sequence.Press( 0 );

        Assert.False( sequence.Advance( 1.0 ).ClickNow );

        var click = sequence.Advance( 1.25 );
        Assert.Equal( CotiPowerPhase.Warming, click.Phase );
        Assert.True( click.ClickNow );

        Assert.False( sequence.Advance( 1.3125 ).ClickNow );
    }

    [Fact]
    public void ReversingAfterTheClickDoesNotClickAgain()
    {
        var sequence = StartingOff();
        sequence.Press( 0 );
        Assert.True( sequence.Advance( 1.375 ).ClickNow );

        sequence.Press( 1.4 );
        Assert.False( sequence.Advance( 1.4 ).ClickNow );
        Assert.False( sequence.Advance( 5 ).ClickNow );
    }
}
