using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// Reads the item table without depending on it, so the resolution rules are testable.
  /// The server implements this over the live template table; tests implement it over a
  /// dictionary.
  /// </summary>
  public interface ICotiItemView
  {
    bool Exists( string id );

    /// <summary>Null or empty when the item declares no prefab. Both are non-matching.</summary>
    string? PrefabPath( string id );

    string? ParentOf( string id );

    IEnumerable<string> AllIds();
  }
}
