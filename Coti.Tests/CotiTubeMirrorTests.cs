using Coti.Shared;
using Xunit;

// A tube's pose from its partner's, across the goggles' centre line (spec section 6).
public class CotiTubeMirrorTests
{
    // vanilla_gpnvg's mount, with a base Y and Z added so their sign change shows.
    private static CotiMountBlock Gpnvg() => new CotiMountBlock
    {
        AnchorBone = "axis",
        PositionX = 0.027f, PositionY = -0.037f, PositionZ = -0.075f,
        RotationX = -90f, RotationY = 5f, RotationZ = 7f,
        RollDegrees = -26f, PitchDegrees = 90f, YawDegrees = 2f,
        Scale = 1.46f,
    };

    [Fact]
    public void SidewaysValuesChangeSignAndTheRestAreKept()
    {
        var mirrored = CotiTubeMirror.Mirror( Gpnvg(), null );

        Assert.Equal( -0.027f, mirrored.PositionX, 4 );
        Assert.Equal( -0.037f, mirrored.PositionY, 4 );
        Assert.Equal( -0.075f, mirrored.PositionZ, 4 );
        Assert.Equal( -90f, mirrored.RotationX, 4 );
        Assert.Equal( -5f, mirrored.RotationY, 4 );
        Assert.Equal( -7f, mirrored.RotationZ, 4 );
        Assert.Equal( 26f, mirrored.RollDegrees, 4 );
        Assert.Equal( 90f, mirrored.PitchDegrees, 4 );
        Assert.Equal( -2f, mirrored.YawDegrees, 4 );
        Assert.Equal( 1.46f, mirrored.Scale, 4 );
    }

    [Theory]
    [InlineData( null, "axis" )]
    [InlineData( "", "axis" )]
    // The Chimera's partners hang on different pods, so a tube keeps its own bone.
    [InlineData( "axis_3", "axis_3" )]
    public void TheTargetKeepsItsOwnAnchorWhenItHasOne( string? targetAnchor, string expected )
    {
        Assert.Equal( expected, CotiTubeMirror.Mirror( Gpnvg(), targetAnchor ).AnchorBone );
    }

    [Fact]
    public void MirroringTwiceGivesTheSourceBack()
    {
        var back = CotiTubeMirror.Mirror( CotiTubeMirror.Mirror( Gpnvg(), null ), null );

        Assert.Equal( 0.027f, back.PositionX, 4 );
        Assert.Equal( 5f, back.RotationY, 4 );
        Assert.Equal( 7f, back.RotationZ, 4 );
        Assert.Equal( -26f, back.RollDegrees, 4 );
        Assert.Equal( 2f, back.YawDegrees, 4 );
    }

    [Fact]
    public void TheSourceIsLeftAlone()
    {
        var source = Gpnvg();
        var mirrored = CotiTubeMirror.Mirror( source, "axis_3" );

        Assert.NotSame( source, mirrored );
        Assert.Equal( 0.027f, source.PositionX, 4 );
        Assert.Equal( "axis", source.AnchorBone );
    }
}
