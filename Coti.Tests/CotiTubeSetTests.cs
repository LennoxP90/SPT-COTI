using Coti.Shared;
using Xunit;

// The per-frame tube state is a set of slots held as bits. Each slot's bit is fixed, so the inventory probe can build a
// set without knowing the device's layout.
public class CotiTubeSetTests
{
    [Theory]
    [InlineData( "mod_coti", 1 )]
    [InlineData( "mod_coti_1", 2 )]
    [InlineData( "mod_coti_3", 4 )]
    [InlineData( "mod_coti_0", 8 )]
    public void EachCotiSlotHasItsOwnBitInAutoPickOrder( string slot, int bit )
    {
        Assert.Equal( bit, CotiTubeSet.Bit( slot ) );
    }

    [Theory]
    [InlineData( null )]
    [InlineData( "" )]
    [InlineData( "mod_nvg" )]
    [InlineData( "MOD_COTI" )]
    public void AnythingElseHasNoBit( string? slot )
    {
        Assert.Equal( 0, CotiTubeSet.Bit( slot ) );
    }

    [Fact]
    public void EveryLayoutsSlotsHaveDistinctBits()
    {
        foreach( var layout in CotiLayouts.All )
        {
            var all = 0;
            foreach( var tube in layout.Tubes )
            {
                var bit = CotiTubeSet.Bit( CotiTubes.SlotName( tube.Label, layout ) );
                Assert.NotEqual( 0, bit );
                Assert.Equal( 0, all & bit );
                all |= bit;
            }
        }
    }

    [Theory]
    [InlineData( 15, "0,1,2,3" )]
    [InlineData( 3, "1,2" )]    // mod_coti and mod_coti_1: the centre pair
    [InlineData( 8, "0" )]
    [InlineData( 0, "none" )]
    public void QuadSetsReadAsTubeNumbersLeftToRight( int slots, string text )
    {
        Assert.Equal( text, CotiTubeSet.Describe( slots, CotiLayouts.Quad ) );
    }

    [Fact]
    public void TheMonoTubeReadsAsCenter()
    {
        Assert.Equal( "center", CotiTubeSet.Describe( 1, CotiLayouts.Mono ) );
    }

    [Theory]
    [InlineData( 1, "mod_coti" )]
    [InlineData( 2, "none" )]   // a device file without tubes has mod_coti only
    public void ADeviceWithoutALayoutHasOnlyModCoti( int slots, string text )
    {
        Assert.Equal( text, CotiTubeSet.Describe( slots, null ) );
    }

    [Fact]
    public void ADualIgnoresTheOuterSlots()
    {
        Assert.Equal( "2", CotiTubeSet.Describe( 1 | 4 | 8, CotiLayouts.Dual ) );
    }
}
