using System;
using System.Collections.Generic;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Runs a patch body so a fault in it stays ours. These sit on methods EFT relies on: a throw out
  /// of the AttachMods prefix costs the player every mod on that item, not just the COTI.
  ///
  /// Reported once per site, since they run per item view and per frame.
  /// </summary>
  internal static class CotiPatchGuard
  {
    private static readonly HashSet<string> Reported = new HashSet<string>();

    public static void Run( string site, Action body )
    {
      try
      {
        body();
      }
      catch( Exception ex )
      {
        Report( site, ex );
      }
    }

    /// <summary>
    /// For a prefix that decides whether the original method runs. <paramref name="onFailure"/> is
    /// what to return when the body throws, and should generally be the answer that leaves the game
    /// behaving as though this mod were not installed.
    /// </summary>
    public static bool Run( string site, Func<bool> body, bool onFailure )
    {
      try
      {
        return body();
      }
      catch( Exception ex )
      {
        Report( site, ex );
        return onFailure;
      }
    }

    private static void Report( string site, Exception ex )
    {
      lock( Reported )
      {
        if( !Reported.Add( site ) )
          return;
      }

      // Plugin.Log is null if this somehow runs before the plugin awakes, and a throw from the
      // reporter would defeat the whole point of the guard.
      try
      {
        Plugin.Log?.LogError( $"[COTI] {site} failed and was suppressed - further failures at this " +
                             $"site will not be reported: {ex}" );
      }
      catch( Exception )
      {
      }
    }
  }
}
