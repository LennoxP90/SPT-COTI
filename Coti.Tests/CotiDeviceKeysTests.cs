using Coti.Shared;
using Xunit;

// Where a key's feedback shows: the mode name on the device the player looks through, the riser's text on one lens.
public class CotiDeviceKeysTests
{
    [Theory]
    // Aiming through the riser with it on: the riser's sight, whatever the goggles do.
    [InlineData( true, true, true, CotiDevice.Riser )]
    [InlineData( true, true, false, CotiDevice.Riser )]
    // Aiming through a riser that is off: the goggle image if it is lit.
    [InlineData( true, false, true, CotiDevice.Goggle )]
    [InlineData( true, false, false, CotiDevice.None )]
    // Not aiming through the riser with the goggle image lit: the goggle image.
    [InlineData( false, true, true, CotiDevice.Goggle )]
    [InlineData( false, false, true, CotiDevice.Goggle )]
    // Goggles off, riser on, not aimed through it: the riser's red dot window, at the hip.
    [InlineData( false, true, false, CotiDevice.Riser )]
    // Nothing on: the mode changes and no name shows.
    [InlineData( false, false, false, CotiDevice.None )]
    public void TheModeNameShowsWhereThePlayerLooks( bool aimedThroughRiser, bool riserOn, bool goggleOn, CotiDevice expected )
    {
        Assert.Equal( expected, CotiDeviceKeys.ModeNameOn( aimedThroughRiser, riserOn, goggleOn ) );
    }

    [Theory]
    [InlineData( true, CotiRiserTextLens.MagnifiedLens )]
    [InlineData( false, CotiRiserTextLens.RedDotWindow )]
    public void TheRiserTextGoesOnOneLens( bool magnifiedOnRiserAimed, CotiRiserTextLens expected )
    {
        Assert.Equal( expected, CotiDeviceKeys.RiserTextLens( magnifiedOnRiserAimed ) );
    }
}
