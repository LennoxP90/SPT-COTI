namespace Coti.Client
{
  /// <summary>
  /// Keeps the overlay's look fixed when the sensor's resolution changes.
  ///
  /// <c>_OutlineWidth</c> is specified in TEXELS, so its apparent thickness halves every time the
  /// target's resolution doubles. Without this, raising the resolution to recover distant contacts
  /// would also thin every contour, and the two changes could not be judged apart.
  /// </summary>
  public static class CotiOverlayScale
  {
    /// <summary>
    /// The resolution the configured width was tuned against. A scale of exactly 1 here is what
    /// makes this invisible to anyone who never touches the resolution.
    /// </summary>
    public const int ReferenceRows = 576;

    /// <summary>
    /// Values at or above this are diagnostic bands, not widths - the shader keys debug output off
    /// <c>_OutlineWidth</c> above 600. Scaling one would select a different band.
    /// </summary>
    public const float DiagnosticFloor = 100f;

    /// <summary>
    /// A rim thinner than one texel is no rim: the erosion taps land back inside the shaded texel,
    /// <c>inner</c> converges on <c>solid</c>, and contour mode renders nothing.
    /// </summary>
    public const float MinimumTexels = 1f;

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
  }
}
