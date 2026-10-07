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
    /// The visited set stops circular parent data from hanging the server load.
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

    /// <summary>
    /// The same answer as the overload above, for classifying a whole item table. <paramref name="known"/>
    /// carries answers between calls: every node on a walked path gets the walk's answer, so each
    /// interior node is read once and a leaf under a known node costs one parent lookup.
    /// </summary>
    public static bool IsNightVision( ICotiItemView items, string id, Dictionary<string, bool> known )
    {
      if( items == null || string.IsNullOrEmpty( id ) )
        return false;

      if( known.TryGetValue( id, out var result ) )
        return result;

      var parent = items.ParentOf( id );

      // Checked before any memo lookup: the NightVision node itself is memoised false.
      if( parent == NightVisionNodeId )
        return known[id] = true;

      if( string.IsNullOrEmpty( parent ) || parent == id )
        return known[id] = false;

      if( known.TryGetValue( parent, out result ) )
        return known[id] = result;

      var path = new List<string> { id };
      var current = parent;
      result = false;

      while( !string.IsNullOrEmpty( current ) )
      {
        if( known.TryGetValue( current, out result ) )
          break;

        if( path.Contains( current ) )
        {
          result = false;
          break;
        }

        path.Add( current );

        var next = items.ParentOf( current );
        if( next == NightVisionNodeId )
        {
          result = true;
          break;
        }

        current = next;
      }

      foreach( var node in path )
        known[node] = result;

      return result;
    }
  }
}
