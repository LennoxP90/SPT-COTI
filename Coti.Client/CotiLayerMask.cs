using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The bitmask arithmetic behind a Renderer-layer union, pure and over primitives so it can be
  /// tested. CotiTunerPreview's Renderer walk needs a running Unity scene; folding the layers it
  /// found into one mask does not.
  /// </summary>
  public static class CotiLayerMask
  {
    /// <summary>
    /// Unions every layer into one mask; duplicates fold harmlessly. An empty set falls back to
    /// <paramref name="fallbackLayer"/> rather than returning zero, since a zero mask culls
    /// everything and gives a black viewport, while the fallback shows whatever the root is on.
    /// </summary>
    public static int FoldLayerMask( IEnumerable<int> layers, int fallbackLayer )
    {
      var mask = 0;

      foreach( var layer in layers )
        mask |= 1 << layer;

      return mask == 0 ? 1 << fallbackLayer : mask;
    }
  }
}
