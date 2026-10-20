using System;
using Coti.Shared;
using Xunit;

namespace Coti.Tests
{
  public class CotiIconSyncTests
  {
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    [Fact]
    public void PngIsRecognisedByItsSignatureOnly()
    {
      Assert.True( CotiIconSync.IsPng( Png ) );
      Assert.False( CotiIconSync.IsPng( new byte[] { 0x89, 0x50, 0x4E, 0x47 } ) );
      Assert.False( CotiIconSync.IsPng( new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 } ) );
      Assert.False( CotiIconSync.IsPng( null ) );
    }

    [Fact]
    public void ShaIsLowerHex()
    {
      Assert.Equal( "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", CotiIconSync.Sha256Hex( Array.Empty<byte>() ) );
    }

    [Fact]
    public void RendersOnlyWhatTheServerLacks()
    {
      Assert.True( CotiIconSync.ShouldRender( null ) );
      Assert.True( CotiIconSync.ShouldRender( "" ) );
      Assert.False( CotiIconSync.ShouldRender( "ab" ) );
    }

    [Fact]
    public void SendsOnlyMissingOrChangedIcons()
    {
      Assert.True( CotiIconSync.ShouldSend( null, "ab" ) );
      Assert.True( CotiIconSync.ShouldSend( "ab", "cd" ) );
      Assert.False( CotiIconSync.ShouldSend( "AB", "ab" ) );
      Assert.False( CotiIconSync.ShouldSend( null, null ) );
    }

    [Fact]
    public void OneClientHoldsTheLeaseUntilDoneOrExpired()
    {
      var lease = new CotiIconLease();
      var now = new DateTime( 2026, 10, 20, 12, 0, 0, DateTimeKind.Utc );

      var first = lease.TryClaim( now );
      Assert.NotNull( first );
      Assert.Null( lease.TryClaim( now.AddSeconds( 30 ) ) );
      Assert.True( lease.Accepts( first, now.AddSeconds( 60 ) ) );
      Assert.False( lease.Accepts( "other", now.AddSeconds( 60 ) ) );

      // An accepted upload extends the lease, so the holder keeps it past the first two minutes.
      Assert.Null( lease.TryClaim( now.AddSeconds( 150 ) ) );

      lease.Release( first );
      var second = lease.TryClaim( now.AddSeconds( 151 ) );
      Assert.NotNull( second );
      Assert.False( lease.Accepts( first, now.AddSeconds( 152 ) ) );

      // A holder that goes silent loses it.
      Assert.NotNull( lease.TryClaim( now.AddSeconds( 151 ) + CotiIconLease.Length ) );
    }
  }
}
