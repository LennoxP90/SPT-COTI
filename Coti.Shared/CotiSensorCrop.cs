using System;

namespace Coti.Shared
{
  /// <summary>
  /// A box in viewport space: x across the width and y up from the bottom, both 0 to 1, as the circle mask uses.
  /// </summary>
  public readonly struct CotiCropBox
  {
    public CotiCropBox( float x, float y, float width, float height )
    {
      X = x;
      Y = y;
      Width = width;
      Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public static CotiCropBox Whole => new CotiCropBox( 0f, 0f, 1f, 1f );
  }

  /// <summary>
  /// The 1x thermal only ever shows inside the COTI's circle, so it renders just a box around it: a projection that
  /// maps the box onto the whole target narrows the view, which culls everything outside it, and a target sized to the
  /// box keeps the full frame's texel density in a fraction of the pixels.
  /// </summary>
  public static class CotiSensorCrop
  {
    /// <summary>A little past the circle and its feather, so the edge taps of the outline stay inside the box.</summary>
    public const float Margin = 1.05f;

    /// <summary>
    /// The box around a circle centred at (<paramref name="centerX"/>, <paramref name="centerY"/>) whose radius and
    /// feather are fractions of the screen's height, on a screen <paramref name="aspect"/> wide per unit of height.
    /// </summary>
    public static bool TryBox( float centerX, float centerY, float radius, float feather, float aspect, out CotiCropBox box )
    {
      box = CotiCropBox.Whole;
      if( !( radius > 0f ) || !( aspect > 0f ) || float.IsNaN( centerX ) || float.IsNaN( centerY ) )
        return false;

      var halfHeight = ( radius + Math.Max( 0f, feather ) ) * Margin;
      var halfWidth = halfHeight / aspect;

      var x0 = Clamp01( centerX - halfWidth );
      var x1 = Clamp01( centerX + halfWidth );
      var y0 = Clamp01( centerY - halfHeight );
      var y1 = Clamp01( centerY + halfHeight );
      if( x1 <= x0 || y1 <= y0 )
        return false;

      box = new CotiCropBox( x0, y0, x1 - x0, y1 - y0 );
      return true;
    }

    /// <summary>
    /// Scale and offset applied to clip space after the camera's projection (x' = sx x + tx w, likewise y) so the box
    /// fills the target.
    /// </summary>
    public static void Projection( CotiCropBox box, out float sx, out float sy, out float tx, out float ty )
    {
      sx = 1f / box.Width;
      sy = 1f / box.Height;
      tx = -( 2f * box.X + box.Width - 1f ) * sx;
      ty = -( 2f * box.Y + box.Height - 1f ) * sy;
    }

    /// <summary>The target's size along one axis: the full frame's, scaled to the box, at least 16.</summary>
    public static int Pixels( int full, float fraction )
    {
      return Math.Max( 16, (int)Math.Ceiling( full * fraction - 1e-4 ) );
    }

    private static float Clamp01( float v )
    {
      return v < 0f ? 0f : v > 1f ? 1f : v;
    }
  }
}
