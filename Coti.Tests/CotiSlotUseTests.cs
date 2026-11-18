using System.Collections.Generic;
using Coti.Shared;
using Xunit;

// With auto-discovery off, a goggle keeps its COTI slots only when a saved item list still has one occupied. A miss
// here leaves a profile holding an item in a slot that no longer exists, so the rule is pinned.
public class CotiSlotUseTests
{
    private const string Goggle = "aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Coti = "6a7f15387b68c336e18e8977";

    private static HashSet<string> Collect( params CotiSavedItem[] items )
    {
        var hosts = new HashSet<string>();
        CotiSlotUse.CollectHostsInUse( items, hosts );
        return hosts;
    }

    [Theory]
    [InlineData( "mod_coti" )]
    [InlineData( "mod_coti_0" )]
    [InlineData( "mod_coti_1" )]
    [InlineData( "mod_coti_3" )]
    public void OccupiedCotiSlotNamesItsHost( string slot )
    {
        var hosts = Collect(
            new CotiSavedItem( "g1", Goggle, "helmet", "mod_nvg" ),
            new CotiSavedItem( "c1", Coti, "g1", slot ) );

        Assert.Equal( new[] { Goggle }, hosts );
    }

    [Fact]
    public void OtherSlotsNameNothing()
    {
        Assert.Empty( Collect(
            new CotiSavedItem( "g1", Goggle, "helmet", "mod_nvg" ),
            new CotiSavedItem( "b1", "battery", "g1", "mod_equipment" ) ) );
    }

    [Fact]
    public void ParentOutsideTheListNamesNothing()
    {
        Assert.Empty( Collect( new CotiSavedItem( "c1", Coti, "elsewhere", "mod_coti" ) ) );
    }

    [Fact]
    public void ParentListedAfterItsChildStillCounts()
    {
        var hosts = Collect(
            new CotiSavedItem( "c1", Coti, "g1", "mod_coti" ),
            new CotiSavedItem( "g1", Goggle, "helmet", "mod_nvg" ) );

        Assert.Contains( Goggle, hosts );
    }
}
