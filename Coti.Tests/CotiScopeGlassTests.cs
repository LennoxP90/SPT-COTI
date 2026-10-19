using Coti.Shared;
using Xunit;

// Which sights count as magnified, and which of a magnified scope's materials are glass the thermal should draw.
public class CotiScopeGlassTests
{
    [Fact]
    public void MaxZoomIsTheLargestValueAcrossEveryMode()
    {
        Assert.Equal( 6f, CotiScopeGlass.MaxZoom( new[] { new[] { 1f, 6f } } ) );
        Assert.Equal( 9f, CotiScopeGlass.MaxZoom( new[] { new[] { 2.25f, 9f } } ) );
        Assert.Equal( 4f, CotiScopeGlass.MaxZoom( new[] { new[] { 1f }, new[] { 4f } } ) );
    }

    [Fact]
    public void MaxZoomOfNothingIsOne()
    {
        Assert.Equal( 1f, CotiScopeGlass.MaxZoom( null ) );
        Assert.Equal( 1f, CotiScopeGlass.MaxZoom( new float[0][] ) );
        Assert.Equal( 1f, CotiScopeGlass.MaxZoom( new[] { (float[])null, new float[0] } ) );
    }

    [Theory]
    [InlineData( 6f, false, false, true )]      // Razor HD 1-6x
    [InlineData( 1.16f, false, false, true )]   // just above the gate
    [InlineData( 1.15f, false, false, false )]  // at the gate, not filed as a scope: not magnified
    [InlineData( 1f, false, false, false )]     // a red dot or 1x sight
    [InlineData( 1f, true, false, true )]       // Nightforce NXS 2.5-10x, Elcan Specter OS4x: a scope whose zooms say 1
    [InlineData( 9f, false, true, false )]      // FLIR RS-32: a special scope
    [InlineData( 9f, true, true, false )]       // a special scope is never magnified glass, whatever else it claims
    public void MagnifiedMeansAScopeOrAboveTheGateAndNeverASpecialScope( float maxZoom, bool scope, bool special, bool expected )
    {
        Assert.Equal( expected, CotiScopeGlass.IsMagnified( maxZoom, scope, special, 1.15f ) );
    }

    [Theory]
    [InlineData( "back_linza", "backLens" )]
    [InlineData( "back_linza", "backLens (1)" )]
    [InlineData( "back_linza", "backLens_000" )]
    [InlineData( "scope_30mm_razor_hd_gen_2_1_6x24_LOD0_glass", "scope_30mm_razor_hd_gen_2_1_6x24_LOD0" )]
    [InlineData( "scope_base_trijicon_acog_ta11_3.5x35_glass_LOD0", "scope_base_trijicon_acog_ta11_3.5x35_LOD0" )]
    [InlineData( "default_scope_front_glass", "scope_34mm_nightforce_atacr_7_35x56_LOD0" )]
    [InlineData( "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0_scope_glass", "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0" )]
    [InlineData( "MI_AN_PVS22_Glass", "pvs22" )]
    public void OwnRearAndFrontGlassIsScopeGlass( string material, string obj )
    {
        Assert.True( CotiScopeGlass.IsScopeGlass( material, obj, ownAsset: true ) );
    }

    [Theory]
    [InlineData( "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0_collimator_glass", "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0" )]
    [InlineData( "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0_collimator_linza", "linza_collimator" )]
    [InlineData( "anything_glass", "linza_collimator_LOD0" )]
    [InlineData( "scope_30mm_razor_hd_gen_2_1_6x24_LOD0_linza", "linza_mode_000" )]   // the aimed picture surface
    [InlineData( "scope_30mm_razor_hd_gen_2_1_6x24_LOD0", "scope_30mm_razor_hd_gen_2_1_6x24_LOD0" )]   // the body
    [InlineData( "", "" )]
    [InlineData( null, null )]
    public void RedDotPartsPictureSurfaceAndBodyAreNot( string material, string obj )
    {
        Assert.False( CotiScopeGlass.IsScopeGlass( material, obj, ownAsset: true ) );
    }

    [Fact]
    public void ASharedMaterialIsNeverScopeGlass()
    {
        // The EOTech HHS-1 draws the standalone EXPS3 holo's glass through a dependency bundle.
        Assert.False( CotiScopeGlass.IsScopeGlass( "scope_all_eotech_exps3_LOD0_glass", "scope_all_eotech_exps3_LOD0", ownAsset: false ) );
        Assert.False( CotiScopeGlass.IsScopeGlass( "back_linza", "backLens", ownAsset: false ) );
    }
}

// A red dot paired with a magnifier ships its own red dot body with glass named like a scope's; that window is found by
// where it sits, never by name or item id. Boxes are taken from the prefabs in the sight lab (metres, root frame).
public class CotiRedDotWindowTests
{
    private static bool Covers( float[] glassCentre, float[] glassSize, float[] lensCentre, float[] lensSize )
    {
        float[] Min( float[] c, float[] s ) => new[] { c[0] - s[0] / 2, c[1] - s[1] / 2, c[2] - s[2] / 2 };
        float[] Max( float[] c, float[] s ) => new[] { c[0] + s[0] / 2, c[1] + s[1] / 2, c[2] + s[2] / 2 };
        return CotiScopeGlass.CoversLens( Min( glassCentre, glassSize ), Max( glassCentre, glassSize ), Min( lensCentre, lensSize ), Max( lensCentre, lensSize ) );
    }

    [Fact]
    public void AComboRedDotWindowCoversItsLens()
    {
        // Epic's Micro T-1 with a Unity FTC magnifier: T1 Glass, 4 mm in front of the 1x lens.
        Assert.True( Covers( new[] { 0f, -0.0013f, 0.2096f }, new[] { 0.0209f, 0.0208f, 0.0582f },
                             new[] { 0f, -0.0013f, 0.2135f }, new[] { 0.0209f, 0.0209f, 0f } ) );
    }

    [Fact]
    public void ADualOpticsScopeGlassBelowItsRedDotDoesNot()
    {
        // NcStar ADO: the 3-9x scope's front glass sits 4.3 cm below the red dot on top of it.
        Assert.False( Covers( new[] { 0.0001f, 0.0394f, -0.111f }, new[] { 0.0229f, 0.0229f, 0.0027f },
                              new[] { 0.0001f, 0.0819f, 0.0621f }, new[] { 0.0234f, 0.0185f, 0f } ) );
    }

    [Fact]
    public void TheLineOfSightIsTheLensThinnestAxis()
    {
        // A lens facing along x: glass anywhere along x but overlapping in y and z covers it.
        Assert.True( Covers( new[] { 0.3f, 0f, 0f }, new[] { 0.01f, 0.02f, 0.02f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0.02f, 0.02f } ) );
        Assert.False( Covers( new[] { 0f, 0f, 0.3f }, new[] { 0.02f, 0.02f, 0.01f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0.02f, 0.02f } ) );
    }

    [Theory]
    [InlineData( "scope_base_aimpoint_micro_t1_LOD0", false, true, true )]   // the combo window
    [InlineData( "scope_all_eotech_g33_pos_1_LOD0", true, true, false )]     // the magnifier's own glass, in line with the dot
    [InlineData( "backLens", false, true, false )]                           // a rear glass stays the magnifier's
    [InlineData( "scope_all_ncstar_advance_dual_optic_3_9x_42_LOD0", false, false, false )]   // off the dot's line
    [InlineData( null, false, true, true )]
    public void OnlyFrontGlassOutsideTheMagnifierOnTheDotsLineIsAWindow( string obj, bool underOptic, bool covers, bool expected )
    {
        Assert.Equal( expected, CotiScopeGlass.IsRedDotWindow( obj, underOptic, covers ) );
    }
}

// Every bundle a scope bundle depends on, directly or through another dependency: where shared materials come from.
public class CotiScopeGlassDependencyTests
{
    private static System.Collections.Generic.IEnumerable<string> Deps( string key )
    {
        switch( key )
        {
            case "hhs_1": return new[] { "exps3", "g33", "shaders" };
            case "exps3": return new[] { "shaders" };
            case "g33": return new[] { "textures", "shaders" };
            case "loop_a": return new[] { "loop_b" };
            case "loop_b": return new[] { "loop_a" };
            default: return null;
        }
    }

    [Fact]
    public void TheClosureHoldsDirectAndIndirectDependenciesOnceAndNotTheBundleItself()
    {
        var closure = CotiScopeGlass.DependencyClosure( "hhs_1", Deps );
        Assert.Equal( new[] { "exps3", "g33", "shaders", "textures" }, System.Linq.Enumerable.OrderBy( closure, k => k ) );
    }

    [Fact]
    public void ABundleWithNoDependenciesHasAnEmptyClosure()
    {
        Assert.Empty( CotiScopeGlass.DependencyClosure( "razor_hd", Deps ) );
    }

    [Fact]
    public void ADependencyLoopEnds()
    {
        Assert.Equal( new[] { "loop_b" }, CotiScopeGlass.DependencyClosure( "loop_a", Deps ) );
    }
}

// Scope glass follows world glass, unless its own toggle turns its reflections off.
public class CotiScopeGlassModeTests
{
    [Theory]
    [InlineData( CotiGlassMode.Reflections, true, CotiGlassMode.Reflections )]
    [InlineData( CotiGlassMode.Reflections, false, CotiGlassMode.Plain )]
    [InlineData( CotiGlassMode.Plain, true, CotiGlassMode.Plain )]
    [InlineData( CotiGlassMode.Plain, false, CotiGlassMode.Plain )]
    public void ScopeGlassReflectsOnlyWhenBothAreOn( CotiGlassMode world, bool scopeReflections, CotiGlassMode expected )
    {
        Assert.Equal( expected, CotiScopeGlass.Mode( world, scopeReflections ) );
    }
}
