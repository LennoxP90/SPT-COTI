using Coti.Shared;
using Xunit;

// Both riser colourways are recognised by template id, and nothing else is.
public class CotiRiserIdsTests
{
  [Theory]
  [InlineData( "6ad3016bab020957abb6ca3b", true )]  // black
  [InlineData( "6ad408a96bf1f761d970a4ff", true )]  // tan
  [InlineData( "6a7f15387b68c336e18e8977", false )] // the goggle COTI
  [InlineData( "5c064c400db834001d23f468", false )] // the donor LaRue riser
  [InlineData( "", false )]
  [InlineData( null, false )]
  public void IsRiser( string? tplId, bool expected )
  {
    Assert.Equal( expected, CotiIds.IsRiser( tplId! ) );
  }

  [Fact]
  public void EveryColourwayIsListedOnce()
  {
    Assert.Equal( new[] { CotiIds.RiserTplId, CotiIds.RiserTanTplId }, CotiIds.RiserTplIds );
  }
}
