using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

// The bot strip's riser branch: a riser that is not kept goes, its sight moves down to the riser's slot when that
// slot takes it, and anything that cannot move goes with everything under it.
public class CotiRiserBotRuleTests
{
  private const string Riser = CotiIds.RiserTplId;
  private const string Tan = CotiIds.RiserTanTplId;

  // A gun whose upper's mod_scope holds a riser carrying a mount with a red dot on it.
  private static readonly List<(string Id, string? ParentId, string? SlotId, string Tpl)> Gun =
  [
    ( "gun", "equip", "FirstPrimaryWeapon", "m4" ),
    ( "upper", "gun", "mod_reciever", "upper_tpl" ),
    ( "riser", "upper", "mod_scope", Riser ),
    ( "mount", "riser", "mod_scope", "mount_tpl" ),
    ( "dot", "mount", "mod_scope", "dot_tpl" ),
  ];

  private static bool UpperTakesMount( string parent, string slot, string child ) =>
      parent == "upper_tpl" && slot == "mod_scope" && child == "mount_tpl";

  [Fact]
  public void KeptRiserChangesNothing()
  {
    var ( remove, moves ) = CotiRiserBotRule.Plan( Gun, () => true, UpperTakesMount );
    Assert.Empty( remove );
    Assert.Empty( moves );
  }

  [Fact]
  public void DroppedRiserLeavesItsSightOnTheRail()
  {
    var ( remove, moves ) = CotiRiserBotRule.Plan( Gun, () => false, UpperTakesMount );
    Assert.Equal( ["riser"], remove );
    Assert.Equal( [( "mount", "upper", "mod_scope" )], moves );
  }

  [Fact]
  public void SightGoesTooWhenTheRailRefusesIt()
  {
    var ( remove, moves ) = CotiRiserBotRule.Plan( Gun, () => false, ( _, _, _ ) => false );
    Assert.Equal( ["riser", "mount", "dot"], remove.OrderBy( id => id == "riser" ? 0 : id == "mount" ? 1 : 2 ) );
    Assert.Empty( moves );
  }

  [Fact]
  public void LooseRiserInABackpackJustGoes()
  {
    var items = new List<(string, string?, string?, string)>
    {
      ( "bag", "equip", "Backpack", "bag_tpl" ),
      ( "riser", "bag", "main", Tan ),
    };
    var ( remove, moves ) = CotiRiserBotRule.Plan( items, () => false, ( _, _, _ ) => false );
    Assert.Equal( ["riser"], remove );
    Assert.Empty( moves );
  }

  [Fact]
  public void EachRiserRollsOnItsOwn()
  {
    var items = new List<(string, string?, string?, string)>( Gun )
    {
      ( "gun2", "equip", "SecondPrimaryWeapon", "m4" ),
      ( "riser2", "gun2", "mod_scope", Tan ),
      ( "dot2", "riser2", "mod_scope", "dot_tpl" ),
    };
    var rolls = new Queue<bool>( [true, false] );
    var ( remove, moves ) = CotiRiserBotRule.Plan( items, rolls.Dequeue, ( _, _, _ ) => true );
    Assert.Equal( ["riser2"], remove );
    Assert.Equal( [( "dot2", "gun2", "mod_scope" )], moves );
  }

  [Fact]
  public void OtherItemsAreNeverTouched()
  {
    var items = new List<(string, string?, string?, string)> { ( "gun", "equip", "FirstPrimaryWeapon", "m4" ) };
    var asked = false;
    var ( remove, moves ) = CotiRiserBotRule.Plan( items, () => asked = true, ( _, _, _ ) => true );
    Assert.False( asked );
    Assert.Empty( remove );
    Assert.Empty( moves );
  }
}
