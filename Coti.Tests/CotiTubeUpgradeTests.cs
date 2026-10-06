using System;
using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

// Upgrading a one-tube device file to tubes, and the legacy pair every COTI before 3.3.0 reads (spec sections 3 and 6).
public class CotiTubeUpgradeTests
{
    // hosts/vanilla_gpnvg.json as shipped in 3.2.0.
    private static CotiDeviceFile V1Device() => new CotiDeviceFile
    {
        Schema = 1,
        Device = "vanilla_gpnvg",
        DisplayName = "GPNVG-18",
        Tuned = true,
        Hosts = { new CotiHostRef { Id = "5c0558060db834001b735271" } },
        Mask = new CotiMaskBlock { CenterX = 0.525f, CenterY = 0.5f, Radius = 0.285f, Feather = 0.01f },
        Mount = new CotiMountBlock
        {
            AnchorBone = "axis",
            PositionX = 0.027f, PositionY = -0.037f, PositionZ = -0.075f,
            RotationX = -90f,
            RollDegrees = -26f, PitchDegrees = 90f, YawDegrees = 2f,
            Scale = 1.46f,
        },
    };

    // Picking a type for a v1 file in the mount editor: seed the home tube from the pose, then save under the layout.
    private static CotiDeviceFile Upgrade( CotiDeviceFile v1, CotiLayout layout )
    {
        var mounts = new Dictionary<string, CotiMountBlock>();
        CotiTubeUpgrade.Seed( layout, mounts, v1.Mount );
        return CotiTubeUpgrade.WithLayout( v1, layout, mounts );
    }

    [Fact]
    public void TheHomeTubeKeepsThePoseAndItsPartnerGetsTheMirror()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Quad );

        Assert.Equal( "quad", device.Layout );
        Assert.Equal( new[] { "tube_1", "tube_2" }, device.Tubes!.Keys.OrderBy( k => k, StringComparer.Ordinal ) );

        var home = device.Tubes["tube_2"].Mount;
        Assert.Equal( 0.027f, home.PositionX, 4 );
        Assert.Equal( -26f, home.RollDegrees, 4 );
        Assert.Equal( 2f, home.YawDegrees, 4 );

        var partner = device.Tubes["tube_1"].Mount;
        Assert.Equal( -0.027f, partner.PositionX, 4 );
        Assert.Equal( 26f, partner.RollDegrees, 4 );
        Assert.Equal( -2f, partner.YawDegrees, 4 );
        Assert.Equal( "axis", partner.AnchorBone );

        Assert.Null( device.Tubes["tube_2"].Pod );
        Assert.Null( device.Tubes["tube_1"].Pod );
    }

    [Fact]
    public void TheLegacyPairIsRewrittenFromTheHomeTube()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Quad );

        // The quad's home circle at 16:9, centre at 4 decimals and radius at 5.
        Assert.Equal( 0.5371f, device.Mask.CenterX, 4 );
        Assert.Equal( 0.4992f, device.Mask.CenterY, 4 );
        Assert.Equal( 0.28506f, device.Mask.Radius, 5 );
        Assert.Equal( 0.01f, device.Mask.Feather, 4 );

        Assert.Equal( 0.027f, device.Mount.PositionX, 4 );
        Assert.Equal( "axis", device.Mount.AnchorBone );
        Assert.NotSame( device.Tubes!["tube_2"].Mount, device.Mount );
    }

    [Fact]
    public void TheV1DeviceIsLeftAlone()
    {
        var v1 = V1Device();
        var device = Upgrade( v1, CotiLayouts.Quad );
        device.Tubes!["tube_2"].Mount.PositionX = 1f;
        device.Hosts.Clear();

        Assert.Null( v1.Layout );
        Assert.Null( v1.Tubes );
        Assert.Equal( 0.525f, v1.Mask.CenterX, 4 );
        Assert.Equal( 0.027f, v1.Mount.PositionX, 4 );
        Assert.Single( v1.Hosts );
    }

    [Fact]
    public void TheDeviceKeepsItsIdentity()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Quad );

        Assert.Equal( "vanilla_gpnvg", device.Device );
        Assert.Equal( "GPNVG-18", device.DisplayName );
        Assert.True( device.Tuned );
        Assert.Equal( "5c0558060db834001b735271", Assert.Single( device.Hosts ).Id );
    }

    [Fact]
    public void AMonoHasOneTubeAndNoPartner()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Mono );

        Assert.Equal( new[] { "tube_center" }, device.Tubes!.Keys );
        Assert.Equal( 0.5006f, device.Mask.CenterX, 4 );
        Assert.Equal( 0.4992f, device.Mask.CenterY, 4 );
        Assert.Equal( 0.27359f, device.Mask.Radius, 5 );
    }

    [Theory]
    [InlineData( "dual", 0.5353f, 0.4991f, 0.27362f )]
    [InlineData( "pvs5a", 0.5021f, 0.5056f, 0.24311f )]
    public void TheTwoTubeLayoutsPoseTheInnerPair( string name, float x, float y, float radius )
    {
        Assert.True( CotiLayouts.TryGet( name, out var layout ) );
        var device = Upgrade( V1Device(), layout );

        Assert.Equal( new[] { "tube_1", "tube_2" }, device.Tubes!.Keys.OrderBy( k => k, StringComparer.Ordinal ) );
        Assert.Equal( x, device.Mask.CenterX, 4 );
        Assert.Equal( y, device.Mask.CenterY, 4 );
        Assert.Equal( radius, device.Mask.Radius, 5 );
    }

    [Fact]
    public void WithoutAHomeTubeTheLegacyMountStays()
    {
        var device = V1Device();
        device.Layout = "quad";
        device.Tubes = new Dictionary<string, CotiTube>
        {
            ["tube_1"] = new CotiTube { Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = -0.031f } },
        };

        CotiTubeUpgrade.WriteLegacyPair( device, CotiLayouts.Quad );

        Assert.Equal( 0.027f, device.Mount.PositionX, 4 );
        Assert.Equal( 0.5371f, device.Mask.CenterX, 4 );
    }

    [Fact]
    public void AReposedHomeTubeMovesTheLegacyMount()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Quad );
        device.Tubes!["tube_2"].Mount.PositionX = 0.031f;

        CotiTubeUpgrade.WriteLegacyPair( device, CotiLayouts.Quad );

        Assert.Equal( 0.031f, device.Mount.PositionX, 4 );
        Assert.NotSame( device.Tubes["tube_2"].Mount, device.Mount );
    }

    // A quad with all four tubes posed and a pod on tube_1, as the mount editor holds it.
    private static CotiDeviceFile QuadDevice()
    {
        var device = Upgrade( V1Device(), CotiLayouts.Quad );
        device.Tubes!["tube_1"].Pod = new CotiPodBlock { Bone = "axis_3", DownX = 80f };
        device.Tubes["tube_3"] = new CotiTube { Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = 0.0931f } };
        device.Tubes["tube_0"] = new CotiTube { Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = -0.0931f } };
        return device;
    }

    private static Dictionary<string, CotiMountBlock> MountsOf( CotiDeviceFile device ) =>
        device.Tubes!.ToDictionary( t => t.Key, t => t.Value.Mount.Copy() );

    [Fact]
    public void SeedingFillsTheHomeTubeAndItsPartner()
    {
        var mounts = new Dictionary<string, CotiMountBlock>();
        var legacy = V1Device().Mount;

        CotiTubeUpgrade.Seed( CotiLayouts.Quad, mounts, legacy );

        Assert.Equal( new[] { "tube_1", "tube_2" }, mounts.Keys.OrderBy( k => k, StringComparer.Ordinal ) );
        Assert.Equal( 0.027f, mounts["tube_2"].PositionX, 4 );
        Assert.NotSame( legacy, mounts["tube_2"] );
        Assert.Equal( -0.027f, mounts["tube_1"].PositionX, 4 );
    }

    [Fact]
    public void PickingATypeForAFileWithoutALayoutUpgradesItFromTheCurrentPose()
    {
        // The editor holds a layoutless file's one mount under the empty label; a nudge before the pick is kept.
        var v1 = V1Device();
        var mounts = new Dictionary<string, CotiMountBlock> { [""] = v1.Mount.Copy() };
        mounts[""].PositionX = 0.03f;

        CotiTubeUpgrade.Seed( CotiLayouts.Dual, mounts, mounts[""] );
        var saved = CotiTubeUpgrade.WithLayout( v1, CotiLayouts.Dual, mounts );

        Assert.Equal( "dual", saved.Layout );
        Assert.Equal( new[] { "tube_1", "tube_2" }, saved.Tubes!.Keys.OrderBy( k => k, StringComparer.Ordinal ) );
        Assert.Equal( 0.03f, saved.Tubes["tube_2"].Mount.PositionX, 4 );
        Assert.Equal( -0.03f, saved.Tubes["tube_1"].Mount.PositionX, 4 );
        Assert.Equal( 0.03f, saved.Mount.PositionX, 4 );
        Assert.Equal( 0.5353f, saved.Mask.CenterX, 4 );
    }

    [Fact]
    public void SeedingNeverReplacesAPose()
    {
        var mounts = new Dictionary<string, CotiMountBlock>
        {
            ["tube_2"] = new CotiMountBlock { AnchorBone = "axis", PositionX = 0.031f },
            ["tube_0"] = new CotiMountBlock { AnchorBone = "axis", PositionX = -0.09f },
        };

        CotiTubeUpgrade.Seed( CotiLayouts.Quad, mounts, V1Device().Mount );

        Assert.Equal( 0.031f, mounts["tube_2"].PositionX, 4 );
        // The partner comes from the posed home, not from the legacy mount.
        Assert.Equal( -0.031f, mounts["tube_1"].PositionX, 4 );
        Assert.Equal( -0.09f, mounts["tube_0"].PositionX, 4 );
        Assert.False( mounts.ContainsKey( "tube_3" ) );
    }

    [Fact]
    public void SeedingAMonoFillsOnlyTheCentre()
    {
        var mounts = MountsOf( QuadDevice() );

        CotiTubeUpgrade.Seed( CotiLayouts.Mono, mounts, V1Device().Mount );

        Assert.Equal( 0.027f, mounts["tube_center"].PositionX, 4 );
        Assert.Equal( 5, mounts.Count );
    }

    [Fact]
    public void AnOverriddenLayoutKeepsOnlyItsOwnTubes()
    {
        var quad = QuadDevice();

        var dual = CotiTubeUpgrade.WithLayout( quad, CotiLayouts.Dual, MountsOf( quad ) );

        Assert.Equal( "dual", dual.Layout );
        Assert.Equal( new[] { "tube_1", "tube_2" }, dual.Tubes!.Keys.OrderBy( k => k, StringComparer.Ordinal ) );
        // The legacy pair follows the new layout's home circle.
        Assert.Equal( 0.5353f, dual.Mask.CenterX, 4 );
        Assert.Equal( 0.027f, dual.Mount.PositionX, 4 );

        Assert.Equal( "quad", quad.Layout );
        Assert.Equal( 4, quad.Tubes!.Count );
    }

    [Fact]
    public void TheEditedMountsAreWrittenAndPodsCarried()
    {
        var quad = QuadDevice();
        var mounts = MountsOf( quad );
        mounts["tube_2"].PositionX = 0.031f;
        mounts["tube_1"].PositionX = -0.031f;
        // The v1 mount the editor may still hold, and anything else outside the layout, is not written.
        mounts[""] = new CotiMountBlock { PositionX = 0.5f };

        var saved = CotiTubeUpgrade.WithLayout( quad, CotiLayouts.Quad, mounts );

        Assert.Equal( 4, saved.Tubes!.Count );
        Assert.Equal( 0.031f, saved.Tubes["tube_2"].Mount.PositionX, 4 );
        Assert.Equal( 0.031f, saved.Mount.PositionX, 4 );
        Assert.Equal( -0.031f, saved.Tubes["tube_1"].Mount.PositionX, 4 );
        Assert.Equal( "axis_3", saved.Tubes["tube_1"].Pod!.Bone );
        Assert.Equal( 80f, saved.Tubes["tube_1"].Pod!.DownX, 4 );
        Assert.NotSame( quad.Tubes!["tube_1"].Pod, saved.Tubes["tube_1"].Pod );
        Assert.NotSame( mounts["tube_2"], saved.Tubes["tube_2"].Mount );
    }
}
