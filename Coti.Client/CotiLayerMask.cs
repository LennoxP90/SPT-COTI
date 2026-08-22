using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The bitmask arithmetic behind a Renderer-layer union, pure and over primitives - same
  /// reasoning as CotiOrbitMath: CotiTunerPreview's own Renderer walk needs a running Unity scene
  /// and cannot be reasoned about outside one, but folding whatever layers that walk found into one
  /// mask can be, so CotiTunerPreview calls into this rather than folding the bits itself inline.
  /// </summary>
  public static class CotiLayerMask
  {
    /// <summary>
    /// Unions every layer into one mask, OR-ing each `1 << layer` in turn - duplicates fold
    /// harmlessly, since OR is idempotent. An empty set falls back to
    /// <paramref name="fallbackLayer"/> rather than returning zero: a zero mask means "cull
    /// everything," which would guarantee a black viewport even once the right renderers do
    /// appear on a later resolve, whereas the fallback at least shows whatever the root itself is
    /// on.
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
