using Coti.Shared;
using Xunit;

// Full mode's fill default went from 55 to 45. A saved 55 is the old default, never a choice, and moves once.
public class CotiFillMigrationTests
{
    [Fact]
    public void TheOldDefaultMovesToTheNewOne()
    {
        Assert.Equal( 45f, CotiThermalModes.MovedFill( 55f ) );
    }

    [Theory]
    [InlineData( 0f )]
    [InlineData( 40f )]
    [InlineData( 45f )]
    [InlineData( 70f )]
    [InlineData( 100f )]
    public void AnyOtherValueIsAChoiceAndStays( float chosen )
    {
        Assert.Equal( chosen, CotiThermalModes.MovedFill( chosen ) );
    }
}
