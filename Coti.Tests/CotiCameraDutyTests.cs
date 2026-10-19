using Coti.Shared;
using Xunit;

// Which thermal camera draws, and which device owns the one magnified view.
public class CotiCameraDutyTests
{
    // A thermal sight is its own picture: neither camera draws, whatever is on.
    [Theory]
    [InlineData( false, false )]
    [InlineData( false, true )]
    [InlineData( true, false )]
    [InlineData( true, true )]
    public void AThermalSightStopsBoth( bool goggles, bool magnifiedSight )
    {
        Assert.False( CotiCameraDuty.OneX( goggles, true, true, magnifiedSight ) );
        Assert.False( CotiCameraDuty.Magnified( true, true, magnifiedSight ) );
    }

    // The goggle image keeps drawing around a magnified scope (cut by its silhouette), so both cameras draw.
    [Fact]
    public void GogglesOnBehindAMagnifiedSightDrawBoth()
    {
        Assert.True( CotiCameraDuty.OneX( true, false, false, true ) );
        Assert.True( CotiCameraDuty.Magnified( true, false, true ) );
    }

    // The riser's red dot window is not in view behind a magnifier, so only the magnified camera draws for it.
    [Fact]
    public void ARiserAloneBehindAMagnifiedSightDrawsOnlyTheMagnifiedOne()
    {
        Assert.False( CotiCameraDuty.OneX( false, true, false, true ) );
        Assert.True( CotiCameraDuty.Magnified( true, false, true ) );
    }

    [Theory]
    [InlineData( true, false, true )]
    [InlineData( false, true, true )]
    [InlineData( true, true, true )]
    [InlineData( false, false, false )]
    public void AtOneXEitherDeviceRunsTheOneXCamera( bool goggles, bool riser, bool expected )
    {
        Assert.Equal( expected, CotiCameraDuty.OneX( goggles, riser, false, false ) );
        Assert.False( CotiCameraDuty.Magnified( goggles || riser, false, false ) );
    }

    // The path runs for Magnify With Optic, or for a riser with a sight on it whatever the goggles do.
    [Theory]
    [InlineData( true, false, true )]
    [InlineData( true, true, true )]
    [InlineData( false, true, true )]
    [InlineData( false, false, false )]
    public void MagnifyEnabledBySettingOrByARiser( bool setting, bool riser, bool expected )
    {
        Assert.Equal( expected, CotiCameraDuty.MagnifyEnabled( setting, riser ) );
    }

    // One magnified view: the riser first, the goggle COTI only with the setting, else nobody.
    [Theory]
    [InlineData( true, true, true, CotiMagnifiedOwner.Riser )]
    [InlineData( true, true, false, CotiMagnifiedOwner.Riser )]
    [InlineData( true, false, false, CotiMagnifiedOwner.Riser )]
    [InlineData( false, true, true, CotiMagnifiedOwner.Goggle )]
    [InlineData( false, true, false, CotiMagnifiedOwner.None )]
    [InlineData( false, false, true, CotiMagnifiedOwner.None )]
    [InlineData( false, false, false, CotiMagnifiedOwner.None )]
    public void TheRiserOwnsTheMagnifiedViewWhilePowered( bool riserPowered, bool goggles, bool setting, CotiMagnifiedOwner expected )
    {
        Assert.Equal( expected, CotiCameraDuty.MagnifiedOwner( riserPowered, goggles, setting ) );
    }

    // When the riser powers off (its message over), the goggle COTI takes the lens back on the same setting.
    [Fact]
    public void TheGoggleTakesOverWhenTheRiserGoesOff()
    {
        Assert.Equal( CotiMagnifiedOwner.Riser, CotiCameraDuty.MagnifiedOwner( true, true, true ) );
        Assert.Equal( CotiMagnifiedOwner.Goggle, CotiCameraDuty.MagnifiedOwner( false, true, true ) );
    }
}
