namespace Coti.Client
{
  /// <summary>
  /// Keeps the overlay's look fixed when the sensor's resolution changes.
  ///
  /// <c>_OutlineWidth</c> is specified in texels, so its apparent thickness halves every time the
  /// target's resolution doubles. Scaling it keeps contour thickness constant when the resolution
  /// is raised to recover distant contacts.
  /// </summary>
  public static class CotiOverlayScale
  {
    /// <summary>
    /// The resolution the configured width was tuned against. At this resolution the scale is
    /// exactly 1.
    /// </summary>
    public const int ReferenceRows = 576;

    /// <summary>
    /// Values at or above this are diagnostic bands rather than widths: the shader keys debug output off
    /// <c>_OutlineWidth</c> above 600. Scaling one would select a different band.
    /// </summary>
    public const float DiagnosticFloor = 100f;

    /// <summary>
    /// A rim thinner than one texel is no rim: the erosion taps land back inside the shaded texel,
    /// <c>inner</c> converges on <c>solid</c>, and contour mode renders nothing.
    /// </summary>
    public const float MinimumTexels = 1f;

    /// <summary>
    /// Texels per metre of world at one metre from a camera of this vertical field of view drawing into this many
    /// rows. Divided by a surface's distance, it turns an outline's thickness on the object into texels, so the line
    /// shrinks with the object as it recedes and grows with it under magnification. Zero without a usable view.
    /// </summary>
    public static float PixelsPerMetre( int rows, float verticalFovDegrees )
    {
      if( rows <= 0 || verticalFovDegrees <= 0f || verticalFovDegrees >= 180f )
        return 0f;
      return rows / ( 2f * (float)System.Math.Tan( verticalFovDegrees * System.Math.PI / 360.0 ) );
    }

    /// <summary>
    /// The outline width to hand the shader for a target of <paramref name="rows"/> rows. A
    /// non-positive row count leaves the configured value alone.
    /// </summary>
    public static float OutlineWidth( float configured, int rows )
    {
      if( configured >= DiagnosticFloor )
        return configured;

      var scaled = rows <= 0 ? configured : configured * rows / ReferenceRows;
      return scaled < MinimumTexels ? MinimumTexels : scaled;
    }

    /// <summary>
    /// How many of a target's texels make one pixel where it is shown: the 1x picture spans the screen's height, the
    /// magnified one only the lens's. 1 without a usable size.
    /// </summary>
    public static float TexelsPerPixel( int rows, float screenRows )
    {
      return rows > 0 && screenRows > 0f ? rows / screenRows : 1f;
    }

    /// <summary>
    /// The perspective outline's limits in the target's texels, set in screen pixels so they mean the same on any
    /// target: at least one screen pixel, so a far line never vanishes, and at most the configured width scaled to
    /// this screen and multiplied by the zoom, so magnification thickens a near line as it does the object. At 1x the
    /// cap is <see cref="OutlineWidth"/>'s value; a diagnostic width passes through.
    /// </summary>
    public static void OutlineRange(
        float configured, float screenRows, float zoom, float texelsPerPixel, out float min, out float max )
    {
      min = texelsPerPixel > MinimumTexels ? texelsPerPixel : MinimumTexels;
      if( configured >= DiagnosticFloor )
      {
        max = configured;
        return;
      }

      var cap = configured * screenRows / ReferenceRows * ( zoom > 1f ? zoom : 1f ) * texelsPerPixel;
      max = cap > min ? cap : min;
    }
  }
}
