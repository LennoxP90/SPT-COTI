using System.Linq;
using Coti.Shared;
using Xunit;

// Which of a scope's renderers the goggle's 1x cut leaves out: only those in a coarser LOD level.
public class CotiLodCutTests
{
    // The HHS-1: the EXPS3 and the G33 at LOD0, their low-poly copies at LOD1.
    [Fact]
    public void TheCoarseLevelIsLeftOutAndLevelZeroStays()
    {
        var coarse = CotiLodCut.CoarseOnly( new[]
        {
            ( 0, "g33" ), ( 0, "exps3" ), ( 1, "g33_LOD1" ), ( 1, "exps3_LOD1" ),
        } );

        Assert.Equal( new[] { "exps3_LOD1", "g33_LOD1" }, coarse.OrderBy( name => name ) );
    }

    // A renderer at level 0 in one group and a higher level in another is still drawn.
    [Fact]
    public void LevelZeroAnywhereWins()
    {
        var coarse = CotiLodCut.CoarseOnly( new[] { ( 1, "shared" ), ( 0, "shared" ), ( 2, "far" ) } );

        Assert.Equal( new[] { "far" }, coarse );
    }

    [Fact]
    public void NoGroupsLeavesNothingOut()
    {
        Assert.Empty( CotiLodCut.CoarseOnly( new (int, string)[0] ) );
    }
}
