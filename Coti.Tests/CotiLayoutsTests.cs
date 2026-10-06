using Coti.Shared;
using Xunit;

// The four hole patterns measured from BorkelRNVG 3.0.4's masks.
public class CotiLayoutsTests
{
    [Fact]
    public void TheFourLayoutsAreListedInOrder()
    {
        Assert.Equal( new[] { "quad", "dual", "mono", "pvs5a" }, CotiLayouts.All.Select( l => l.Name ).ToArray() );
    }

    [Theory]
    [InlineData( "quad", "tube_2", new[] { "tube_0", "tube_1", "tube_2", "tube_3" } )]
    [InlineData( "dual", "tube_2", new[] { "tube_1", "tube_2" } )]
    [InlineData( "mono", "tube_center", new[] { "tube_center" } )]
    [InlineData( "pvs5a", "tube_2", new[] { "tube_1", "tube_2" } )]
    public void EachLayoutListsItsTubesLeftToRight( string name, string home, string[] labels )
    {
        Assert.True( CotiLayouts.TryGet( name, out var layout ) );
        Assert.Equal( home, layout.Home );
        Assert.Equal( labels, layout.Tubes.Select( t => t.Label ).ToArray() );

        for( var i = 1; i < layout.Tubes.Count; i++ )
            Assert.True( layout.Tubes[i - 1].Dx < layout.Tubes[i].Dx, $"{name}: {labels[i - 1]} is not left of {labels[i]}" );
    }

    [Fact]
    public void TryGetFindsEachLayoutByItsExactName()
    {
        foreach( var layout in CotiLayouts.All )
        {
            Assert.True( CotiLayouts.TryGet( layout.Name, out var found ) );
            Assert.Same( layout, found );
        }
    }

    [Fact]
    public void TryGetRefusesAnyOtherName()
    {
        foreach( var name in new[] { "Quad", "hex", "", null } )
            Assert.False( CotiLayouts.TryGet( name, out _ ), $"\"{name}\" matched a layout" );
    }

    [Fact]
    public void FindIsNullForALabelNotInTheLayout()
    {
        Assert.Null( CotiLayouts.Quad.Find( CotiTubes.Center ) );
        Assert.Null( CotiLayouts.Mono.Find( CotiTubes.Tube2 ) );
        Assert.Null( CotiLayouts.Dual.Find( null ) );
        Assert.Same( CotiLayouts.Mono.Tubes[0], CotiLayouts.Mono.Find( CotiTubes.Center ) );
    }
}
