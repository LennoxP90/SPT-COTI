using Coti.Shared;
using Xunit;

// Tube labels and the slot each one gets. mod_coti stays on today's tube, so a COTI already fitted in a profile stays
// put, and the extra slots come in the order EFT's auto-pick is expected to fill them.
public class CotiTubesTests
{
    private static CotiLayout? Layout( string? name ) => CotiLayouts.TryGet( name, out var layout ) ? layout : null;

    [Theory]
    [InlineData( "quad", new[] { "mod_coti", "mod_coti_1", "mod_coti_3", "mod_coti_0" } )]
    [InlineData( "dual", new[] { "mod_coti", "mod_coti_1" } )]
    [InlineData( "mono", new[] { "mod_coti" } )]
    [InlineData( "pvs5a", new[] { "mod_coti", "mod_coti_1" } )]
    public void SlotNamesFollowTheAutoPickOrder( string layout, string[] slots )
    {
        Assert.Equal( slots, CotiTubes.SlotNames( Layout( layout ) ).ToArray() );
    }

    [Fact]
    public void AV1DeviceHasOnlyModCoti()
    {
        Assert.Equal( new[] { "mod_coti" }, CotiTubes.SlotNames( null ).ToArray() );
    }

    [Theory]
    [InlineData( "quad", "tube_2" )]
    [InlineData( "dual", "tube_2" )]
    [InlineData( "mono", "tube_center" )]
    [InlineData( "pvs5a", "tube_2" )]
    public void TheHomeTubeKeepsModCoti( string layout, string home )
    {
        Assert.Equal( home, Layout( layout )!.Home );
        Assert.Equal( CotiIds.ModSlotName, CotiTubes.SlotName( home, Layout( layout )! ) );
    }

    [Theory]
    [InlineData( "dual", "tube_3" )]
    [InlineData( "mono", "tube_2" )]
    [InlineData( "quad", "tube_center" )]
    public void SlotNameRefusesALabelTheLayoutLacks( string layout, string label )
    {
        Assert.Throws<ArgumentException>( () => CotiTubes.SlotName( label, Layout( layout )! ) );
    }

    [Fact]
    public void LabelOfSlotInvertsSlotNameOnEveryLayout()
    {
        foreach( var layout in CotiLayouts.All )
            foreach( var tube in layout.Tubes )
                Assert.Equal( tube.Label, CotiTubes.LabelOfSlot( CotiTubes.SlotName( tube.Label, layout ), layout ) );
    }

    [Theory]
    [InlineData( "mod_coti_3", "dual" )]
    [InlineData( "mod_coti", null )]
    [InlineData( null, "quad" )]
    [InlineData( "mod_scope", "quad" )]
    public void LabelOfSlotIsNullWhenNoTubeHasTheSlot( string? slot, string? layout )
    {
        Assert.Null( CotiTubes.LabelOfSlot( slot, Layout( layout ) ) );
    }

    [Theory]
    [InlineData( "mod_coti", true )]
    [InlineData( "mod_coti_0", true )]
    [InlineData( "mod_coti_1", true )]
    [InlineData( "mod_coti_3", true )]
    [InlineData( "mod_coti_2", false )]
    [InlineData( "MOD_COTI", false )]
    [InlineData( "mod_scope", false )]
    [InlineData( null, false )]
    public void IsCotiSlotAcceptsExactlyTheFourSlots( string? slot, bool expected )
    {
        Assert.Equal( expected, CotiTubes.IsCotiSlot( slot ) );
    }

    [Theory]
    [InlineData( "quad", "mod_coti", "ECOTI R" )]
    [InlineData( "dual", "mod_coti", "ECOTI R" )]
    [InlineData( "pvs5a", "mod_coti", "ECOTI R" )]
    [InlineData( "mono", "mod_coti", "ECOTI" )]
    [InlineData( null, "mod_coti", "ECOTI" )]
    [InlineData( "quad", "mod_coti_1", "ECOTI L" )]
    [InlineData( "dual", "mod_coti_1", "ECOTI L" )]
    [InlineData( "pvs5a", "mod_coti_1", "ECOTI L" )]
    [InlineData( "quad", "mod_coti_3", "ECOTI OR" )]
    [InlineData( "quad", "mod_coti_0", "ECOTI OL" )]
    public void EverySlotHasItsInGameName( string? layout, string slot, string name )
    {
        Assert.Equal( name, CotiTubes.SlotDisplayName( slot, Layout( layout ) ) );
    }

    [Fact]
    public void ANonCotiSlotHasNoInGameName()
    {
        Assert.Throws<ArgumentException>( () => CotiTubes.SlotDisplayName( "mod_scope", CotiLayouts.Quad ) );
    }

    [Theory]
    [InlineData( "quad", new[] { "ECOTI OL", "ECOTI L", "ECOTI R", "ECOTI OR" } )]
    [InlineData( "dual", new[] { "ECOTI L", "ECOTI R" } )]
    [InlineData( "pvs5a", new[] { "ECOTI L", "ECOTI R" } )]
    [InlineData( "mono", new[] { "ECOTI" } )]
    public void SlotsReadLeftToRightByDisplayRank( string layout, string[] expected )
    {
        var shown = CotiTubes.SlotNames( Layout( layout ) ).OrderBy( CotiTubes.DisplayRank )
            .Select( slot => CotiTubes.SlotDisplayName( slot, Layout( layout ) ) );
        Assert.Equal( expected, shown );
    }

    [Theory]
    [InlineData( "tube_0", CotiTubeSide.Left )]
    [InlineData( "tube_1", CotiTubeSide.Left )]
    [InlineData( "tube_2", CotiTubeSide.Right )]
    [InlineData( "tube_3", CotiTubeSide.Right )]
    [InlineData( "tube_center", CotiTubeSide.Center )]
    [InlineData( null, CotiTubeSide.Center )]
    public void EachLabelIsOnItsSide( string? label, CotiTubeSide side )
    {
        Assert.Equal( side, CotiTubes.SideOf( label ) );
    }

    [Theory]
    [InlineData( "tube_1", "tube_2" )]
    [InlineData( "tube_2", "tube_1" )]
    [InlineData( "tube_0", "tube_3" )]
    [InlineData( "tube_3", "tube_0" )]
    [InlineData( "tube_center", null )]
    public void PartnersAreTheMirrorTubes( string label, string? partner )
    {
        Assert.Equal( partner, CotiTubes.PartnerOf( label ) );
    }

    [Theory]
    [InlineData( "quad", "tube_0", "tube_0 (OL)" )]
    [InlineData( "quad", "tube_1", "tube_1 (L)" )]
    [InlineData( "quad", "tube_2", "tube_2 (R, home)" )]
    [InlineData( "quad", "tube_3", "tube_3 (OR)" )]
    [InlineData( "dual", "tube_1", "tube_1 (L)" )]
    [InlineData( "pvs5a", "tube_2", "tube_2 (R, home)" )]
    // One tube needs no home mark.
    [InlineData( "mono", "tube_center", "tube_center" )]
    public void EachTubeHasItsPanelName( string layout, string label, string name )
    {
        Assert.Equal( name, CotiTubes.PanelName( label, Layout( layout )! ) );
    }
}
