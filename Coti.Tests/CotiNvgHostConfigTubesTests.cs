using Coti.Client;
using Coti.Shared;
using Xunit;

// The client's flattened device: the legacy fields as before, plus the layout and, per tube, the
// mount it uses and its pod. Values are the ISB Aishi GPNVG-18's legacy pose and a mirror of it.
public class CotiNvgHostConfigTubesTests
{
    private static CotiMountBlock Mount( string bone, float x, float roll, float yaw )
    {
        return new CotiMountBlock
        {
            AnchorBone = bone, PositionX = x, PositionY = -0.037f, PositionZ = -0.075f,
            RotationX = -90f, RollDegrees = roll, PitchDegrees = 90f, YawDegrees = yaw, Scale = 1.46f,
        };
    }

    private static CotiDeviceFile V1()
    {
        return new CotiDeviceFile
        {
            Schema = 1, Device = "com.samc137.aishi_gpnvg18", DisplayName = "L3Harris GPNVG-18 (ISB Aishi)", Tuned = true,
            Mask = new CotiMaskBlock { CenterX = 0.525f, CenterY = 0.5f, Radius = 0.285f, Feather = 0.01f },
            Mount = Mount( "axis", 0.027f, -26f, 2f ),
        };
    }

    // tube_2 posed slightly off the legacy pose so the two can be told apart; tube_1 its mirror, on
    // a pod; tube_0 and tube_3 not posed.
    private static CotiDeviceFile Quad()
    {
        var device = V1();
        device.Layout = "quad";
        device.Tubes = new Dictionary<string, CotiTube>
        {
            [CotiTubes.Tube2] = new CotiTube { Mount = Mount( "axis", 0.030f, -26f, 2f ) },
            [CotiTubes.Tube1] = new CotiTube
            {
                Mount = Mount( "axis", -0.030f, 26f, -2f ),
                Pod = new CotiPodBlock { Bone = "axis", DownX = -33f },
            },
        };
        return device;
    }

    [Fact]
    public void AV1DeviceHasOnlyTheHomeSlotAtTheLegacyMount()
    {
        var host = CotiNvgHostConfig.FromDevice( V1() );

        Assert.Null( host.Layout );
        Assert.Empty( host.Tubes );
        Assert.Equal( new[] { "mod_coti" }, host.SlotNames );
        Assert.Null( host.TubeForSlot( "mod_coti" ) );
        Assert.Equal( "axis", host.MountForSlot( "mod_coti" ).AnchorBone );
        Assert.Equal( 0.027f, host.MountForSlot( "mod_coti" ).PositionX, 4 );
    }

    [Fact]
    public void TheLegacyFieldsAreCarriedAsBefore()
    {
        var host = CotiNvgHostConfig.FromDevice( Quad() );

        Assert.Equal( "com.samc137.aishi_gpnvg18", host.MaskName );
        Assert.Equal( 0.525f, host.MaskCenterX, 4 );
        Assert.Equal( 0.285f, host.MaskRadius, 4 );
        Assert.Equal( "axis", host.MountAnchorBone );
        Assert.Equal( 0.027f, host.MountPositionX, 4 );
        Assert.Equal( 1.46f, host.MountScale, 4 );
    }

    [Fact]
    public void AQuadDeviceGetsFourSlotsInAutoPickOrder()
    {
        var host = CotiNvgHostConfig.FromDevice( Quad() );

        Assert.Equal( "quad", host.Layout!.Name );
        Assert.Equal( new[] { "mod_coti", "mod_coti_1", "mod_coti_3", "mod_coti_0" }, host.SlotNames );
        Assert.Equal( 4, host.Tubes.Count );
    }

    [Fact]
    public void EachPosedTubeMountsAtItsOwnMount()
    {
        var host = CotiNvgHostConfig.FromDevice( Quad() );

        Assert.Equal( 0.030f, host.MountForSlot( "mod_coti" ).PositionX, 4 );
        Assert.Equal( -0.030f, host.MountForSlot( "mod_coti_1" ).PositionX, 4 );
        Assert.Equal( 26f, host.MountForSlot( "mod_coti_1" ).RollDegrees, 4 );
    }

    [Theory]
    [InlineData( "mod_coti_0" )]
    [InlineData( "mod_coti_3" )]
    public void AnUnposedTubeMountsAtTheLegacyMount( string slotName )
    {
        var host = CotiNvgHostConfig.FromDevice( Quad() );

        Assert.NotNull( host.TubeForSlot( slotName ) );
        Assert.Equal( 0.027f, host.MountForSlot( slotName ).PositionX, 4 );
        Assert.Equal( -26f, host.MountForSlot( slotName ).RollDegrees, 4 );
    }

    [Fact]
    public void OnlyATubeThatDeclaresAPodCarriesIt()
    {
        var host = CotiNvgHostConfig.FromDevice( Quad() );

        Assert.Equal( -33f, host.TubeForSlot( "mod_coti_1" )!.Pod!.DownX, 4 );
        Assert.Equal( "axis", host.TubeForSlot( "mod_coti_1" )!.Pod!.Bone );
        Assert.Null( host.TubeForSlot( "mod_coti" )!.Pod );
        Assert.Null( host.TubeForSlot( "mod_coti_0" )!.Pod );
    }

    [Fact]
    public void EachTubesMessagesSitWhereItsTextBlockSays()
    {
        var device = Quad();
        device.Tubes![CotiTubes.Tube1].Text = new CotiTextBlock { Edge = 0.7f, Y = 0.1f };
        // Ignored once the file has tubes.
        device.Text = new CotiTextBlock { Align = "right" };

        var host = CotiNvgHostConfig.FromDevice( device );

        Assert.Equal( new CotiTextPlacement( CotiTubeSide.Left, 0.7f, 0.1f ), host.TextPlacement( CotiTubes.Tube1 ) );
        Assert.Equal( new CotiTextPlacement( CotiTubeSide.Right, 0.6f, 0f ), host.TextPlacement( CotiTubes.Tube2 ) );
        // Not posed, so not in the file, and still placed by the rule.
        Assert.Equal( new CotiTextPlacement( CotiTubeSide.Left, 0.6f, 0f ), host.TextPlacement( CotiTubes.Tube0 ) );
    }

    [Fact]
    public void AV1DevicesMessagesSitWhereItsTopLevelBlockSays()
    {
        var device = V1();
        Assert.Equal( new CotiTextPlacement( CotiTubeSide.Center, 0f, 0f ), CotiNvgHostConfig.FromDevice( device ).TextPlacement( null ) );

        device.Text = new CotiTextBlock { Edge = -0.1f, Y = 0.2f };
        Assert.Equal( new CotiTextPlacement( CotiTubeSide.Center, -0.1f, 0.2f ), CotiNvgHostConfig.FromDevice( device ).TextPlacement( null ) );
    }

    [Fact]
    public void ASlotOutsideTheLayoutMountsAtTheLegacyMount()
    {
        var device = V1();
        device.Layout = "dual";
        device.Tubes = new Dictionary<string, CotiTube>();

        var host = CotiNvgHostConfig.FromDevice( device );

        Assert.Equal( new[] { "mod_coti", "mod_coti_1" }, host.SlotNames );
        Assert.Null( host.TubeForSlot( "mod_coti_0" ) );
        Assert.Equal( 0.027f, host.MountForSlot( "mod_coti_0" ).PositionX, 4 );
    }

    [Theory]
    [InlineData( "quad", false )]
    [InlineData( "hex", true )]
    [InlineData( null, true )]
    public void AnythingShortOfAKnownLayoutWithTubesIsV1( string? layout, bool withTubes )
    {
        var device = V1();
        device.Layout = layout;
        device.Tubes = withTubes ? new Dictionary<string, CotiTube>() : null;

        var host = CotiNvgHostConfig.FromDevice( device );

        Assert.Null( host.Layout );
        Assert.Equal( new[] { "mod_coti" }, host.SlotNames );
    }
}
