using Coti.Shared;
using Xunit;

// Every scope ring is recognised by template id, and neither riser nor base mount is.
public class CotiScopeRingIdsTests
{
  [Theory]
  [InlineData( "6ad5739462c39577194eedc5", true )]  // TSR-1 30 mm
  [InlineData( "6ad57762f51afaf4379f19a2", true )]  // TSR-1 34 mm tan
  [InlineData( "6ad5776a93ea4db35ed55dcf", true )]  // TSR-2 35 mm tan
  [InlineData( "6ad3016bab020957abb6ca3b", false )] // the riser
  [InlineData( "6171407e50224f204c1da3c5", false )] // the 30 mm base mount
  [InlineData( "", false )]
  [InlineData( null, false )]
  public void IsScopeRing( string? tplId, bool expected )
  {
    Assert.Equal( expected, CotiIds.IsScopeRing( tplId! ) );
  }

  [Fact]
  public void SixVariantsNoneShared()
  {
    Assert.Equal( 6, CotiIds.ScopeRingTplIds.Distinct().Count() );
    Assert.DoesNotContain( CotiIds.ScopeRingTplIds, CotiIds.IsRiser );
  }
}
