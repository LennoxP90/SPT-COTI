using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

// The optional text block (spec section 3): where a tube's display messages sit. It travels with every copy of the
// device, validation drops one it cannot read, and the mount editor's save writes the editor's blocks.
public class CotiTextBlockTests
{
    private static CotiDeviceFile Dual() => new CotiDeviceFile
    {
        Schema = 1, Device = "d", DisplayName = "D", Tuned = true,
        Hosts = { new CotiHostRef { Id = "111" } },
        Mask = new CotiMaskBlock { CenterX = 0.5353f, CenterY = 0.4991f, Radius = 0.27362f, Feather = 0.01f },
        Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = 0.03f },
        Layout = "dual",
        Tubes = new Dictionary<string, CotiTube>
        {
            [CotiTubes.Tube1] = new CotiTube
            {
                Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = -0.03f },
                Text = new CotiTextBlock { Align = "left", Edge = 0.7f, Y = 0.1f },
            },
            [CotiTubes.Tube2] = new CotiTube { Mount = new CotiMountBlock { AnchorBone = "axis", PositionX = 0.03f } },
        },
    };

    private static List<string> Normalise( CotiDeviceFile device )
    {
        var warnings = new List<string>();
        CotiTubeValidation.Normalise( device, "d.json", warnings );
        return warnings;
    }

    [Fact]
    public void ACopySharesNoTextBlock()
    {
        var device = Dual();
        device.Text = new CotiTextBlock { Align = "center", Y = -0.2f };

        var copy = device.Copy();

        Assert.NotSame( device.Text, copy.Text );
        Assert.Equal( "center", copy.Text!.Align );
        Assert.Equal( -0.2f, copy.Text.Y );
        Assert.NotSame( device.Tubes![CotiTubes.Tube1].Text, copy.Tubes![CotiTubes.Tube1].Text );
        Assert.Equal( 0.7f, copy.Tubes[CotiTubes.Tube1].Text!.Edge );
        Assert.Null( copy.Tubes[CotiTubes.Tube2].Text );
    }

    [Fact]
    public void AReadableBlockIsKeptWithoutAWarning()
    {
        var device = Dual();
        device.Tubes![CotiTubes.Tube2].Text = new CotiTextBlock { Y = 0.05f };

        Assert.Empty( Normalise( device ) );
        Assert.Equal( "left", device.Tubes[CotiTubes.Tube1].Text!.Align );
        Assert.Equal( 0.05f, device.Tubes[CotiTubes.Tube2].Text!.Y );
    }

    [Theory]
    [InlineData( "up", 0.6f, 0f )]
    [InlineData( "Left", 0.6f, 0f )]
    [InlineData( null, float.NaN, 0f )]
    [InlineData( "right", 0.6f, float.PositiveInfinity )]
    public void AnUnreadableTubeBlockIsDroppedAndTheTubeKept( string? align, float edge, float y )
    {
        var device = Dual();
        device.Tubes![CotiTubes.Tube1].Text = new CotiTextBlock { Align = align, Edge = edge, Y = y };

        var warning = Assert.Single( Normalise( device ) );

        Assert.Contains( "tube_1", warning );
        Assert.Contains( "text", warning );
        Assert.Null( device.Tubes[CotiTubes.Tube1].Text );
        Assert.Equal( -0.03f, device.Tubes[CotiTubes.Tube1].Mount.PositionX );
    }

    [Fact]
    public void AV1FileKeepsAReadableTopLevelBlockAndDropsAnUnreadableOne()
    {
        var good = new CotiDeviceFile { Text = new CotiTextBlock { Align = "center", Edge = 0.1f } };
        Assert.Empty( Normalise( good ) );
        Assert.NotNull( good.Text );

        var bad = new CotiDeviceFile { Text = new CotiTextBlock { Align = "middle" } };
        var warning = Assert.Single( Normalise( bad ) );
        Assert.Contains( "middle", warning );
        Assert.Null( bad.Text );
    }

    [Fact]
    public void TheSaveCarriesTheFilesTextBlocksWhenTheEditorGivesNone()
    {
        var dual = Dual();

        var saved = CotiTubeUpgrade.WithLayout( dual, CotiLayouts.Dual, Mounts( dual ) );

        Assert.Equal( 0.7f, saved.Tubes![CotiTubes.Tube1].Text!.Edge );
        Assert.NotSame( dual.Tubes![CotiTubes.Tube1].Text, saved.Tubes[CotiTubes.Tube1].Text );
        Assert.Null( saved.Tubes[CotiTubes.Tube2].Text );
    }

    [Fact]
    public void TheSaveWritesTheEditorsTextBlocksAndNothingElse()
    {
        var dual = Dual();
        // A block the editor reset is absent, and a label outside the layout is not written.
        var texts = new Dictionary<string, CotiTextBlock?>
        {
            [CotiTubes.Tube2] = new CotiTextBlock { Align = "center" },
            [CotiTubes.Tube0] = new CotiTextBlock { Y = 0.3f },
        };

        var saved = CotiTubeUpgrade.WithLayout( dual, CotiLayouts.Dual, Mounts( dual ), texts );

        Assert.Null( saved.Tubes![CotiTubes.Tube1].Text );
        Assert.Equal( "center", saved.Tubes[CotiTubes.Tube2].Text!.Align );
        Assert.NotSame( texts[CotiTubes.Tube2], saved.Tubes[CotiTubes.Tube2].Text );
        Assert.Equal( new[] { "tube_1", "tube_2" }, saved.Tubes.Keys.OrderBy( k => k ) );
    }

    [Fact]
    public void AnEditorTextIsWrittenOnlyForATubeTheEditorPosed()
    {
        // A tube is written only with a mount, so the editor poses a tube (at the legacy mount) when its text is edited.
        var dual = Dual();
        var mounts = new Dictionary<string, CotiMountBlock> { [CotiTubes.Tube2] = dual.Tubes![CotiTubes.Tube2].Mount.Copy() };
        var texts = new Dictionary<string, CotiTextBlock?>
        {
            [CotiTubes.Tube1] = new CotiTextBlock { Y = 0.2f },
            [CotiTubes.Tube2] = new CotiTextBlock { Y = 0.3f },
        };

        var saved = CotiTubeUpgrade.WithLayout( dual, CotiLayouts.Dual, mounts, texts );

        Assert.Equal( new[] { "tube_2" }, saved.Tubes!.Keys.ToArray() );
        Assert.Equal( 0.3f, saved.Tubes[CotiTubes.Tube2].Text!.Y );

        mounts[CotiTubes.Tube1] = dual.Mount.Copy();
        saved = CotiTubeUpgrade.WithLayout( dual, CotiLayouts.Dual, mounts, texts );

        Assert.Equal( 0.2f, saved.Tubes![CotiTubes.Tube1].Text!.Y );
        Assert.Equal( dual.Mount.PositionX, saved.Tubes[CotiTubes.Tube1].Mount.PositionX );
    }

    [Fact]
    public void AMultiTubeFilesTopLevelBlockIsNamedAsIgnored()
    {
        var device = Dual();
        device.Text = new CotiTextBlock { Y = 0.2f };

        var warning = Assert.Single( Normalise( device ) );

        Assert.Contains( "ignored", warning );
        Assert.True( device.IsMultiTube );
    }

    [Fact]
    public void ASavedMultiTubeFileHasNoTopLevelBlock()
    {
        // The top-level block belongs to a file without tubes; once saved with a layout each tube carries its own.
        var v1 = Dual();
        v1.Layout = null;
        v1.Tubes = null;
        v1.Text = new CotiTextBlock { Y = 0.2f };
        var mounts = new Dictionary<string, CotiMountBlock> { [""] = v1.Mount.Copy() };
        CotiTubeUpgrade.Seed( CotiLayouts.Dual, mounts, mounts[""] );

        var saved = CotiTubeUpgrade.WithLayout( v1, CotiLayouts.Dual, mounts );

        Assert.Null( saved.Text );
        Assert.NotNull( v1.Text );
    }

    private static Dictionary<string, CotiMountBlock> Mounts( CotiDeviceFile device ) =>
        device.Tubes!.ToDictionary( t => t.Key, t => t.Value.Mount.Copy() );
}
