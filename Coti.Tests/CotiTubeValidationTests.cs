using Coti.Shared;
using Xunit;

// Spec section 3's rules for layout and tubes: anything wrong falls back towards today's one COTI, a bad tube is dropped
// without taking its slot, and the legacy mask and mount are never altered.
public class CotiTubeValidationTests
{
    private static CotiTube Tube( string bone, float x ) => new CotiTube
    {
        Mount = new CotiMountBlock { AnchorBone = bone, PositionX = x, Scale = 1.518f },
        Pod = new CotiPodBlock { DownX = -33f },
    };

    private static Dictionary<string, CotiTube> FourTubes() => new Dictionary<string, CotiTube>
    {
        [CotiTubes.Tube0] = Tube( "axis_3", -0.06f ),
        [CotiTubes.Tube1] = Tube( "axis_3", -0.007f ),
        [CotiTubes.Tube2] = Tube( "axis_2", 0.007f ),
        [CotiTubes.Tube3] = Tube( "axis_2", 0.06f ),
    };

    private static CotiDeviceFile Device( string? layout, Dictionary<string, CotiTube>? tubes ) => new CotiDeviceFile
    {
        Schema = 1, Device = "com.c11.truenorth4_argus_chimera", DisplayName = "Argus Chimera", Tuned = true,
        Mask = new CotiMaskBlock { CenterX = 0.5371f, CenterY = 0.4992f, Radius = 0.28506f, Feather = 0.01f },
        Mount = new CotiMountBlock { AnchorBone = "axis_2", PositionX = 0.007f, Scale = 1.518f },
        Layout = layout,
        Tubes = tubes,
    };

    private static List<string> Normalise( CotiDeviceFile device )
    {
        var warnings = new List<string>();
        CotiTubeValidation.Normalise( device, "chimera.json", warnings );
        return warnings;
    }

    [Fact]
    public void AV1FileIsLeftAloneWithoutAWarning()
    {
        var device = Device( null, null );

        Assert.Empty( Normalise( device ) );
        Assert.False( device.IsMultiTube );
    }

    [Fact]
    public void AValidQuadKeepsEveryTubeAndPod()
    {
        var device = Device( "quad", FourTubes() );

        Assert.Empty( Normalise( device ) );
        Assert.True( device.IsMultiTube );
        Assert.Equal( 4, device.Tubes!.Count );
        Assert.All( device.Tubes.Values, t => Assert.NotNull( t.Pod ) );
    }

    [Theory]
    [InlineData( "quad", false, "no tubes" )]
    [InlineData( null, true, "no layout" )]
    [InlineData( "hex", true, "\"hex\"" )]
    [InlineData( "Quad", true, "\"Quad\"" )]
    public void AnIncompletePairFallsBackToV1WithAWarning( string? layout, bool withTubes, string reason )
    {
        var device = Device( layout, withTubes ? FourTubes() : null );

        var warning = Assert.Single( Normalise( device ) );

        Assert.StartsWith( "chimera.json: ", warning );
        Assert.Contains( reason, warning );
        Assert.Null( device.Layout );
        Assert.Null( device.Tubes );
        Assert.False( device.IsMultiTube );
    }

    [Theory]
    [InlineData( "tube_center" )]
    [InlineData( "tube_4" )]
    public void ALabelOutsideTheLayoutIsDroppedAndTheRestKept( string label )
    {
        var tubes = FourTubes();
        tubes[label] = Tube( "axis", 0f );
        var device = Device( "quad", tubes );

        var warning = Assert.Single( Normalise( device ) );

        Assert.Contains( label, warning );
        Assert.False( device.Tubes!.ContainsKey( label ) );
        Assert.Equal( 4, device.Tubes.Count );
    }

    [Theory]
    [InlineData( float.NaN )]
    [InlineData( float.PositiveInfinity )]
    public void ATubeWithANonFiniteMountValueIsDroppedButKeepsItsSlot( float bad )
    {
        var tubes = FourTubes();
        tubes[CotiTubes.Tube0].Mount.RotationY = bad;
        var device = Device( "quad", tubes );

        var warning = Assert.Single( Normalise( device ) );

        Assert.Contains( "tube_0", warning );
        Assert.False( device.Tubes!.ContainsKey( CotiTubes.Tube0 ) );

        // Slots come from the layout, so a COTI already in mod_coti_0 keeps its slot.
        Assert.True( device.IsMultiTube );
        Assert.True( CotiLayouts.TryGet( device.Layout, out var layout ) );
        Assert.Equal( 4, CotiTubes.SlotNames( layout ).Count );
    }

    [Fact]
    public void ATubeWithNoMountIsDropped()
    {
        var tubes = FourTubes();
        tubes[CotiTubes.Tube1] = new CotiTube { Mount = null! };
        tubes[CotiTubes.Tube3] = null!;
        var device = Device( "quad", tubes );

        var warnings = Normalise( device );

        Assert.Equal( 2, warnings.Count );
        Assert.Equal( new[] { CotiTubes.Tube0, CotiTubes.Tube2 }, device.Tubes!.Keys.OrderBy( k => k ).ToArray() );
    }

    [Fact]
    public void APodWithANonFiniteDownValueIsDroppedAndTheTubeKept()
    {
        var tubes = FourTubes();
        tubes[CotiTubes.Tube2].Pod!.DownZ = float.NaN;
        var device = Device( "quad", tubes );

        var warning = Assert.Single( Normalise( device ) );

        Assert.Contains( "tube_2", warning );
        Assert.Null( device.Tubes![CotiTubes.Tube2].Pod );
        Assert.Equal( "axis_2", device.Tubes[CotiTubes.Tube2].Mount.AnchorBone );
    }

    [Theory]
    [InlineData( "quad" )]
    [InlineData( "hex" )]
    public void ValidationNeverTouchesTheLegacyPair( string layout )
    {
        var tubes = FourTubes();
        tubes[CotiTubes.Tube0].Mount.PositionX = float.NaN;
        var device = Device( layout, tubes );
        var mask = device.Mask;
        var mount = device.Mount;

        Assert.NotEmpty( Normalise( device ) );

        Assert.Same( mask, device.Mask );
        Assert.Same( mount, device.Mount );
        Assert.Equal( 0.5371f, device.Mask.CenterX );
        Assert.Equal( 0.007f, device.Mount.PositionX );
        Assert.Equal( "axis_2", device.Mount.AnchorBone );
    }

    [Fact]
    public void ATubeMountsAtItsOwnMountAndAnAbsentOneAtTheLegacyMount()
    {
        var tubes = FourTubes();
        tubes.Remove( CotiTubes.Tube3 );
        var device = Device( "quad", tubes );
        Normalise( device );

        Assert.Same( device.Tubes![CotiTubes.Tube0].Mount, CotiTubeValidation.MountFor( device, CotiTubes.Tube0 ) );
        Assert.Same( device.Mount, CotiTubeValidation.MountFor( device, CotiTubes.Tube3 ) );
    }

    [Fact]
    public void AV1DeviceMountsEveryLabelAtTheLegacyMount()
    {
        var device = Device( null, null );

        Assert.Same( device.Mount, CotiTubeValidation.MountFor( device, CotiTubes.Tube2 ) );
        Assert.Same( device.Mount, CotiTubeValidation.MountFor( device, CotiTubes.Center ) );
    }
}
