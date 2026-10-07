using Coti.Shared;
using Xunit;

// A block is written again only when it no longer holds COTI's value.
public class CotiHeatRewriteTests
{
    [Fact]
    public void AnUnchangedValueHolds() => Assert.True( CotiHeatRewrite.Holds( CotiWorldHeat.BlockValue( 36.6f ), CotiWorldHeat.BlockValue( 36.6f ) ) );

    [Fact]
    public void TheAirsSlowDriftHolds() => Assert.True( CotiHeatRewrite.Holds( CotiWorldHeat.BlockValue( 16.500f ), CotiWorldHeat.BlockValue( 16.505f ) ) );

    [Fact]
    public void AChangedTemperatureDoesNot() => Assert.False( CotiHeatRewrite.Holds( CotiWorldHeat.BlockValue( 36.6f ), CotiWorldHeat.BlockValue( 36.7f ) ) );

    [Theory]
    [InlineData( -20f )]   // a cultist on a freezing map
    [InlineData( 0f )]
    [InlineData( 60f )]    // a burning fire
    public void ABlockEftReplacedDoesNot( float celsius ) => Assert.False( CotiHeatRewrite.Holds( 0f, CotiWorldHeat.BlockValue( celsius ) ) );

    [Fact]
    public void NoHeatHoldsOnlyAsNoHeat()
    {
        Assert.True( CotiHeatRewrite.Holds( 0f, CotiWorldHeat.BlockValue( null ) ) );
        Assert.False( CotiHeatRewrite.Holds( CotiWorldHeat.BlockValue( 16.5f ), CotiWorldHeat.BlockValue( null ) ) );
    }
}
