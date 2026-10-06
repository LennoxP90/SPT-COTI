namespace Coti.Shared
{
  /// <summary>
  /// A tube's pose from its partner's, across the goggles' centre line: tube_1 from tube_2, tube_0 from tube_3.
  /// Sideways position and the turns that swing the unit sideways change sign; height, depth, pitch, base X and scale
  /// do not.
  /// </summary>
  public static class CotiTubeMirror
  {
    /// <summary>
    /// A new block. The target keeps its own anchor bone when it has one, because the Chimera's partners hang on
    /// different pods; otherwise it takes the source's.
    /// </summary>
    public static CotiMountBlock Mirror( CotiMountBlock source, string? targetAnchor )
    {
      return new CotiMountBlock
      {
        AnchorBone = string.IsNullOrEmpty( targetAnchor ) ? source.AnchorBone : targetAnchor,
        PositionX = -source.PositionX,
        PositionY = source.PositionY,
        PositionZ = source.PositionZ,
        RotationX = source.RotationX,
        RotationY = -source.RotationY,
        RotationZ = -source.RotationZ,
        RollDegrees = -source.RollDegrees,
        PitchDegrees = source.PitchDegrees,
        YawDegrees = -source.YawDegrees,
        Scale = source.Scale,
      };
    }
  }
}
