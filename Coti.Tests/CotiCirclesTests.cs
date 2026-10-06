using Coti.Shared;
using Xunit;

// Layout circles are stored in screen heights from the centre, so they map onto any screen shape and stay in Borkel's
// holes; a v1 mask block passes through as 3.2.0 drew it; the legacy mask is the home tube at 16:9.
public class CotiCirclesTests
{
    private static CotiLayoutTube Tube( string layout, string label )
    {
        Assert.True( CotiLayouts.TryGet( layout, out var found ) );
        return found.Find( label )!;
    }

    [Theory]
    [InlineData( "quad", "tube_0", 0.0782f, 0.4992f, 0.28506f )]
    [InlineData( "quad", "tube_1", 0.4629f, 0.4992f, 0.28506f )]
    [InlineData( "quad", "tube_2", 0.5371f, 0.4992f, 0.28506f )]
    [InlineData( "quad", "tube_3", 0.9218f, 0.4992f, 0.28506f )]
    [InlineData( "dual", "tube_1", 0.4647f, 0.4991f, 0.27362f )]
    [InlineData( "dual", "tube_2", 0.5353f, 0.4991f, 0.27362f )]
    [InlineData( "mono", "tube_center", 0.5006f, 0.4992f, 0.27359f )]
    [InlineData( "pvs5a", "tube_1", 0.4915f, 0.5076f, 0.24311f )]
    [InlineData( "pvs5a", "tube_2", 0.5021f, 0.5056f, 0.24311f )]
    public void At16By9EachCircleSitsWhereTheSpecMeasuredIt( string layout, string label, float u, float v, float radius )
    {
        var circle = CotiCircles.FromLayout( Tube( layout, label ), 16f / 9f );

        Assert.Equal( u, circle.U, 4 );
        Assert.Equal( v, circle.V, 4 );
        Assert.Equal( radius, circle.Radius, 5 );
        Assert.Equal( CotiLayouts.Feather, circle.Feather );
    }

    [Theory]
    [InlineData( 16f / 9f )]
    [InlineData( 16f / 10f )]
    [InlineData( 21f / 9f )]
    public void TheOffsetInScreenHeightsIsTheSameOnEveryScreen( float aspect )
    {
        foreach( var layout in CotiLayouts.All )
        {
            foreach( var tube in layout.Tubes )
            {
                var circle = CotiCircles.FromLayout( tube, aspect );

                Assert.Equal( tube.Dx, ( circle.U - 0.5f ) * aspect, 5 );
                Assert.Equal( tube.Dy, circle.V - 0.5f, 5 );
                Assert.Equal( tube.Radius, circle.Radius );
            }
        }
    }

    [Theory]
    [InlineData( 16f / 10f, 0.03135f, 0.96865f )]
    [InlineData( 21f / 9f, 0.17864f, 0.82136f )]
    public void TheOuterQuadTubesMoveWithTheScreenShape( float aspect, float leftU, float rightU )
    {
        Assert.Equal( leftU, CotiCircles.FromLayout( CotiLayouts.Quad.Find( CotiTubes.Tube0 )!, aspect ).U, 5 );
        Assert.Equal( rightU, CotiCircles.FromLayout( CotiLayouts.Quad.Find( CotiTubes.Tube3 )!, aspect ).U, 5 );
    }

    [Fact]
    public void AV1MaskBlockPassesStraightThrough()
    {
        var circle = CotiCircles.FromMask(
            new CotiMaskBlock { CenterX = 0.5361f, CenterY = 0.5f, Radius = 0.274f, Feather = 0.012f } );

        Assert.Equal( 0.5361f, circle.U );
        Assert.Equal( 0.5f, circle.V );
        Assert.Equal( 0.274f, circle.Radius );
        Assert.Equal( 0.012f, circle.Feather );
    }

    [Theory]
    [InlineData( "quad", 0.5371f, 0.4992f, 0.28506f )]
    [InlineData( "dual", 0.5353f, 0.4991f, 0.27362f )]
    [InlineData( "mono", 0.5006f, 0.4992f, 0.27359f )]
    [InlineData( "pvs5a", 0.5021f, 0.5056f, 0.24311f )]
    public void TheLegacyMaskIsTheHomeTubeAt16By9Rounded( string layout, float centerX, float centerY, float radius )
    {
        Assert.True( CotiLayouts.TryGet( layout, out var found ) );
        var mask = CotiCircles.LegacyMask( found.Find( found.Home )! );

        // Exact: device files carry these literals, and every release before 3.3.0 reads them.
        Assert.Equal( centerX, mask.CenterX );
        Assert.Equal( centerY, mask.CenterY );
        Assert.Equal( radius, mask.Radius );
        Assert.Equal( 0.01f, mask.Feather );
    }
}
