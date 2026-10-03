using System.Linq;
using Coti.Shared;
using Xunit;

// The thermal cameras render at the sensor's rate, not the game's: 60 Hz by default, the real ECOTI's. Frame times
// below carry a little jitter, as real ones do.
public class CotiSensorPacerTests
{
    private static int Renders( int hz, double fps, double seconds, double jitter = 0.0002 )
    {
        var pacer = new CotiSensorPacer();
        var frames = (int)( fps * seconds );
        var count = 0;
        for( var i = 0; i < frames; i++ )
        {
            var t = 10.0 + i / fps + ( i % 2 == 0 ? jitter : -jitter );
            if( pacer.Due( t, hz ) )
                count++;
        }
        return count;
    }

    [Fact]
    public void ZeroHertzRendersEveryFrame()
    {
        Assert.Equal( 900, Renders( 0, 90, 10 ) );
    }

    [Fact]
    public void AtTheSensorsOwnRateEveryFrameRenders()
    {
        // A strict "a whole period has passed" test would skip half of these on jitter alone.
        Assert.Equal( 600, Renders( 60, 60, 10 ) );
    }

    [Theory]
    [InlineData( 90, 600 )]
    [InlineData( 120, 600 )]
    [InlineData( 144, 600 )]
    [InlineData( 240, 600 )]
    public void FasterFrameRatesRenderAboutTheSensorsRate( double fps, int expected )
    {
        var renders = Renders( 60, fps, 10 );
        Assert.InRange( renders, expected * 0.95, expected * 1.10 );
    }

    [Fact]
    public void SlowerThanTheSensorRendersEveryFrame()
    {
        Assert.Equal( 450, Renders( 60, 45, 10 ) );
    }

    [Fact]
    public void ALongPauseRendersOnceThenResumesTheRateWithoutABurst()
    {
        var pacer = new CotiSensorPacer();
        Assert.True( pacer.Due( 10.0, 60 ) );
        Assert.True( pacer.Due( 15.0, 60 ) );     // after a 5 s gap: render now

        var next = Enumerable.Range( 1, 6 ).Select( i => pacer.Due( 15.0 + i / 240.0, 60 ) ).ToArray();
        Assert.InRange( next.Count( due => due ), 1, 2 );   // 25 ms at 240 fps: one or two sensor frames, not a burst
    }

    [Fact]
    public void ResetRendersOnTheNextFrame()
    {
        var pacer = new CotiSensorPacer();
        Assert.True( pacer.Due( 10.0, 60 ) );
        Assert.False( pacer.Due( 10.001, 60 ) );

        pacer.Reset();
        Assert.True( pacer.Due( 10.002, 60 ) );
    }
}
