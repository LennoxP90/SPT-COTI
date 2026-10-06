using System.Linq;
using Coti.Shared;
using Xunit;

// The 1x thermal renders only the COTI's circle: a viewport box around it, a projection that maps that box onto the
// whole target, and a target sized to keep the full frame's texel density.
public class CotiSensorCropTests
{
    [Fact]
    public void TheBoxSurroundsTheCircleAndItsFeather()
    {
        // GPNVG: centre (0.525, 0.5), radius 0.285 and feather 0.02 of the height, on a 1904x993 screen.
        Assert.True( CotiSensorCrop.TryBox( 0.525f, 0.5f, 0.285f, 0.02f, 1904f / 993f, out var box ) );

        var halfHeight = ( 0.285f + 0.02f ) * CotiSensorCrop.Margin;
        Assert.Equal( 0.5f - halfHeight, box.Y, 4 );
        Assert.Equal( 2 * halfHeight, box.Height, 4 );
        Assert.Equal( 2 * halfHeight * 993f / 1904f, box.Width, 4 );
        Assert.Equal( 0.525f - box.Width / 2, box.X, 4 );
    }

    [Fact]
    public void TheBoxIsClampedToTheScreen()
    {
        Assert.True( CotiSensorCrop.TryBox( 0.5f, 0.5f, 0.6f, 0.0f, 1f, out var box ) );

        Assert.Equal( 0f, box.X, 4 );
        Assert.Equal( 0f, box.Y, 4 );
        Assert.Equal( 1f, box.Width, 4 );
        Assert.Equal( 1f, box.Height, 4 );
    }

    [Theory]
    [InlineData( 0f, 1.9f )]
    [InlineData( 0.3f, 0f )]
    [InlineData( float.NaN, 1.9f )]
    public void NoBoxWithoutACircleOrAScreen( float radius, float aspect )
    {
        Assert.False( CotiSensorCrop.TryBox( 0.5f, 0.5f, radius, 0f, aspect, out _ ) );
    }

    [Fact]
    public void TheProjectionMapsTheBoxOntoTheWholeTarget()
    {
        var box = new CotiCropBox( 0.2f, 0.1f, 0.4f, 0.5f );
        CotiSensorCrop.Projection( box, out var sx, out var sy, out var tx, out var ty );

        // Box corners in normalised device coordinates land on -1 and 1.
        Assert.Equal( -1f, sx * ( 2 * 0.2f - 1 ) + tx, 4 );
        Assert.Equal( 1f, sx * ( 2 * 0.6f - 1 ) + tx, 4 );
        Assert.Equal( -1f, sy * ( 2 * 0.1f - 1 ) + ty, 4 );
        Assert.Equal( 1f, sy * ( 2 * 0.6f - 1 ) + ty, 4 );
    }

    [Fact]
    public void TheWholeScreenIsTheIdentity()
    {
        CotiSensorCrop.Projection( CotiCropBox.Whole, out var sx, out var sy, out var tx, out var ty );

        Assert.Equal( 1f, sx, 5 );
        Assert.Equal( 1f, sy, 5 );
        Assert.Equal( 0f, tx, 5 );
        Assert.Equal( 0f, ty, 5 );
    }

    [Theory]
    [InlineData( 1536, 0.3f, 461 )]
    [InlineData( 1152, 0.64f, 738 )]
    [InlineData( 1152, 0.001f, 16 )]
    [InlineData( 1152, 1f, 1152 )]
    public void TheTargetKeepsTheFullFramesTexelDensity( int full, float fraction, int expected )
    {
        Assert.Equal( expected, CotiSensorCrop.Pixels( full, fraction ) );
    }

    [Fact]
    public void NoBoxesHaveNoUnion()
    {
        Assert.False( CotiSensorCrop.Union( new CotiCropBox[0], out _ ) );
    }

    [Fact]
    public void OneBoxIsItsOwnUnion()
    {
        Assert.True( CotiSensorCrop.Union( new[] { new CotiCropBox( 0.2f, 0.1f, 0.4f, 0.5f ) }, out var box ) );

        Assert.Equal( 0.2f, box.X, 4 );
        Assert.Equal( 0.1f, box.Y, 4 );
        Assert.Equal( 0.4f, box.Width, 4 );
        Assert.Equal( 0.5f, box.Height, 4 );
    }

    [Fact]
    public void TheUnionSpansEveryBox()
    {
        var a = new CotiCropBox( 0.1f, 0.3f, 0.2f, 0.2f );
        var b = new CotiCropBox( 0.5f, 0.2f, 0.3f, 0.1f );

        Assert.True( CotiSensorCrop.Union( new[] { a, b }, out var box ) );

        Assert.Equal( 0.1f, box.X, 4 );
        Assert.Equal( 0.2f, box.Y, 4 );
        Assert.Equal( 0.7f, box.Width, 4 );
        Assert.Equal( 0.3f, box.Height, 4 );
    }

    [Fact]
    public void TheUnionIsClampedToTheScreen()
    {
        var a = new CotiCropBox( -0.2f, 0.1f, 0.5f, 0.5f );
        var b = new CotiCropBox( 0.8f, 0.6f, 0.5f, 0.6f );

        Assert.True( CotiSensorCrop.Union( new[] { a, b }, out var box ) );

        Assert.Equal( 0f, box.X, 4 );
        Assert.Equal( 0.1f, box.Y, 4 );
        Assert.Equal( 1f, box.Width, 4 );
        Assert.Equal( 0.9f, box.Height, 4 );
    }

    // Spec section 1's cost figures at 16:9: the inner pair's box is about a fifth larger than one tube's.
    [Fact]
    public void TheQuadsInnerPairGrowsTheBoxByAFifth()
    {
        var left = QuadTubeBox( CotiTubes.Tube1 );
        var right = QuadTubeBox( CotiTubes.Tube2 );

        Assert.True( CotiSensorCrop.Union( new[] { left, right }, out var box ) );

        Assert.Equal( left.X, box.X, 4 );
        Assert.Equal( right.X + right.Width, box.X + box.Width, 4 );
        Assert.Equal( right.Y, box.Y, 4 );
        Assert.Equal( right.Height, box.Height, 4 );
        Assert.InRange( box.Width * box.Height / ( right.Width * right.Height ), 1.20f, 1.23f );
    }

    // Any outer tube makes the box reach the screen's edge: 40% of the screen with the home tube, 62% with all four.
    [Fact]
    public void AnOuterQuadTubeReachesTheScreensEdge()
    {
        Assert.True( CotiSensorCrop.Union(
            new[] { QuadTubeBox( CotiTubes.Tube2 ), QuadTubeBox( CotiTubes.Tube3 ) }, out var pair ) );
        Assert.Equal( 1f, pair.X + pair.Width, 4 );
        Assert.InRange( pair.Width * pair.Height, 0.39f, 0.40f );

        var all = new[] { CotiTubes.Tube0, CotiTubes.Tube1, CotiTubes.Tube2, CotiTubes.Tube3 }.Select( QuadTubeBox ).ToArray();
        Assert.True( CotiSensorCrop.Union( all, out var four ) );
        Assert.Equal( 0f, four.X, 4 );
        Assert.Equal( 1f, four.Width, 4 );
        Assert.InRange( four.Width * four.Height, 0.61f, 0.63f );
    }

    private static CotiCropBox QuadTubeBox( string label )
    {
        const float aspect = 16f / 9f;
        var circle = CotiCircles.FromLayout( CotiLayouts.Quad.Find( label )!, aspect );
        Assert.True( CotiSensorCrop.TryBox( circle.U, circle.V, circle.Radius, circle.Feather, aspect, out var box ) );
        return box;
    }
}
