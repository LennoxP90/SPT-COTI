using Coti.Shared;
using Xunit;

// Each open circle gets its own copy of the message, in viewport units (x across, y up). Left tubes' copies grow
// inward from an outer edge 0.6 r left of the circle's centre, right tubes' from 0.6 r right of it, and centre tubes'
// (and every v1 device's) sit centred. A device file's text block moves a copy from there, and no copy comes closer
// than 0.02 screen heights to the screen's edge.
public class CotiDisplayLayoutTests
{
    private const float Aspect169 = 16f / 9f;

    // The cropped Power Off... image, the longest message, and Initializing..., the tallest.
    private const int LongestWidth = 535;
    private const int LongestHeight = 86;
    private const int TallestWidth = 530;
    private const int TallestHeight = 96;

    private static CotiCircle Circle( CotiLayout layout, string label, float aspect ) =>
        CotiCircles.FromLayout( layout.Find( label )!, aspect );

    private static CotiTextRect Rect( CotiLayout layout, string label, float aspect )
    {
        Assert.True( CotiDisplayLayout.TryRect( Circle( layout, label, aspect ), CotiDisplayLayout.Resolve( null, label ),
            aspect, LongestWidth, LongestHeight, out var rect ) );
        return rect;
    }

    private static CotiTextRect Rect( CotiLayout layout, string label, float aspect, CotiTextBlock block )
    {
        Assert.True( CotiDisplayLayout.TryRect( Circle( layout, label, aspect ), CotiDisplayLayout.Resolve( block, label ),
            aspect, LongestWidth, LongestHeight, out var rect ) );
        return rect;
    }

    [Fact]
    public void A631TexelImageSpansTodaysShareOfTheDiameter()
    {
        // 3.2.0 drew the whole 631 x 183 image across 0.31 of the circle's diameter; one texel keeps that size.
        var circle = new CotiCircle( 0.5f, 0.5f, 0.274f, 0.01f );
        Assert.True( CotiDisplayLayout.TryRect( circle, CotiDisplayLayout.Resolve( null, null ), Aspect169, 631, 183, out var rect ) );

        Assert.Equal( 0.31f, rect.Width * Aspect169 / ( 2f * 0.274f ), 4 );
        Assert.Equal( 631f / 183f, rect.Width * 1920f / ( rect.Height * 1080f ), 3 );
    }

    [Fact]
    public void ACentreCopySitsOnTheCircleCentre()
    {
        var circle = Circle( CotiLayouts.Mono, CotiTubes.Center, Aspect169 );
        var rect = Rect( CotiLayouts.Mono, CotiTubes.Center, Aspect169 );

        Assert.Equal( circle.U, rect.X + rect.Width / 2f, 4 );
        Assert.Equal( circle.V, rect.Y + rect.Height / 2f, 4 );
    }

    [Fact]
    public void AV1DeviceStaysCentred()
    {
        var circle = CotiCircles.FromMask( new CotiMaskBlock { CenterX = 0.5361f, CenterY = 0.5f, Radius = 0.274f, Feather = 0.01f } );
        Assert.True( CotiDisplayLayout.TryRect( circle, CotiDisplayLayout.Resolve( null, null ), Aspect169,
            LongestWidth, LongestHeight, out var rect ) );

        Assert.Equal( 0.5361f, rect.X + rect.Width / 2f, 4 );
        Assert.Equal( 0.5f, rect.Y + rect.Height / 2f, 4 );
    }

    [Theory]
    [InlineData( "quad" )]
    [InlineData( "dual" )]
    [InlineData( "pvs5a" )]
    public void InnerCopiesHangFromTheirOuterEdges( string layoutName )
    {
        Assert.True( CotiLayouts.TryGet( layoutName, out var layout ) );

        var left = Circle( layout, CotiTubes.Tube1, Aspect169 );
        var leftRect = Rect( layout, CotiTubes.Tube1, Aspect169 );
        Assert.Equal( left.U - 0.6f * left.Radius / Aspect169, leftRect.X, 4 );
        Assert.Equal( left.V, leftRect.Y + leftRect.Height / 2f, 4 );

        var right = Circle( layout, CotiTubes.Tube2, Aspect169 );
        var rightRect = Rect( layout, CotiTubes.Tube2, Aspect169 );
        Assert.Equal( right.U + 0.6f * right.Radius / Aspect169, rightRect.X + rightRect.Width, 4 );
        Assert.Equal( right.V, rightRect.Y + rightRect.Height / 2f, 4 );

        // The letters grow inward, so a lone COTI on the home tube shows its text right of the circle's centre.
        Assert.True( rightRect.X > right.U );
    }

    [Theory]
    [InlineData( 16f / 9f, 0.01125f, 0.98875f )]
    [InlineData( 16f / 10f, 0.0125f, 0.9875f )]
    public void OuterQuadCopiesArePulledInsideTheScreen( float aspect, float leftEdge, float rightEdge )
    {
        var outerLeft = Rect( CotiLayouts.Quad, CotiTubes.Tube0, aspect );
        var outerRight = Rect( CotiLayouts.Quad, CotiTubes.Tube3, aspect );

        // Not Assert.Equal at 4 places: 0.01125 sits on a rounding boundary and a float lands either side of it.
        Assert.InRange( outerLeft.X, leftEdge - 0.00005f, leftEdge + 0.00005f );
        Assert.InRange( outerRight.X + outerRight.Width, rightEdge - 0.00005f, rightEdge + 0.00005f );
    }

    [Fact]
    public void OuterQuadCopiesStayOnTheirEdgeOnAWideScreen()
    {
        const float aspect = 21f / 9f;
        var circle = Circle( CotiLayouts.Quad, CotiTubes.Tube3, aspect );
        var rect = Rect( CotiLayouts.Quad, CotiTubes.Tube3, aspect );

        Assert.Equal( circle.U + 0.6f * circle.Radius / aspect, rect.X + rect.Width, 4 );
        Assert.Equal( 0.89466f, rect.X + rect.Width, 4 );
    }

    // The longest message's inner copies at 1920 x 1080: about 190 px apart on a quad, 180 on a dual, 60 on a PVS-5A.
    [Theory]
    [InlineData( "quad", 188.4f )]
    [InlineData( "dual", 179.5f )]
    [InlineData( "pvs5a", 59.4f )]
    public void InnerCopiesKeepTheirGap( string layoutName, float gapPixels )
    {
        Assert.True( CotiLayouts.TryGet( layoutName, out var layout ) );
        var left = Rect( layout, CotiTubes.Tube1, Aspect169 );
        var right = Rect( layout, CotiTubes.Tube2, Aspect169 );

        var gap = ( right.X - ( left.X + left.Width ) ) * 1920f;
        Assert.InRange( gap, gapPixels - 0.5f, gapPixels + 0.5f );
    }

    [Theory]
    [InlineData( 16f / 9f )]
    [InlineData( 16f / 10f )]
    [InlineData( 21f / 9f )]
    public void EveryCopyStaysInsideItsCircle( float aspect )
    {
        foreach( var layout in CotiLayouts.All )
        {
            foreach( var tube in layout.Tubes )
            {
                foreach( var (width, height) in new[] { ( LongestWidth, LongestHeight ), ( TallestWidth, TallestHeight ) } )
                {
                    var circle = CotiCircles.FromLayout( tube, aspect );
                    Assert.True( CotiDisplayLayout.TryRect( circle, CotiDisplayLayout.Resolve( null, tube.Label ), aspect,
                        width, height, out var rect ) );

                    foreach( var x in new[] { rect.X, rect.X + rect.Width } )
                    {
                        foreach( var y in new[] { rect.Y, rect.Y + rect.Height } )
                        {
                            var dx = ( x - circle.U ) * aspect;
                            var dy = y - circle.V;
                            Assert.True( dx * dx + dy * dy < circle.Radius * circle.Radius,
                                $"{layout.Name} {tube.Label} {width}x{height} at aspect {aspect}" );
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData( 0f, Aspect169, 535, 86 )]
    [InlineData( -0.2f, Aspect169, 535, 86 )]
    [InlineData( float.NaN, Aspect169, 535, 86 )]
    [InlineData( 0.27f, 0f, 535, 86 )]
    [InlineData( 0.27f, Aspect169, 0, 86 )]
    [InlineData( 0.27f, Aspect169, 535, 0 )]
    public void RefusesGeometryThatCannotPlaceAnImage( float radius, float aspect, int width, int height )
    {
        Assert.False( CotiDisplayLayout.TryRect( new CotiCircle( 0.5f, 0.5f, radius, 0.01f ), CotiDisplayLayout.Resolve( null, null ),
            aspect, width, height, out _ ) );
    }

    [Theory]
    [InlineData( "tube_0", CotiTubeSide.Left, 0.6f )]
    [InlineData( "tube_1", CotiTubeSide.Left, 0.6f )]
    [InlineData( "tube_2", CotiTubeSide.Right, 0.6f )]
    [InlineData( "tube_3", CotiTubeSide.Right, 0.6f )]
    [InlineData( "tube_center", CotiTubeSide.Center, 0f )]
    [InlineData( null, CotiTubeSide.Center, 0f )]
    public void WithoutABlockEachTubeKeepsTheRule( string? label, CotiTubeSide align, float edge )
    {
        var placement = CotiDisplayLayout.Resolve( null, label );

        Assert.Equal( align, placement.Align );
        Assert.Equal( edge, placement.Edge );
        Assert.Equal( 0f, placement.Y );
        Assert.Equal( placement, CotiDisplayLayout.Resolve( new CotiTextBlock(), label ) );
    }

    [Theory]
    [InlineData( "tube_2", "center", null, 0f )]
    [InlineData( "tube_center", "left", null, 0.6f )]
    [InlineData( "tube_1", "right", 0.3f, 0.3f )]
    public void AMissingEdgeFollowsTheAlignInUse( string label, string align, float? edge, float want )
    {
        var placement = CotiDisplayLayout.Resolve( new CotiTextBlock { Align = align, Edge = edge }, label );

        Assert.True( CotiTextBlock.TryParseAlign( align, out var side ) );
        Assert.Equal( side, placement.Align );
        Assert.Equal( want, placement.Edge );
    }

    [Theory]
    [InlineData( 16f / 9f )]
    [InlineData( 21f / 9f )]
    public void ACustomEdgeAndYMoveTheAnchoredEdge( float aspect )
    {
        var left = Circle( CotiLayouts.Dual, CotiTubes.Tube1, aspect );
        var leftRect = Rect( CotiLayouts.Dual, CotiTubes.Tube1, aspect, new CotiTextBlock { Edge = 0.7f } );
        Assert.Equal( left.U - 0.7f * left.Radius / aspect, leftRect.X, 4 );
        Assert.Equal( left.V, leftRect.Y + leftRect.Height / 2f, 4 );

        var right = Circle( CotiLayouts.Dual, CotiTubes.Tube2, aspect );
        var rightRect = Rect( CotiLayouts.Dual, CotiTubes.Tube2, aspect, new CotiTextBlock { Edge = 0.4f, Y = 0.1f } );
        Assert.Equal( right.U + 0.4f * right.Radius / aspect, rightRect.X + rightRect.Width, 4 );
        Assert.Equal( right.V + 0.1f * right.Radius, rightRect.Y + rightRect.Height / 2f, 4 );

        // The default copy moved by exactly the change in edge and y.
        var defaultRight = Rect( CotiLayouts.Dual, CotiTubes.Tube2, aspect );
        Assert.Equal( -0.2f * right.Radius / aspect, rightRect.X - defaultRight.X, 4 );
        Assert.Equal( 0.1f * right.Radius, rightRect.Y - defaultRight.Y, 4 );
    }

    [Fact]
    public void ACentreCopyTakesAnOffset()
    {
        var circle = Circle( CotiLayouts.Mono, CotiTubes.Center, Aspect169 );
        var rect = Rect( CotiLayouts.Mono, CotiTubes.Center, Aspect169, new CotiTextBlock { Edge = 0.2f, Y = -0.15f } );

        Assert.Equal( circle.U + 0.2f * circle.Radius / Aspect169, rect.X + rect.Width / 2f, 4 );
        Assert.Equal( circle.V - 0.15f * circle.Radius, rect.Y + rect.Height / 2f, 4 );
    }

    [Fact]
    public void AnAlignOverrideAnchorsTheOtherSide()
    {
        var circle = Circle( CotiLayouts.Dual, CotiTubes.Tube2, Aspect169 );
        var rect = Rect( CotiLayouts.Dual, CotiTubes.Tube2, Aspect169, new CotiTextBlock { Align = "left" } );

        Assert.Equal( circle.U - 0.6f * circle.Radius / Aspect169, rect.X, 4 );
    }

    [Theory]
    [InlineData( 16f / 9f )]
    [InlineData( 21f / 9f )]
    public void NoBlockPushesACopyOffTheScreen( float aspect )
    {
        var margin = CotiDisplayLayout.ScreenMargin;

        var farLeft = Rect( CotiLayouts.Quad, CotiTubes.Tube0, aspect, new CotiTextBlock { Edge = 5f, Y = 5f } );
        Assert.Equal( margin / aspect, farLeft.X, 5 );
        Assert.Equal( 1f - margin, farLeft.Y + farLeft.Height, 5 );

        var farRight = Rect( CotiLayouts.Quad, CotiTubes.Tube3, aspect, new CotiTextBlock { Edge = 5f, Y = -5f } );
        Assert.Equal( 1f - margin / aspect, farRight.X + farRight.Width, 5 );
        Assert.Equal( margin, farRight.Y, 5 );

        var centre = Rect( CotiLayouts.Mono, CotiTubes.Center, aspect, new CotiTextBlock { Edge = -9f } );
        Assert.Equal( margin / aspect, centre.X, 5 );
    }

    [Theory]
    [InlineData( "tube_1", null, null, null )]
    [InlineData( "tube_1", "left", 0.6f, 0f )]
    [InlineData( "tube_center", "center", 0f, null )]
    public void ABlockThatRepeatsTheRuleTrimsToNothing( string label, string? align, float? edge, float? y )
    {
        Assert.Null( CotiDisplayLayout.Trim( new CotiTextBlock { Align = align, Edge = edge, Y = y }, label ) );
        Assert.Null( CotiDisplayLayout.Trim( null, label ) );
    }

    [Theory]
    [InlineData( "tube_1", "left", 0.7f, 0f, null, 0.7f, null )]
    [InlineData( "tube_1", "right", 0.6f, 0.1f, "right", null, 0.1f )]
    [InlineData( "tube_2", "center", 0f, null, "center", null, null )]
    [InlineData( "tube_center", null, 0.25f, -0.05f, null, 0.25f, -0.05f )]
    public void TrimmingKeepsOnlyWhatDiffersAndPlacesTheSame( string label, string? align, float? edge, float? y,
        string? wantAlign, float? wantEdge, float? wantY )
    {
        var block = new CotiTextBlock { Align = align, Edge = edge, Y = y };

        var trimmed = CotiDisplayLayout.Trim( block, label )!;

        Assert.Equal( wantAlign, trimmed.Align );
        Assert.Equal( wantEdge, trimmed.Edge );
        Assert.Equal( wantY, trimmed.Y );
        Assert.Equal( CotiDisplayLayout.Resolve( block, label ), CotiDisplayLayout.Resolve( trimmed, label ) );
    }
}
