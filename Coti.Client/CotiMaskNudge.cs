using System;
using Coti.Shared;

namespace Coti.Client
{
  /// <summary>
  /// Which of the four mask numbers a keypress moves.
  /// </summary>
  public enum CotiMaskAxis
  {
    CenterX,
    CenterY,
    Radius,
    Feather,
  }

  /// <summary>
  /// The arithmetic behind the mask editor's hotkeys, kept pure so it can be tested without a
  /// game. Uses <see cref="System.Math"/> rather than Unity's Mathf for the same reason
  /// <see cref="CotiOrbitMath"/> does - this file is source-linked into Coti.Tests, which has no
  /// Unity reference.
  ///
  /// Steps are per axis because the four values live on very different scales. A shipped feather
  /// is 0.01 and a shipped radius 0.28, so one shared step would either crawl across the radius
  /// or blow the feather from hard-edged to a smear in two presses.
  /// </summary>
  public static class CotiMaskNudge
  {
    public const float CenterStep = 0.005f;
    public const float RadiusStep = 0.005f;
    public const float FeatherStep = 0.002f;

    /// <summary>
    /// Matches the pose editor's own fine modifier (CotiPoseTuner's FineDivisorAngle) so holding
    /// Shift means the same thing in both windows.
    /// </summary>
    public const float FineDivisor = 5f;

    /// <summary>
    /// Above zero, not at it. CotiDeviceMerge rejects a device whose radius is zero or negative,
    /// so a mask nudged to zero and then published would write a file the server refuses on its
    /// next load - the device would simply stop existing, with the cause three days behind you.
    /// The clamp is what makes that unreachable rather than merely unlikely.
    /// </summary>
    public const float MinRadius = 0.01f;

    public const float MaxRadius = 1f;

    /// <summary>
    /// Zero is allowed and meaningful: MaskGeometry.ComputeCoverage treats a feather of zero as a
    /// hard-edged circle, which is a legitimate look rather than a broken value.
    /// </summary>
    public const float MinFeather = 0f;

    public const float MaxFeather = 0.25f;

    // The centre is normalised across the screen, so outside 0..1 the circle is off-screen
    // entirely and the editor would look broken with nothing to drag it back by.
    public const float MinCenter = 0f;
    public const float MaxCenter = 1f;

    /// <summary>
    /// Shipped device files carry four decimals (0.5361, 0.274), and floats drift over a long
    /// hold, so each step lands back on that grid. Without it a few hundred presses write
    /// 0.28500000000000003 into a hand-editable file.
    /// </summary>
    private const int Decimals = 4;

    /// <summary>
    /// Returns a NEW block with one axis moved. Never mutates the argument: the editor nudges
    /// from the device's saved mask, and mutating in place would rewrite the very state the
    /// on-screen delta is measured against.
    /// </summary>
    public static CotiMaskBlock Nudge( CotiMaskBlock current, CotiMaskAxis axis, int direction, bool fine )
    {
      if( current == null )
        throw new ArgumentNullException( nameof( current ) );

      var next = new CotiMaskBlock
      {
        CenterX = current.CenterX,
        CenterY = current.CenterY,
        Radius = current.Radius,
        Feather = current.Feather,
      };

      var step = StepFor( axis ) * direction;
      if( fine )
        step /= FineDivisor;

      switch( axis )
      {
        case CotiMaskAxis.CenterX:
          next.CenterX = Settle( current.CenterX + step, MinCenter, MaxCenter );
          break;
        case CotiMaskAxis.CenterY:
          next.CenterY = Settle( current.CenterY + step, MinCenter, MaxCenter );
          break;
        case CotiMaskAxis.Radius:
          next.Radius = Settle( current.Radius + step, MinRadius, MaxRadius );
          break;
        case CotiMaskAxis.Feather:
          next.Feather = Settle( current.Feather + step, MinFeather, MaxFeather );
          break;
      }

      return next;
    }

    public static float StepFor( CotiMaskAxis axis )
    {
      switch( axis )
      {
        case CotiMaskAxis.Radius:
          return RadiusStep;
        case CotiMaskAxis.Feather:
          return FeatherStep;
        default:
          return CenterStep;
      }
    }

    // Round first so the value lands on the file's own grid, clamp second so the limit is the
    // last word - rounding a clamped value could step back across the limit it just enforced.
    private static float Settle( float value, float min, float max )
    {
      var rounded = (float)Math.Round( value, Decimals );
      return rounded < min ? min : rounded > max ? max : rounded;
    }
  }
}
