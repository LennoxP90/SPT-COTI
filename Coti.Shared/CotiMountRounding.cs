using System;

namespace Coti.Shared
{
  /// <summary>
  /// Rounds a mount to the precision the device files carry. Nudging accumulates float error, and
  /// these files are hand-edited - noise like 0.007000001 hides real changes in a diff.
  ///
  /// Position keeps four decimals, finer than the editor's smallest step, so rounding cannot
  /// discard a nudge.
  /// </summary>
  public static class CotiMountRounding
  {
    public const int PositionDecimals = 4;
    public const int AngleDecimals = 2;
    public const int ScaleDecimals = 4;

    /// <summary>
    /// Returns a NEW block. Never rounds in place: the caller may be holding the live table's own
    /// mount, and a publish must not quietly rewrite what the client is currently mounting from.
    /// </summary>
    public static CotiMountBlock Round( CotiMountBlock mount )
    {
      if( mount == null )
        throw new ArgumentNullException( nameof( mount ) );

      return new CotiMountBlock
      {
        AnchorBone = mount.AnchorBone,

        PositionX = To( mount.PositionX, PositionDecimals ),
        PositionY = To( mount.PositionY, PositionDecimals ),
        PositionZ = To( mount.PositionZ, PositionDecimals ),

        RotationX = To( mount.RotationX, AngleDecimals ),
        RotationY = To( mount.RotationY, AngleDecimals ),
        RotationZ = To( mount.RotationZ, AngleDecimals ),

        RollDegrees = To( mount.RollDegrees, AngleDecimals ),
        PitchDegrees = To( mount.PitchDegrees, AngleDecimals ),
        YawDegrees = To( mount.YawDegrees, AngleDecimals ),

        Scale = To( mount.Scale, ScaleDecimals ),
      };
    }

    // Math.Round on the double, cast back once. Rounding the float directly reintroduces the very
    // representation error being removed, because the halfway case is decided in binary.
    private static float To( float value, int decimals )
    {
      return (float)Math.Round( (double)value, decimals );
    }
  }
}
