namespace Coti.Client
{
  /// <summary>
  /// Decides whether the magnified path should run this frame, and where the lens sits on screen.
  ///
  /// Pure and over primitives: everything else in this feature needs a running game, so this is the
  /// only part that can be tested outside it.
  /// </summary>
  public static class CotiOpticFusion
  {
    /// <summary>
    /// Below this the optic magnifies too little to be worth a second scene render. A variable
    /// scope at its bottom stop is about 1.32 (35 main against 26.5 optic), so it stays above.
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
    /// Whether the live optic camera is the sight the player looks through. A dual sight with both its red dot and its
    /// scope in one mode (the ADO P4) keeps the scope's camera running while aiming through the dot, so only the aim
    /// bone tells them apart: the game's own SightNBone.BoneRelatesToOptics. With no aim bone known, the camera stands.
    /// </summary>
    public static bool AimedThroughOptic( bool aimBoneKnown, bool aimBoneIsOptic )
    {
      return !aimBoneKnown || aimBoneIsOptic;
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

  }
}
