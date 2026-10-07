using Coti.Shared;
using Xunit;

// CotiThermalWorld.TagUntaggedMaterials decides each opaque-queue material once and never reads the tag of one outside
// those queues. Both rest on CotiRenderTypeTag.For, pinned here.
public class CotiMaterialSweepTests
{
    private static readonly string[] Tags = { null, "", "Opaque", "TransparentCutout", "Transparent", "Background", "TreeLeaf", "CotiGlass", "CotiParticle" };
    private static readonly int[] OpaqueQueues = { 2000, 2001, 2449, 2450, 2500 };

    [Theory]
    [InlineData( 1000 )]
    [InlineData( 1999 )]
    [InlineData( 2501 )]
    [InlineData( 3000 )]
    [InlineData( 4000 )]
    public void OutsideTheOpaqueQueuesNoTagIsGivenWhateverTheMaterialsOwn( int queue )
    {
        Assert.Null( CotiRenderTypeTag.For( null, queue ) );
        foreach( var tag in Tags )
        {
            Assert.Null( CotiRenderTypeTag.For( tag, queue, onSolidCollider: false ) );
            Assert.Null( CotiRenderTypeTag.For( tag, queue, onSolidCollider: true ) );
        }
    }

    [Fact]
    public void InsideThemWhetherATagIsGivenDoesNotDependOnTheQueue()
    {
        foreach( var tag in Tags )
            foreach( var solid in new[] { false, true } )
            {
                var given = CotiRenderTypeTag.For( tag, OpaqueQueues[0], solid ) != null;
                foreach( var queue in OpaqueQueues )
                    Assert.Equal( given, CotiRenderTypeTag.For( tag, queue, solid ) != null );
            }
    }

    [Fact]
    public void AGivenTagIsLeftAloneOnTheNextSweep()
    {
        foreach( var queue in OpaqueQueues )
        {
            var given = CotiRenderTypeTag.For( null, queue );
            Assert.NotNull( given );
            Assert.Null( CotiRenderTypeTag.For( given, queue, onSolidCollider: false ) );
            Assert.Null( CotiRenderTypeTag.For( given, queue, onSolidCollider: true ) );
        }
    }
}
