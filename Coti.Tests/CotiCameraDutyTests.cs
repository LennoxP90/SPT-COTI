using Coti.Shared;
using Xunit;

// At most one thermal camera draws a frame, and while heat shows without a thermal sight, exactly one does.
public class CotiCameraDutyTests
{
    [Theory]
    [InlineData( false, false )]
    [InlineData( false, true )]
    [InlineData( true, false )]
    [InlineData( true, true )]
    public void TheTwoCamerasNeverBothDraw( bool thermalSight, bool magnifiedSight )
    {
        Assert.False( CotiCameraDuty.OneX( true, thermalSight, magnifiedSight )
                      && CotiCameraDuty.Magnified( true, thermalSight, magnifiedSight ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void WithoutAThermalSightExactlyOneDraws( bool magnifiedSight )
    {
        Assert.NotEqual( CotiCameraDuty.OneX( true, false, magnifiedSight ),
                         CotiCameraDuty.Magnified( true, false, magnifiedSight ) );
    }

    [Fact]
    public void BehindAMagnifiedSightOnlyTheMagnifiedCameraDraws()
    {
        Assert.False( CotiCameraDuty.OneX( true, false, true ) );
        Assert.True( CotiCameraDuty.Magnified( true, false, true ) );
    }

    [Theory]
    [InlineData( false, false, false )]
    [InlineData( false, false, true )]
    [InlineData( true, true, false )]
    [InlineData( true, true, true )]
    public void NeitherDrawsWhenOffOrThroughAThermalSight( bool active, bool thermalSight, bool magnifiedSight )
    {
        Assert.False( CotiCameraDuty.OneX( active, thermalSight, magnifiedSight ) );
        Assert.False( CotiCameraDuty.Magnified( active, thermalSight, magnifiedSight ) );
    }
}
