using Coti.Client;
using Xunit;

public class CotiOutlinePerspectiveTests
{
    [Fact]
    public void PixelsPerMetreIsRowsOverTheFrustumHeightAtOneMetre()
    {
        // 90 degrees: the frustum is 2 m tall at 1 m, so 1152 rows hold 576 pixels per metre.
        Assert.Equal(576f, CotiOverlayScale.PixelsPerMetre(1152, 90f), precision: 3);
    }

    [Fact]
    public void ANarrowerFieldOfViewDrawsMorePixelsPerMetre()
    {
        // A 7x magnifier's 5 degree view against a 60 degree eye: the same thickness spans far more texels.
        Assert.True(CotiOverlayScale.PixelsPerMetre(1024, 5f) > 10f * CotiOverlayScale.PixelsPerMetre(1024, 60f));
    }

    [Theory]
    [InlineData(0, 60f)]
    [InlineData(1152, 0f)]
    [InlineData(1152, 180f)]
    public void NoProjectionWithoutRowsOrAUsableFieldOfView(int rows, float fov)
    {
        Assert.Equal(0f, CotiOverlayScale.PixelsPerMetre(rows, fov));
    }

    [Fact]
    public void AtOneTimesTheCapIsTheConfiguredWidthAsBefore()
    {
        CotiOverlayScale.OutlineRange( 1.5f, 993f, 1f, 1152f / 993f, out _, out var max );

        Assert.Equal( CotiOverlayScale.OutlineWidth( 1.5f, 1152 ), max, 3 );
    }

    [Fact]
    public void MagnificationRaisesTheCapByTheZoom()
    {
        CotiOverlayScale.OutlineRange( 1.5f, 993f, 1f, 2.5f, out _, out var unmagnified );
        CotiOverlayScale.OutlineRange( 1.5f, 993f, 4f, 2.5f, out _, out var magnified );

        Assert.Equal( 4f * unmagnified, magnified, 3 );
    }

    [Theory]
    [InlineData( 2.5f, 2.5f )]   // a 1024-row scope picture shown ~400 px tall: one screen pixel is 2.5 texels
    [InlineData( 0.5f, 1f )]     // a target smaller than the screen still needs a whole texel to erode
    public void TheThinnestLineIsOneScreenPixel( float texelsPerPixel, float expected )
    {
        CotiOverlayScale.OutlineRange( 1.5f, 993f, 1f, texelsPerPixel, out var min, out _ );

        Assert.Equal( expected, min, 3 );
    }

    [Fact]
    public void TheCapNeverFallsBelowTheFloor()
    {
        CotiOverlayScale.OutlineRange( 0.01f, 993f, 1f, 2.5f, out var min, out var max );

        Assert.True( max >= min );
    }

    [Fact]
    public void ADiagnosticWidthPassesThrough()
    {
        CotiOverlayScale.OutlineRange( 950f, 993f, 4f, 2.5f, out _, out var max );

        Assert.Equal( 950f, max );
    }

    [Theory]
    [InlineData( 1024, 400f, 2.56f )]
    [InlineData( 1152, 0f, 1f )]
    [InlineData( 0, 400f, 1f )]
    public void TexelsPerPixelIsTheTargetsRowsOverTheRowsItCoversOnScreen( int rows, float screenRows, float expected )
    {
        Assert.Equal( expected, CotiOverlayScale.TexelsPerPixel( rows, screenRows ), 3 );
    }
}
