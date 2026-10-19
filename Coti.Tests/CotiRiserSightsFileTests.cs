using System.Linq;
using Coti.Server;
using Xunit;

// config/riser-sights.json: the thermal riser's deny list. A file that is not one gives null and a reason, so the server
// denies nothing and says why; a bad id drops only its own entry.
public class CotiRiserSightsFileTests
{
  [Fact]
  public void CommentsAndTrailingCommasAreAllowed()
  {
    var file = ThermalRiserSightsFile.Parse( """
        // hand-edited
        { "deny": [ { "id": "f849f458cb4ad62c5d8b96e1", "note": "riser on riser" }, ], }
        """, out var error );

    Assert.NotNull( file );
    Assert.Equal( string.Empty, error );
    Assert.Equal( "riser on riser", Assert.Single( file!.Deny ).Note );
  }

  [Theory]
  [InlineData( "not json" )]
  [InlineData( "{ \"deny\": null }" )]
  [InlineData( "null" )]
  [InlineData( "{ \"deny\": \"f849f458cb4ad62c5d8b96e1\" }" )]
  public void AnythingElseGivesNullAndAReason( string json )
  {
    Assert.Null( ThermalRiserSightsFile.Parse( json, out var error ) );
    Assert.NotEmpty( error );
  }

  [Fact]
  public void AMissingListIsAnEmptyOne()
  {
    // An absent key leaves the initialiser, unlike an explicit null.
    Assert.Empty( ThermalRiserSightsFile.Parse( "{}", out _ )!.Deny );
  }

  [Fact]
  public void ABadIdDropsOnlyItsOwnEntryAndIsNamed()
  {
    var file = ThermalRiserSightsFile.Parse( """
        { "deny": [ { "id": "F849F458CB4AD62C5D8B96E1" }, { "id": "f849f458" }, { "note": "no id" }, null ] }
        """, out var error );

    Assert.Equal( "f849f458cb4ad62c5d8b96e1", Assert.Single( file!.Deny ).Id );
    Assert.Contains( "f849f458", error );
    Assert.Contains( "skipped 3", error );
    Assert.Contains( "(no id)", error );
  }

  [Fact]
  public void NamesMatchInAnyCase()
  {
    // A hand edit that capitalises a key must not read as an empty list.
    var file = ThermalRiserSightsFile.Parse( """{ "Deny": [ { "ID": "f849f458cb4ad62c5d8b96e1", "Note": "x" } ] }""", out var error );

    Assert.Equal( string.Empty, error );
    var entry = Assert.Single( file!.Deny );
    Assert.Equal( ( "f849f458cb4ad62c5d8b96e1", "x" ), ( entry.Id, entry.Note ) );
  }

  [Fact]
  public void WhatThePageSavesReadsBack()
  {
    var saved = new ThermalRiserSightsFile
    {
      Deny = [new DeniedSight { Id = "03f6c86c56945862820e1006", Note = "its own \"micro\" mount slot isn't checked" }],
    };

    var json = saved.Serialize();
    var read = ThermalRiserSightsFile.Parse( json, out var error );

    Assert.Equal( string.Empty, error );
    Assert.Equal( saved.Deny.Select( d => ( d.Id, d.Note ) ), read!.Deny.Select( d => ( d.Id, d.Note ) ) );
    Assert.Contains( "isn't", json );
  }
}
