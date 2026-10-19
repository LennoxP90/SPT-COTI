using Coti.Shared;
using Xunit;

// The riser's LED, and the light state Fika carries, follow its power sequence: lit from the boot's first frame to the end
// of Power Off. Driven through the real sequence, with the light switched the way ThermalRiserState switches it.
public class CotiRiserLightTests
{
    private static CotiPowerSequence Sequence( bool startOn = false )
    {
        var timings = new CotiPowerTimings { InitializingSeconds = 1.25, ModeSeconds = 0.75, WarmingSeconds = 0.25, PowerOffSeconds = 1.5 };
        return new CotiPowerSequence( timings, startOn );
    }

    // One frame: advance the sequence, then bring the light to it, as ThermalRiserState.Tick does.
    private static bool Frame( CotiPowerSequence sequence, double now, bool light )
    {
        sequence.Advance( now );
        var request = CotiRiserLight.Request( sequence.Phase, light );
        return request == CotiLightRequest.None ? light : request == CotiLightRequest.On;
    }

    [Fact]
    public void ThePressLightsTheLedOnTheBootsFirstFrame()
    {
        var sequence = Sequence();
        sequence.Press( 10 );

        Assert.Equal( CotiPowerPhase.Initializing, sequence.Phase );
        Assert.True( Frame( sequence, 10, light: false ) );
    }

    [Fact]
    public void TheLedStaysLitThroughTheWholeShutdownAndGoesDarkWhenItEnds()
    {
        var sequence = Sequence( startOn: true );
        var light = true;

        sequence.Press( 20 );
        for( var t = 20.0; t < 21.5; t += 0.125 )
        {
            light = Frame( sequence, t, light );
            Assert.Equal( CotiPowerPhase.PoweringOff, sequence.Phase );
            Assert.True( light, $"dark at {t}" );
        }

        light = Frame( sequence, 21.5, light );
        Assert.Equal( CotiPowerPhase.Off, sequence.Phase );
        Assert.False( light );
    }

    [Fact]
    public void APressDuringTheShutdownBootsAgainAndTheLedNeverGoesDark()
    {
        var sequence = Sequence( startOn: true );
        var light = true;

        sequence.Press( 30 );
        light = Frame( sequence, 30.5, light );
        sequence.Press( 31 );
        Assert.Equal( CotiPowerPhase.Initializing, sequence.Phase );

        for( var t = 31.0; t < 34; t += 0.125 )
        {
            light = Frame( sequence, t, light );
            Assert.True( light, $"dark at {t}" );
        }

        Assert.Equal( CotiPowerPhase.On, sequence.Phase );
    }

    [Fact]
    public void APressDuringTheBootStartsTheShutdownWithTheLedStillLit()
    {
        var sequence = Sequence();
        var light = false;

        sequence.Press( 40 );
        light = Frame( sequence, 40.5, light );
        sequence.Press( 40.75 );

        Assert.Equal( CotiPowerPhase.PoweringOff, sequence.Phase );
        Assert.True( Frame( sequence, 41, light ) );
    }

    [Fact]
    public void WithTheSequenceOffTheLightFlipsAtThePress()
    {
        var sequence = Sequence();
        sequence.Enabled = false;

        sequence.Press( 50 );
        Assert.True( Frame( sequence, 50, light: false ) );
        sequence.Press( 51 );
        Assert.False( Frame( sequence, 51, light: true ) );
    }

    [Theory]
    [InlineData( CotiPowerPhase.Off, false )]
    [InlineData( CotiPowerPhase.Initializing, true )]
    [InlineData( CotiPowerPhase.ShowingMode, true )]
    [InlineData( CotiPowerPhase.Warming, true )]
    [InlineData( CotiPowerPhase.On, true )]
    [InlineData( CotiPowerPhase.PoweringOff, true )]
    public void LitInEveryPhaseButOff( CotiPowerPhase phase, bool lit )
    {
        Assert.Equal( lit, CotiRiserLight.Lit( phase ) );
        Assert.Equal( CotiLightRequest.None, CotiRiserLight.Request( phase, lit ) );
        Assert.Equal( lit ? CotiLightRequest.On : CotiLightRequest.Off, CotiRiserLight.Request( phase, !lit ) );
    }

    // Leaving the hands (holster, swap, drop) ends only a shutdown; a riser that is on stays on, as a flashlight does.
    [Theory]
    [InlineData( CotiPowerPhase.PoweringOff, true )]
    [InlineData( CotiPowerPhase.Off, false )]
    [InlineData( CotiPowerPhase.Initializing, false )]
    [InlineData( CotiPowerPhase.ShowingMode, false )]
    [InlineData( CotiPowerPhase.Warming, false )]
    [InlineData( CotiPowerPhase.On, false )]
    public void OnlyAShutdownEndsWhenTheRiserLeavesTheHands( CotiPowerPhase phase, bool ends )
    {
        Assert.Equal( ends, CotiRiserLight.EndsOnLeavingHands( phase ) );
    }
}
