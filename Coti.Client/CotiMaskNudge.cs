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
  /// game. Uses <see cref="System.Math"/> rather than Unity's Mathf, like
  /// <see cref="CotiOrbitMath"/>, because this file is source-linked into Coti.Tests, which has no
  /// Unity reference.
  ///
  /// Steps are per axis because the four values live on very different scales: a typical feather
  /// is 0.01 and a typical radius 0.28.
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
    /// Above zero because CotiDeviceMerge rejects a device whose radius is zero or negative, so a
    /// published zero radius would make the server drop the device on its next load.
    /// </summary>
    public const float MinRadius = 0.01f;

    public const float MaxRadius = 1f;

    /// <summary>
    /// Zero is allowed: the overlay shader treats a feather of zero as a hard-edged circle.
    /// </summary>
    public const float MinFeather = 0f;

    public const float MaxFeather = 0.25f;

    // The centre is normalised across the screen, so outside 0..1 the circle is off-screen
    // entirely.
    public const float MinCenter = 0f;
    public const float MaxCenter = 1f;

    /// <summary>
    /// Device files carry four decimals (0.5361, 0.274), and floats drift over many steps, so each
    /// step is rounded back onto that grid to keep the hand-editable file clean.
    /// </summary>
    private const int Decimals = 4;

    /// <summary>
    /// Returns a new block with one axis moved. Never mutates the argument, because the on-screen
    /// delta is measured against the device's saved mask.
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

    // Round first, then clamp: rounding a clamped value could cross the limit again.
    private static float Settle( float value, float min, float max )
    {
      var rounded = (float)Math.Round( value, Decimals );
      return rounded < min ? min : rounded > max ? max : rounded;
    }
  }
}
