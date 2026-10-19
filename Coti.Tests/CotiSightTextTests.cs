using Coti.Shared;
using Xunit;

// The riser's sight text: its size from the glass's size on screen alone, held still against small changes.
public class CotiSightTextTests
{
    [Fact]
    public void AWideGlassGetsItsShareOfTheHeight()
    {
        // The EXPS3's red dot window measured in raid at 1440p.
        Assert.Equal( 0.15f * 154f, CotiSightText.EmPixels( 217f, 154f ), 3 );
    }

    [Fact]
    public void ARoundGlassIsHeldToTheWidestMessageFittingItsWidth()
    {
        var em = CotiSightText.EmPixels( 433f, 433f );

        Assert.Equal( 433f * CotiSightText.MaxWidthShare / CotiSightText.WidestMessageEms, em, 3 );
        Assert.True( em < 0.15f * 433f );
        Assert.True( em * CotiSightText.WidestMessageEms <= 0.72f * 433f + 0.01f );
    }

    // Smaller than the old rule (an 86-texel line at 17% of the lens, the long messages fitted to 80% of its width), in
    // every glass and for every message.
    [Theory]
    [InlineData( 217f, 154f )]
    [InlineData( 433f, 433f )]
    [InlineData( 120f, 120f )]
    [InlineData( 640f, 600f )]
    public void TheTextIsSmallerThanBefore( float width, float height )
    {
        var longestBefore = System.Math.Min( height * 0.17f / 86f, width * 0.8f / 535f ) * 96f;
        Assert.True( CotiSightText.EmPixels( width, height ) < longestBefore );
    }

    [Fact]
    public void TheSizeScalesWithTheGlassWithNoMinimum()
    {
        Assert.Equal( CotiSightText.EmPixels( 400f, 300f ) / 50f, CotiSightText.EmPixels( 8f, 6f ), 5 );
        Assert.True( CotiSightText.EmPixels( 8f, 6f ) > 0f );
    }

    [Theory]
    [InlineData( 0f, 100f )]
    [InlineData( 100f, 0f )]
    [InlineData( float.NaN, 100f )]
    [InlineData( 100f, -5f )]
    public void NoGlassNoText( float width, float height )
    {
        Assert.Equal( 0f, CotiSightText.EmPixels( width, height ) );
    }

    // A variable scope zooming changes nothing the size reads from: the glass's box wobbles by a pixel or two at most, and
    // the held size stays put for the whole message.
    [Fact]
    public void ABoxWobblingDuringAZoomKeepsTheSize()
    {
        var held = CotiSightText.Hold( 0f, CotiSightText.EmPixels( 433f, 433f ) );

        foreach( var height in new[] { 431f, 432f, 434f, 436f, 430f, 433f } )
        {
            held = CotiSightText.Hold( held, CotiSightText.EmPixels( height, height ) );
            Assert.Equal( CotiSightText.EmPixels( 433f, 433f ), held, 4 );
        }

        Assert.Equal( CotiSightText.EmPixels( 433f, 433f ), held, 4 );
    }

    [Fact]
    public void AnAimInMovesTheSize()
    {
        var held = CotiSightText.Hold( 0f, CotiSightText.EmPixels( 120f, 120f ) );
        var target = CotiSightText.EmPixels( 433f, 433f );

        Assert.Equal( target, CotiSightText.Hold( held, target ) );
    }

    [Theory]
    [InlineData( 0f, 12f, 12f )]
    [InlineData( 20f, 20.6f, 20f )] // under a pixel
    [InlineData( 40f, 41.5f, 40f )] // under 4%
    [InlineData( 40f, 42f, 42f )] // past 4%
    [InlineData( 20f, 0f, 0f )] // the glass is gone
    public void HoldKeepsSmallChangesOut( float held, float target, float expected )
    {
        Assert.Equal( expected, CotiSightText.Hold( held, target ) );
    }

    [Theory]
    [InlineData( CotiDisplayMessage.Initializing, CotiThermalMode.Outline, "Initializing..." )]
    [InlineData( CotiDisplayMessage.PowerOff, CotiThermalMode.Full, "Power Off..." )]
    [InlineData( CotiDisplayMessage.Mode, CotiThermalMode.Outline, "Outline" )]
    [InlineData( CotiDisplayMessage.Mode, CotiThermalMode.Full, "Full" )]
    [InlineData( CotiDisplayMessage.Blank, CotiThermalMode.Full, null )]
    [InlineData( CotiDisplayMessage.None, CotiThermalMode.Full, null )]
    public void EachMessageHasItsWords( CotiDisplayMessage message, CotiThermalMode mode, string? words )
    {
        Assert.Equal( words, CotiSightText.Words( message, mode ) );
    }
}
