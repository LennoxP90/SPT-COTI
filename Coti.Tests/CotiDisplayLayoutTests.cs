using Coti.Shared;
using Xunit;

// The message is blitted with a scale and offset, so the test is where destination points land
// in the source image: the circle's centre must hit the image's centre, and the image must span
// TextWidthOfDiameter of the circle.
public class CotiDisplayLayoutTests
{
    private const float Aspect169 = 16f / 9f;

    private static float SourceU( float destU, float scale, float offset ) => destU * scale + offset;

    [Theory]
    [InlineData( 0.5f, 0.5f, 0.274f, 4f )]
    [InlineData( 0.62f, 0.41f, 0.2f, 5.3f )]
    [InlineData( 0.3f, 0.7f, 0.35f, 3f )]
    public void CircleCentreLandsOnTheImageCentre( float cx, float cy, float r, float imageAspect )
    {
        Assert.True( CotiDisplayLayout.TryBlitTransform( cx, cy, r, Aspect169, imageAspect,
            out var sx, out var sy, out var ox, out var oy ) );

        Assert.Equal( 0.5f, SourceU( cx, sx, ox ), 4 );
        Assert.Equal( 0.5f, SourceU( cy, sy, oy ), 4 );
    }

    [Fact]
    public void ImageSpansItsShareOfTheCircleDiameter()
    {
        const float r = 0.274f;
        Assert.True( CotiDisplayLayout.TryBlitTransform( 0.5f, 0.5f, r, Aspect169, 4f,
            out var sx, out _, out _, out _ ) );

        // 1 / scaleX is the image's width in destination UV; times the aspect converts it to
        // screen heights, the unit the radius is in.
        var widthInScreenHeights = ( 1f / sx ) * Aspect169;
        Assert.Equal( CotiDisplayLayout.TextWidthOfDiameter, widthInScreenHeights / ( 2f * r ), 4 );
    }

    [Fact]
    public void ImageKeepsItsOwnAspectOnScreen()
    {
        const float imageAspect = 4f;
        Assert.True( CotiDisplayLayout.TryBlitTransform( 0.5f, 0.5f, 0.274f, Aspect169, imageAspect,
            out var sx, out var sy, out _, out _ ) );

        var widthPixels = ( 1f / sx ) * 1920f;
        var heightPixels = ( 1f / sy ) * 1080f;
        Assert.Equal( imageAspect, widthPixels / heightPixels, 3 );
    }

    [Theory]
    [InlineData( 0f, Aspect169, 4f )]
    [InlineData( -0.2f, Aspect169, 4f )]
    [InlineData( 0.27f, 0f, 4f )]
    [InlineData( 0.27f, Aspect169, 0f )]
    [InlineData( float.NaN, Aspect169, 4f )]
    public void RefusesGeometryThatCannotPlaceAnImage( float r, float screenAspect, float imageAspect )
    {
        Assert.False( CotiDisplayLayout.TryBlitTransform( 0.5f, 0.5f, r, screenAspect, imageAspect,
            out _, out _, out _, out _ ) );
    }
}
