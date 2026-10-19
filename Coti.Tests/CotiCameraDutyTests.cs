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

    // One magnified view: a powered riser or ring under the optic, else nobody; the goggle COTI never draws in a scope.
    [Theory]
    [InlineData( true, CotiMagnifiedOwner.Riser )]
    [InlineData( false, CotiMagnifiedOwner.None )]
    public void OnlyAPoweredDeviceDrawsInTheScope( bool riserPowered, CotiMagnifiedOwner expected )
    {
        Assert.Equal( expected, CotiCameraDuty.MagnifiedOwner( riserPowered ) );
    }
}
