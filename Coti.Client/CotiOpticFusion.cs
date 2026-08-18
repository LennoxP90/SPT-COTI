namespace Coti.Client
{
  /// <summary>
  /// Decides whether the magnified path should run this frame, and where the lens sits on screen.
  ///
  /// Pure and over primitives: everything else in this feature needs a running game, so this is the
  /// only part that can be reasoned about at a desk.
  /// </summary>
  public static class CotiOpticFusion
  {
    /// <summary>
    /// Below this the optic magnifies too little to be worth a second scene render. A variable
    /// scope at its bottom stop measured 35.00 main against 26.50 optic, a ratio of 1.32.
    /// </summary>
    public const float MinimumMagnification = 1.15f;

    /// <summary>
    /// True when a second thermal render matched to the optic is worth doing.
    ///
    /// <paramref name="opticFieldOfView"/> is zero when there is no optic camera, which covers
    /// hipfire, iron sights and every non-magnified sight - all already correct, because the main
    /// camera's own field of view narrows on aiming and the 1x camera copies it.
    /// </summary>
    public static bool ShouldMagnify(
        bool configEnabled, bool cotiActive, float mainFieldOfView, float opticFieldOfView )
    {
      if( !configEnabled || !cotiActive )
        return false;

      return Magnification( mainFieldOfView, opticFieldOfView ) >= MinimumMagnification;
    }

    /// <summary>
    /// How much larger the optic renders the world than the main camera does. Returns 1 for any
    /// input that cannot produce a sensible ratio, including a camera mid-teardown.
    /// </summary>
    public static float Magnification( float mainFieldOfView, float opticFieldOfView )
    {
      if( mainFieldOfView <= 0f || opticFieldOfView <= 0f )
        return 1f;

      return mainFieldOfView / opticFieldOfView;
    }

    /// <summary>
    /// The widest a lens may credibly be before its measured box is treated as garbage.
    ///
    /// A guard, not a tuned value. The box is used to DELETE part of the overlay, so a projection
    /// that has blown up would switch the thermal off across most of the screen.
    /// </summary>
    public const float MaximumLensExtent = 0.75f;

    /// <summary>
    /// Turns the lens's measured screen box into the ellipse the overlay excludes, in normalised
    /// viewport coordinates.
    ///
    /// The box is axis-aligned in world space around a tilted disc, so the ellipse over-covers the
    /// glass. That is intended - what surrounds the lens is the scope body, which the player cannot
    /// see through either - and <paramref name="scale"/> pulls it back if the spill reaches past it.
    ///
    /// False means no lens to exclude, and every caller must then leave the overlay alone.
    /// </summary>
    public static bool TryLensEllipse(
        float minX, float minY, float maxX, float maxY,
        float pixelWidth, float pixelHeight, float scale,
        out float centreU, out float centreV, out float radiusU, out float radiusV )
    {
      centreU = 0f;
      centreV = 0f;
      radiusU = 0f;
      radiusV = 0f;

      if( pixelWidth <= 0f || pixelHeight <= 0f || scale <= 0f )
        return false;

      var width = maxX - minX;
      var height = maxY - minY;
      if( width <= 0f || height <= 0f )
        return false;

      var extentU = width / pixelWidth;
      var extentV = height / pixelHeight;
      if( extentU > MaximumLensExtent || extentV > MaximumLensExtent )
        return false;

      centreU = ( minX + maxX ) * 0.5f / pixelWidth;
      centreV = ( minY + maxY ) * 0.5f / pixelHeight;
      radiusU = extentU * 0.5f * scale;
      radiusV = extentV * 0.5f * scale;

      // A zero radius is the shader's own "no lens" signal, so say so here rather than handing over
      // a shape that reads as absent anyway.
      return radiusU > 0f && radiusV > 0f;
    }
  }
}
