using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

// The thermal riser's scope filter: red dots, ring-free scopes and red dot mounts among the donor's peers, minus
// excluded ids. A mount passes only if every mount it holds, at any depth, passes too, and nothing an accepted item
// can hold, at any depth, may magnify. The cases follow the sight lab inventory of 2026-10-18.
public class CotiRiserSightRuleTests
{
  // Red dot class. hhs and combo magnify (vanilla HHS-1, EpicsAIO EXPS3 with G33); fastMicro is Eukyre's FAST Micro
  // mount, red dot class with slots of its own; cs is WTT Armory's Aimpoint CS.
  private static readonly HashSet<string> Dots = ["dot", "dot2", "hhs", "combo", "fastMicro", "cs", "badDot", "sneaky"];
  private static readonly HashSet<string> Mounts =
      ["base", "spacer", "ring", "loopA", "loopB", "empty", "deep1", "deep2", "rmrMount", "hybridMount"];
  // AssaultScope or OpticScope. os4x reads 1x in the database (the bundle sets its zoom) and is a scope by class.
  private static readonly HashSet<string> Scopes = ["hamr", "bravo4", "specterDr", "ado", "os4x", "acog", "scopeOnScope"];
  // "thermal" is a SpecialScope: magnifies (sightModType optic) but is never accepted.
  private static readonly HashSet<string> Magnified = [..Scopes, "hhs", "combo", "thermal"];

  private static readonly Dictionary<string, string[]> ScopeSlots = new()
  {
    ["base"] = ["dot", "spacer"],      // Aimpoint base holding a spacer
    ["spacer"] = ["dot", "dot2"],
    ["ring"] = ["dot", "acog"],        // holds a magnified scope
    ["loopA"] = ["dot", "loopB"],      // stackable mounts that take each other
    ["loopB"] = ["dot", "loopA"],
    ["empty"] = ["spacer"],            // holds no red dot of its own
    ["deep1"] = ["dot", "deep2"],
    ["deep2"] = ["dot", "ring"],       // two levels down: the ring and its scope
    ["rmrMount"] = ["dot"],
    ["hybridMount"] = ["dot", "hhs"],  // passes the red dot mount test, but can carry the HHS-1
    ["hamr"] = ["dot", "dot2"],        // piggyback red dot
    ["bravo4"] = ["dot", "base", "spacer"],
    ["scopeOnScope"] = ["dot", "acog"],
    ["fastMicro"] = ["dot", "dot2"],
    ["cs"] = ["dot", "base"],
    ["badDot"] = ["dot", "deep2"],     // a red dot that can carry a ring
    ["aux"] = ["acog"],
  };

  private static readonly Dictionary<string, string[]> MountSlots = new()
  {
    ["specterDr"] = ["rmrMount"],      // EpicsAIO SpecterDR 1.5x/6x: piggyback through its RMR mount
    ["fastMicro"] = ["skiff"],         // a rail piece that holds nothing
    ["sneaky"] = ["aux"],              // reaches a scope through an item of another class
  };

  private static string[] Get( Dictionary<string, string[]> slots, string id ) => slots.TryGetValue( id, out var h ) ? h : [];

  private static HashSet<string> Accept( IEnumerable<string> peers, params string[] excluded )
  {
    return CotiRiserSightRule.Accept( peers, excluded.Contains, Dots.Contains, Mounts.Contains, Scopes.Contains,
        Magnified.Contains, id => Get( ScopeSlots, id ), id => Get( ScopeSlots, id ).Concat( Get( MountSlots, id ) ) );
  }

  private static string[] Sorted( HashSet<string> ids ) => ids.OrderBy( x => x, System.StringComparer.Ordinal ).ToArray();

  [Fact]
  public void RedDotsAndMountsHoldingOnlyRedDotsAndGoodMountsPass()
  {
    Assert.Equal( ["base", "dot", "spacer"], Sorted( Accept( ["dot", "base", "spacer", "skiff"] ) ) );
  }

  [Fact]
  public void AMountHoldingAMagnifiedScopeFails()
  {
    Assert.Empty( Accept( ["ring"] ) );
  }

  [Fact]
  public void AMountFailsWhenAMountItHoldsFailsAtAnyDepth()
  {
    Assert.Empty( Accept( ["deep1", "deep2"] ) );
  }

  [Fact]
  public void AMountHoldingNoRedDotFails()
  {
    Assert.Empty( Accept( ["empty"] ) );
  }

  [Fact]
  public void MountsThatHoldEachOtherPassWithoutLooping()
  {
    Assert.Equal( ["loopA", "loopB"], Sorted( Accept( ["loopA", "loopB"] ) ) );
  }

  [Fact]
  public void ExcludedIdsAreRemovedFromThePeersOnly()
  {
    Assert.Equal( ["base"], Sorted( Accept( ["dot", "base"], "dot" ) ) );
  }

  [Fact]
  public void RedDotsThatMagnifyThemselvesStay()
  {
    Assert.Equal( ["combo", "hhs"], Sorted( Accept( ["hhs", "combo"] ) ) );
  }

  [Fact]
  public void RingFreeScopesPassWithTheirPiggybackSlots()
  {
    Assert.Equal( ["ado", "bravo4", "hamr", "os4x", "specterDr"],
        Sorted( Accept( ["hamr", "bravo4", "specterDr", "ado", "os4x"] ) ) );
  }

  [Fact]
  public void ASpecialScopeIsNeverAccepted()
  {
    Assert.Empty( Accept( ["thermal"] ) );
  }

  [Fact]
  public void AScopeWhosePiggybackSlotTakesAScopeFails()
  {
    Assert.Empty( Accept( ["scopeOnScope"] ) );
  }

  [Fact]
  public void AMountThatCanCarryAMagnifyingRedDotFails()
  {
    Assert.Empty( Accept( ["hybridMount"] ) );
  }

  [Fact]
  public void RedDotsWithSlotsPassOnlyWhenNothingTheyCanHoldMagnifies()
  {
    Assert.Equal( ["cs", "fastMicro"], Sorted( Accept( ["fastMicro", "cs", "badDot", "sneaky"] ) ) );
  }

  [Theory]
  [InlineData( "mod_sight_rear", true )]
  [InlineData( "mod_sight_rear_000", true )]
  [InlineData( "mod_sight_rear_001", true )]
  [InlineData( "mod_rear_sight", true )]
  [InlineData( "MOD_SIGHT_REAR", true )]
  [InlineData( "mod_sight_front", false )]
  [InlineData( "mod_sight", false )]
  [InlineData( "mod_scope", false )]
  [InlineData( null, false )]
  public void RearSightSlotsAreFoundByName( string? name, bool rear )
  {
    Assert.Equal( rear, CotiRiserSightRule.IsRearSightSlot( name ) );
  }

  [Theory]
  [InlineData( "mod_scope", true )]
  [InlineData( "mod_scope_001", true )]
  [InlineData( "mod_mount", true )]
  [InlineData( "mod_mount_000", true )]
  [InlineData( "mod_tactical", false )]
  [InlineData( "mod_sight_rear", false )]
  [InlineData( null, false )]
  public void HolderSlotsAreScopeAndMountSlots( string? name, bool holder )
  {
    Assert.Equal( holder, CotiRiserSightRule.IsHolderSlot( name ) );
  }

  [Fact]
  public void MagnifyingIsJudgedByZoomAndSightType()
  {
    Assert.True( CotiRiserSightRule.Magnifies( [[3, 1]], "hybrid" ) );    // HHS-1 and the combos
    Assert.True( CotiRiserSightRule.Magnifies( [[1.6], [1]], "reflex" ) );
    Assert.True( CotiRiserSightRule.Magnifies( [[1]], "optic" ) );         // Specter OS4x reads 1x
    Assert.True( CotiRiserSightRule.Magnifies( null, "Hybrid" ) );
    Assert.False( CotiRiserSightRule.Magnifies( [[1]], "reflex" ) );
    Assert.False( CotiRiserSightRule.Magnifies( [[1]], "holo" ) );
    Assert.False( CotiRiserSightRule.Magnifies( [[1]], "iron" ) );
    Assert.False( CotiRiserSightRule.Magnifies( null, null ) );
  }

  // Scope slots beyond the LaRue set. The MCX receiver's mod_scope takes red dots but not the LaRue.
  private static readonly HashSet<string> RiserTakes = ["dot", "base", "hamr"];
  private static readonly HashSet<string> Sights = ["dot", "hamr", "acog", "riser"];

  private static bool Extra( string? slot, string owner, params string[] filter ) =>
      CotiRiserSightRule.IsExtraHostSlot( slot, owner, filter, Sights.Contains, RiserTakes );

  [Fact]
  public void AReceiverScopeSlotTakingARedDotHostsTheRiser()
  {
    Assert.True( Extra( "mod_scope", "mcxReceiver", "dot", "acog" ) );
    Assert.True( Extra( "MOD_SCOPE_001", "handguard", "base" ) );
  }

  [Fact]
  public void ASlotTakingNothingTheRiserTakesIsLeftOut()
  {
    Assert.False( Extra( "mod_scope", "dovetailRail", "dovetailMount" ) );
    Assert.False( Extra( "mod_scope", "mcxReceiver" ) );
  }

  [Fact]
  public void OnlyScopeSlotsHostTheRiser()
  {
    Assert.False( Extra( "mod_mount", "mcxReceiver", "dot" ) );
    Assert.False( Extra( "mod_tactical", "mcxReceiver", "dot" ) );
    Assert.False( Extra( null, "mcxReceiver", "dot" ) );
  }

  [Fact]
  public void NoRiserOnWhatTheRiserTakesOrOnASight()
  {
    Assert.False( Extra( "mod_scope", "base", "dot" ) );   // a mount the riser takes
    Assert.False( Extra( "mod_scope", "hamr", "dot" ) );   // a scope's piggyback slot
    Assert.False( Extra( "mod_scope", "acog", "dot" ) );   // a scope the riser refuses
    Assert.False( Extra( "mod_scope", "riser", "dot" ) );  // the riser itself
  }
}
