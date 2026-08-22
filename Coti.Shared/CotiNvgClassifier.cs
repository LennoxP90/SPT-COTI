using System.Collections.Generic;

namespace Coti.Shared
{
  public static class CotiNvgClassifier
  {
    /// <summary>
    /// The NightVision Node in templates/items.json. Every vanilla NVG and every modded clone
    /// hangs off it. Thermals live elsewhere, so they need no exclusion rule.
    /// </summary>
    public const string NightVisionNodeId = "5a2c3a9486f774688b05e574";

    /// <summary>
    /// Walks the parent chain rather than comparing one level, so a mod that interposes its own
    /// node under NightVision is still classified.
    ///
    /// The visited set is not defensive padding: circular parent data hangs the load, and a
    /// hung load is indistinguishable from a crashed server.
    /// </summary>
    public static bool IsNightVision( ICotiItemView items, string id )
    {
      if( items == null || string.IsNullOrEmpty( id ) )
        return false;

      var visited = new HashSet<string>();
      var current = id;

      while( !string.IsNullOrEmpty( current ) && visited.Add( current ) )
      {
        var parent = items.ParentOf( current );
        if( parent == NightVisionNodeId )
          return true;
        current = parent;
      }

      return false;
    }
  }
}
