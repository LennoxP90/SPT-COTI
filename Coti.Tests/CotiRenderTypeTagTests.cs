using Coti.Shared;
using Xunit;

// A solid surface whose shader has no RenderType of its own must still hide the heat behind it.
public class CotiRenderTypeTagTests
{
    [Theory]
    [InlineData( 2000, "Opaque" )]
    [InlineData( 2449, "Opaque" )]
    [InlineData( 2450, "TransparentCutout" )]
    [InlineData( 2500, "TransparentCutout" )]
    public void UntaggedSolidSurfacesGetATag( int queue, string expected )
    {
        Assert.Equal( expected, CotiRenderTypeTag.For( "", queue ) );
        Assert.Equal( expected, CotiRenderTypeTag.For( null, queue ) );
    }

    [Theory]
    [InlineData( "Opaque" )]
    [InlineData( "TreeLeaf" )]
    [InlineData( "CotiGlass" )]
    [InlineData( "CotiParticle" )]
    public void ATagTheShaderDrawsIsKept( string own )
    {
        Assert.Null( CotiRenderTypeTag.For( own, 2000 ) );
    }

    [Theory]
    [InlineData( "Transparent", 2000, "Opaque" )]            // a road decal that is the ground on a bridge
    [InlineData( "Transparent", 2450, "TransparentCutout" )]
    [InlineData( "Background", 2100, "Opaque" )]
    public void AnUndrawnTagInTheOpaqueQueuesIsReplaced( string own, int queue, string expected )
    {
        Assert.Equal( expected, CotiRenderTypeTag.For( own, queue, onSolidCollider: true ) );
    }

    [Fact]
    public void AnUndrawnTagWithNoCollisionIsLeftAlone() => Assert.Null( CotiRenderTypeTag.For( "Transparent", 2000, onSolidCollider: false ) );

    [Fact]
    public void TransparentInTheTransparentQueueIsLeftAlone() => Assert.Null( CotiRenderTypeTag.For( "Transparent", 3000 ) );

    [Theory]
    [InlineData( 1000 )]
    [InlineData( 1999 )]
    [InlineData( 2501 )]
    [InlineData( 3000 )]
    public void SkyAndSeeThroughQueuesAreLeftAlone( int queue )
    {
        Assert.Null( CotiRenderTypeTag.For( "", queue ) );
    }
}

// Glass blocks long-wave infrared; smoke, sparks and a player's own sight lens must not.
public class CotiGlassTests
{
    [Fact]
    public void AWindowIsGlass()
    {
        Assert.True( CotiRenderTypeTag.IsGlass( 3000, particles: false, onSolidCollider: true, carriedOrLoot: false ) );
    }

    [Theory]
    [InlineData( 3000, true, true, false )]   // particles
    [InlineData( 3000, false, false, false )] // no collider: rain, tracers, muzzle flash
    [InlineData( 3000, false, true, true )]   // carried or loot: a sight lens
    [InlineData( 2000, false, true, false )]  // opaque, handled by For
    public void EverythingElseIsNot( int queue, bool particles, bool onCollider, bool carried )
    {
        Assert.False( CotiRenderTypeTag.IsGlass( queue, particles, onCollider, carried ) );
    }

    [Fact]
    public void HeatHazeIsNeverDrawn() => Assert.True( CotiRenderTypeTag.IsParticleOnly( usedByParticles: true, usedByMesh: false ) );

    [Theory]
    [InlineData( true, true )]   // debris sharing a rock's material: the rock must stay solid
    [InlineData( false, true )]  // a wall
    [InlineData( false, false )] // unused
    public void AnythingAMeshDrawsKeepsItsTag( bool particles, bool mesh ) =>
        Assert.False( CotiRenderTypeTag.IsParticleOnly( particles, mesh ) );

    [Fact]
    public void EachModeHasItsOwnTag()
    {
        Assert.Equal( "CotiGlass", CotiRenderTypeTag.GlassTag( CotiGlassMode.Plain ) );
        Assert.Equal( "CotiGlassMirror", CotiRenderTypeTag.GlassTag( CotiGlassMode.Reflections ) );
    }
}

public class CotiBodyHeatTests
{
    [Theory]
    [InlineData( "sectantPriest" )]
    [InlineData( "sectantWarrior" )]
    [InlineData( "sectantOni" )]
    public void CultistsAreCultists( string role ) => Assert.True( CotiBodyHeat.IsCultist( role ) );

    [Theory]
    [InlineData( "assault" )]
    [InlineData( "pmcBEAR" )]
    [InlineData( null )]
    public void OthersAreNot( string role ) => Assert.False( CotiBodyHeat.IsCultist( role ) );

    [Theory]
    [InlineData( -5f )]
    [InlineData( 15f )]
    [InlineData( 30f )]
    public void ACultistIsJustAboveTheAir( float air ) => Assert.Equal( air + 3f, CotiBodyHeat.Celsius( "sectantPriest", 36.6f, air ) );

    [Fact]
    public void AnyoneElseKeepsTheirOwn() => Assert.Equal( 37.7f, CotiBodyHeat.Celsius( "assault", 37.7f, 15f ) );
}

public class CotiWorldHeatTests
{
    [Fact]
    public void ABarrelInsideItsFireBurns() => Assert.True( CotiWorldHeat.IsFireProp( 1.5f, 1.2f, insideFire: true, particles: false ) );

    [Theory]
    [InlineData( 400f, 1.2f, true, false )] // the map's edge kill zone
    [InlineData( 1.5f, 60f, true, false )]  // the road under the barrel
    [InlineData( 1.5f, 1.2f, false, false )] // loot beside it
    [InlineData( 1.5f, 1.0f, true, true )]   // the flames
    public void NothingElseDoes( float fire, float renderer, bool inside, bool particles ) =>
        Assert.False( CotiWorldHeat.IsFireProp( fire, renderer, inside, particles ) );

    [Fact]
    public void AGasBlockAt40WithAColdWeaponIsStale() =>
        Assert.True( CotiWorldHeat.IsStaleWeaponHeat( 40f, CotiWorldHeat.WeaponCelsius( 0f ) ) );

    [Fact]
    public void ALootPartAt40IsStale() =>
        Assert.True( CotiWorldHeat.IsStaleWeaponHeat( 40f, CotiWorldHeat.WeaponCelsius( null ) ) );

    [Theory]
    [InlineData( 30f, 0f )]     // at rest
    [InlineData( 34.5f, 99f )]  // fired: 30 + 99/22
    [InlineData( 29f, 0f )]     // a muzzle jet's default
    public void AWeaponAtItsOwnHeatIsLeftAlone( float celsius, float overheat ) =>
        Assert.False( CotiWorldHeat.IsStaleWeaponHeat( celsius, CotiWorldHeat.WeaponCelsius( overheat ) ) );

    [Theory]
    [InlineData( "On" )]
    [InlineData( "TurningOn" )]
    [InlineData( "ConstantFlickering" )]
    public void ALampThatIsOnIsLit( string state ) => Assert.True( CotiWorldHeat.IsLampLit( state ) );

    [Theory]
    [InlineData( "Off" )]
    [InlineData( "TurningOff" )]
    [InlineData( "SmoothOff" )]
    [InlineData( "Destroyed" )]
    public void ALampThatIsOffIsNot( string state ) => Assert.False( CotiWorldHeat.IsLampLit( state ) );

    [Fact]
    public void RestIs30() => Assert.Equal( 30f, CotiWorldHeat.WeaponCelsius( 0f ) );

    // The shader takes 0 for "COTI wrote nothing"; a freezing map's cultist or lit LED must still read as written.
    [Theory]
    [InlineData( -18f )]     // a lit LED at -26 C air
    [InlineData( -2f )]      // a cultist just above -5 C air
    [InlineData( 0f )]
    [InlineData( 36.6f )]
    public void AnyWrittenTemperatureIsAboveZeroInTheBlock( float celsius )
    {
        Assert.True( CotiWorldHeat.BlockValue( celsius ) > 0f );
        Assert.Equal( celsius, CotiWorldHeat.BlockValue( celsius ) - CotiWorldHeat.Kelvin, 3 );
    }

    [Fact]
    public void NoTemperatureIsZeroInTheBlock() => Assert.Equal( 0f, CotiWorldHeat.BlockValue( null ) );
}
