using System;

namespace Coti.Shared
{
  /// <summary>
  /// The capsules glass mirrors: a fixed number per body, a fixed number of bodies, and each body's bounding sphere, which
  /// the mirror shader tests first so a ray that misses a body skips all its capsules.
  /// </summary>
  public static class CotiCapsuleBounds
  {
    /// <summary>Capsules per reflected body; the shader walks exactly this many per body.</summary>
    public const int CapsulesPerBody = 36;

    /// <summary>Bodies reflected at once, nearest first.</summary>
    public const int MaxBodies = 10;

    /// <summary>A sphere: centre and radius.</summary>
    public struct Bounds
    {
      public float X, Y, Z, Radius;
    }

    /// <summary>
    /// A sphere holding every capsule whole. <paramref name="ends"/> holds six floats per capsule (one end, then the
    /// other), <paramref name="radii"/> one each. Centred on the box around the ends, so it is never far from tight.
    /// </summary>
    public static Bounds Sphere( float[] ends, float[] radii, int count )
    {
      float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
      float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
      for( var i = 0; i < count * 2; i++ )
      {
        float x = ends[i * 3], y = ends[i * 3 + 1], z = ends[i * 3 + 2];
        minX = Math.Min( minX, x ); maxX = Math.Max( maxX, x );
        minY = Math.Min( minY, y ); maxY = Math.Max( maxY, y );
        minZ = Math.Min( minZ, z ); maxZ = Math.Max( maxZ, z );
      }

      var s = new Bounds { X = ( minX + maxX ) / 2f, Y = ( minY + maxY ) / 2f, Z = ( minZ + maxZ ) / 2f };
      for( var i = 0; i < count * 2; i++ )
      {
        float dx = ends[i * 3] - s.X, dy = ends[i * 3 + 1] - s.Y, dz = ends[i * 3 + 2] - s.Z;
        s.Radius = Math.Max( s.Radius, (float)Math.Sqrt( dx * dx + dy * dy + dz * dz ) + radii[i / 2] );
      }
      return s;
    }
  }
}
