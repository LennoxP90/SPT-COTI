using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// Which of a model's renderers to leave out of a silhouette drawn from its meshes: an LOD group switches levels by
  /// culling, not by disabling renderers, so every level would otherwise be drawn at once and a coarse level that sticks
  /// out past the detailed one would set the outline. First person always shows level 0.
  /// </summary>
  public static class CotiLodCut
  {
    /// <summary>
    /// The items found only in a level above 0 of some LOD group, given every (level, item) of every group. An item that
    /// is level 0 anywhere stays; items in no group never appear here, so the caller keeps them.
    /// </summary>
    public static HashSet<T> CoarseOnly<T>( IEnumerable<(int Level, T Item)> entries ) where T : class
    {
      var coarse = new HashSet<T>();
      var fine = new HashSet<T>();
      foreach( var (level, item) in entries )
      {
        if( item == null )
          continue;

        if( level == 0 )
          fine.Add( item );
        else
          coarse.Add( item );
      }

      coarse.ExceptWith( fine );
      return coarse;
    }
  }
}
