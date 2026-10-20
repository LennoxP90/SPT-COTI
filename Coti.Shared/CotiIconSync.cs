using System;
using System.Security.Cryptography;
using System.Text;

namespace Coti.Shared
{
  /// <summary>
  /// The rules both halves of the icon sync share: the client uploads the game's own cached PNG of an item the ECOTI
  /// pages list, only when the server has none or a different one.
  /// </summary>
  public static class CotiIconSync
  {
    /// <summary>Above the largest icon in a 5,521 file cache (252 KB, a long gun); a ring scope is far smaller.</summary>
    public const int MaxPngBytes = 512 * 1024;

    public static bool IsPng( byte[]? bytes ) =>
        bytes is { Length: >= 8 } && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    public static string Sha256Hex( byte[] bytes )
    {
      using( var sha = SHA256.Create() )
      {
        var hash = sha.ComputeHash( bytes );
        var text = new StringBuilder( hash.Length * 2 );
        foreach( var b in hash )
          text.Append( b.ToString( "x2" ) );
        return text.ToString();
      }
    }

    /// <summary>
    /// Render the icon when the server has none: rendering costs the client a frame or two per item, so an icon the
    /// server already holds is never rendered just to compare it.
    /// </summary>
    public static bool ShouldRender( string? serverSha ) => string.IsNullOrEmpty( serverSha );

    /// <summary>Send a local icon that the server lacks or holds with different bytes.</summary>
    public static bool ShouldSend( string? serverSha, string? localSha ) =>
        !string.IsNullOrEmpty( localSha ) && !string.Equals( serverSha, localSha, StringComparison.OrdinalIgnoreCase );
  }

  /// <summary>
  /// One upload at a time: the first client to claim gets a token, every other gets none until the holder is done or
  /// its lease runs out, so a client that crashes mid-upload blocks nobody for long. Not thread safe; the server locks.
  /// </summary>
  public sealed class CotiIconLease
  {
    public static readonly TimeSpan Length = TimeSpan.FromMinutes( 2 );

    private string? token;
    private DateTime expiresUtc;

    /// <summary>A new token, or null while another client holds an unexpired lease.</summary>
    public string? TryClaim( DateTime nowUtc )
    {
      if( token != null && nowUtc < expiresUtc )
        return null;

      token = Guid.NewGuid().ToString( "N" );
      expiresUtc = nowUtc + Length;
      return token;
    }

    /// <summary>True for the current, unexpired token; each accepted upload extends the lease.</summary>
    public bool Accepts( string? candidate, DateTime nowUtc )
    {
      if( token == null || candidate != token || nowUtc >= expiresUtc )
        return false;

      expiresUtc = nowUtc + Length;
      return true;
    }

    public void Release( string? candidate )
    {
      if( candidate != null && candidate == token )
        token = null;
    }
  }
}
