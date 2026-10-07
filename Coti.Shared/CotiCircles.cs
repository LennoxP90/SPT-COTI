using System;

namespace Coti.Shared
{
  /// <summary>One thermal circle on screen: centre in viewport space (v up), radius and feather in screen heights.</summary>
  public readonly struct CotiCircle
  {
    public CotiCircle( float u, float v, float radius, float feather )
    {
      U = u;
      V = v;
      Radius = radius;
      Feather = feather;
    }

    public float U { get; }
    public float V { get; }
    public float Radius { get; }
    public float Feather { get; }
  }

  public static class CotiCircles
  {
    /// <summary>
    /// Layout offsets are screen heights, so dividing the horizontal one by the aspect keeps the circle in Borkel's hole
    /// on any screen shape.
    /// </summary>
    public static CotiCircle FromLayout( CotiLayoutTube tube, float aspect )
    {
      return new CotiCircle( 0.5f + tube.Dx / aspect, 0.5f + tube.Dy, tube.Radius, CotiLayouts.Feather );
    }

    /// <summary>A v1 device's mask block, straight through, as 3.2.0 builds its mask.</summary>
    public static CotiCircle FromMask( CotiMaskBlock mask )
    {
      return new CotiCircle( mask.CenterX, mask.CenterY, mask.Radius, mask.Feather );
    }

    /// <summary>
    /// The tube's 16:9 circle as the device files carry it, centre to 4 decimals and radius to 5: the mask a release
    /// before 3.3.0 reads from a multi-tube file.
    /// </summary>
    public static CotiMaskBlock LegacyMask( CotiLayoutTube tube )
    {
      var circle = FromLayout( tube, 16f / 9f );
      return new CotiMaskBlock
      {
        CenterX = Round( circle.U, 4 ),
        CenterY = Round( circle.V, 4 ),
        Radius = Round( circle.Radius, 5 ),
        Feather = CotiLayouts.Feather,
      };
    }

    // On the double, as CotiMountRounding does: rounding the float decides the halfway case in binary.
    private static float Round( float value, int decimals )
    {
      return (float)Math.Round( (double)value, decimals );
    }
  }
}
