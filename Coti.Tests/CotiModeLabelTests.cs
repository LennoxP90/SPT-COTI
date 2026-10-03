using Coti.Shared;
using Xunit;

// The mode label: the boot names the mode after Initializing..., and Alt+N names the new one. Exact
// binary-fraction timings, as in CotiPowerSequenceTests.
public class CotiModeLabelTests
{
    private static CotiPowerTimings Timings()
    {
        return new CotiPowerTimings { InitializingSeconds = 1.25, ModeSeconds = 0.5, WarmingSeconds = 0.25, PowerOffSeconds = 1.5 };
    }

    [Fact]
    public void ModeLabelDefaultsToThreeQuartersOfASecond()
    {
        Assert.Equal( 0.75, new CotiPowerTimings().ModeSeconds );
    }

    [Fact]
    public void BootNamesTheModeBetweenInitializingAndTheWarmUpGap()
    {
        var sequence = new CotiPowerSequence( Timings(), startOn: false );
        sequence.Press( 0 );

        Assert.Equal( CotiDisplayMessage.Initializing, sequence.Advance( 1.0 ).Message );

        var label = sequence.Advance( 1.25 );
        Assert.Equal( CotiPowerPhase.ShowingMode, label.Phase );
        Assert.Equal( CotiDisplayMessage.Mode, label.Message );
        Assert.False( label.ThermalOn );

        Assert.Equal( CotiPowerPhase.Warming, sequence.Advance( 1.75 ).Phase );
        Assert.True( sequence.Advance( 2.0 ).ThermalOn );
    }

    [Fact]
    public void BootClicksOnceWithTheLabelInTheWay()
    {
        var sequence = new CotiPowerSequence( Timings(), startOn: false );
        sequence.Press( 0 );

        var clicks = 0;
        for( var t = 0.0; t <= 3.0; t += 0.0625 )
            clicks += sequence.Advance( t ).ClickNow ? 1 : 0;

        Assert.Equal( 1, clicks );
    }

    [Fact]
    public void ZeroSecondsSkipsTheLabel()
    {
        var timings = Timings();
        timings.ModeSeconds = 0;
        var sequence = new CotiPowerSequence( timings, startOn: false );
        sequence.Press( 0 );

        Assert.Equal( CotiPowerPhase.Warming, sequence.Advance( 1.25 ).Phase );
    }

    [Fact]
    public void ShowModeWhileOnNamesTheModeThenReturnsWithoutAWarmUp()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );

        var label = sequence.Advance( 10 );
        Assert.Equal( CotiDisplayMessage.Mode, label.Message );
        Assert.False( label.ThermalOn );

        var back = sequence.Advance( 10.5 );
        Assert.Equal( CotiPowerPhase.On, back.Phase );
        Assert.Equal( 1f, back.GainBoost );
    }

    [Fact]
    public void ShowModeClicksOnceAsTheBootDoesJustBeforeTheThermalReturns()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );

        // The click leads the image by ClickLeadSeconds (0.15): 0.5 - 0.15 = 0.35.
        Assert.False( sequence.Advance( 10.25 ).ClickNow );
        var click = sequence.Advance( 10.375 );
        Assert.True( click.ClickNow );
        Assert.Equal( CotiPowerPhase.ShowingMode, click.Phase );
        Assert.False( sequence.Advance( 10.4375 ).ClickNow );
        Assert.False( sequence.Advance( 10.5 ).ClickNow );
    }

    [Fact]
    public void EachModeChangeClicks()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );
        Assert.True( sequence.Advance( 10.375 ).ClickNow );

        sequence.ShowMode( 10.4375 );
        Assert.False( sequence.Advance( 10.5 ).ClickNow );
        Assert.True( sequence.Advance( 10.8125 ).ClickNow );
    }

    [Fact]
    public void ModeChangeWithNoLabelStillClicks()
    {
        var timings = Timings();
        timings.ModeSeconds = 0;
        var sequence = new CotiPowerSequence( timings );
        sequence.ShowMode( 10 );

        var frame = sequence.Advance( 10 );
        Assert.True( frame.ClickNow );
        Assert.Equal( CotiPowerPhase.On, frame.Phase );
    }

    [Fact]
    public void PowerOffDuringTheLabelBeforeItsClickNeverClicks()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );
        sequence.Press( 10.25 );

        Assert.False( sequence.Advance( 10.375 ).ClickNow );
        Assert.False( sequence.Advance( 13 ).ClickNow );
    }

    [Fact]
    public void ShowModeAgainRestartsTheLabel()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );
        sequence.ShowMode( 10.375 );

        Assert.Equal( CotiPowerPhase.ShowingMode, sequence.Advance( 10.75 ).Phase );
        Assert.Equal( CotiPowerPhase.On, sequence.Advance( 10.875 ).Phase );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void ShowModeDoesNothingWhileOffOrPoweringOff( bool poweringOff )
    {
        var sequence = new CotiPowerSequence( Timings(), startOn: poweringOff );
        if( poweringOff )
            sequence.Press( 0 );

        sequence.ShowMode( 0.5 );

        Assert.Equal( poweringOff ? CotiPowerPhase.PoweringOff : CotiPowerPhase.Off, sequence.Advance( 0.5 ).Phase );
    }

    [Fact]
    public void ShowModeDuringTheBootLeavesTheBootAlone()
    {
        var sequence = new CotiPowerSequence( Timings(), startOn: false );
        sequence.Press( 0 );
        sequence.ShowMode( 0.5 );

        Assert.Equal( CotiPowerPhase.Initializing, sequence.Advance( 1.0 ).Phase );
        Assert.Equal( CotiPowerPhase.ShowingMode, sequence.Advance( 1.25 ).Phase );
    }

    [Fact]
    public void PowerPressDuringTheLabelPowersOff()
    {
        var sequence = new CotiPowerSequence( Timings() );
        sequence.ShowMode( 10 );
        sequence.Press( 10.25 );

        Assert.Equal( CotiPowerPhase.PoweringOff, sequence.Advance( 10.25 ).Phase );
    }

    [Fact]
    public void DisabledSequenceNeverShowsTheLabel()
    {
        var sequence = new CotiPowerSequence( Timings() ) { Enabled = false };
        sequence.ShowMode( 10 );

        Assert.Equal( CotiPowerPhase.On, sequence.Advance( 10 ).Phase );
    }

    [Theory]
    [InlineData( CotiThermalMode.Outline, CotiThermalMode.Full )]
    [InlineData( CotiThermalMode.Full, CotiThermalMode.Outline )]
    public void NextAlternatesTheTwoModes( CotiThermalMode from, CotiThermalMode to )
    {
        Assert.Equal( to, CotiThermalModes.Next( from ) );
    }

    [Theory]
    [InlineData( CotiThermalMode.Outline, 70f, 1f )]     // edges only, whatever the fill
    [InlineData( CotiThermalMode.Full, 70f, 0.3f )]      // full-brightness rim, interior at 70%
    [InlineData( CotiThermalMode.Full, 100f, 0f )]       // solid shapes
    [InlineData( CotiThermalMode.Full, 0f, 1f )]         // no fill left: the outline
    [InlineData( CotiThermalMode.Full, 150f, 0f )]       // clamped
    [InlineData( CotiThermalMode.Full, -5f, 1f )]
    public void OutlineMixPutsTheFillUnderAFullBrightnessRim( CotiThermalMode mode, float fillPercent, float mix )
    {
        Assert.Equal( mix, CotiThermalModes.OutlineMix( mode, fillPercent ), 5 );
    }
}
