using System;

namespace Coti.Client
{
  /// <summary>
  /// The arithmetic behind the pose editor's preview viewport - orbit, zoom and the
  /// frame-the-COTI button - pure and over primitives, same reasoning as CotiTunerStep and
  /// CotiAnchorAdvisor: nothing about a Camera or a RenderTexture can be reasoned about outside a
  /// running game, but a yaw, a pitch, a distance and the trigonometry that turns a bounds size
  /// into a framing distance can, so <see cref="CotiTunerPreview"/> calls into this rather than
  /// doing its own clamping and trig inline. Source-linked into Coti.Tests the same way
  /// CotiAnchorAdvisor.cs is.
  /// </summary>
  public static class CotiOrbitMath
  {
    /// <summary>
    /// Kept short of a full 90 degrees on either side - AT 90 the camera looks straight down (or
    /// up) the yaw axis, where yaw stops doing anything and a small drag can flip the apparent
    /// orbit direction. Staying short of it keeps "drag right, the view turns right" true
    /// everywhere the pitch can reach.
    /// </summary>
    public const float MinPitchDegrees = -85f;
    public const float MaxPitchDegrees = 85f;

    /// <summary>
    /// A sane zoom range in metres: close enough to see a single mount screw, far enough to still
    /// have the whole host in frame. This is the range that lets the viewport go closer than
    /// EFT's own inspect window allows, which is the entire reason it exists.
    /// </summary>
    public const float MinDistanceMetres = 0.02f;
    public const float MaxDistanceMetres = 3f;

    /// <summary>
    /// How much of the vertical field of view a framed bounds should fill - leaves a margin
    /// around the device rather than cropping it edge to edge the instant framing is asked for.
    /// </summary>
    public const float FramingFillFraction = 0.7f;

    public static float ClampPitch( float pitchDegrees )
    {
      return Clamp( pitchDegrees, MinPitchDegrees, MaxPitchDegrees );
    }

    public static float ClampDistance( float distanceMetres )
    {
      return Clamp( distanceMetres, MinDistanceMetres, MaxDistanceMetres );
    }

    /// <summary>
    /// Yaw has no clamp - orbiting all the way around a device is exactly what the drag is for -
    /// but it is wrapped into [0, 360) so the stored value cannot grow without bound over a long
    /// tuning session.
    /// </summary>
    public static float WrapYaw( float yawDegrees )
    {
      var wrapped = yawDegrees % 360f;
      return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    /// <summary>
    /// A drag turned into a new yaw/pitch pair: horizontal movement turns the camera around the
    /// pivot, vertical movement tips it up or down. Routed through <see cref="WrapYaw"/> and
    /// <see cref="ClampPitch"/> here rather than left to the caller, so a drag can never bypass
    /// either limit the way calling them separately might if a caller forgot one.
    /// </summary>
    public static void ApplyDrag(
        float yawDegrees, float pitchDegrees, float dragDeltaX, float dragDeltaY,
        float degreesPerPixel, out float newYawDegrees, out float newPitchDegrees )
    {
      newYawDegrees = WrapYaw( yawDegrees + dragDeltaX * degreesPerPixel );
      // PLUS, not minus: IMGUI reports a positive delta.y for a downward drag, and this must turn the
      // same way EFT's own inspect window does.
      newPitchDegrees = ClampPitch( pitchDegrees + dragDeltaY * degreesPerPixel );
    }

    /// <summary>
    /// A scroll tick turned into a new distance - positive scroll (the usual "away from the
    /// player" direction a wheel reports) zooms in, matching how zoom already feels everywhere
    /// else in this game.
    /// </summary>
    public static float ApplyZoom( float distanceMetres, float scrollDelta, float metresPerScrollUnit )
    {
      return ClampDistance( distanceMetres - scrollDelta * metresPerScrollUnit );
    }

    /// <summary>
    /// How far back a camera needs to be for an object of the given size to fill
    /// <see cref="FramingFillFraction"/> of the frame. Divides by the fraction: filling less of the
    /// frame means standing further away, so multiplying moves the camera the wrong way.
    /// </summary>
    public static float FramingDistance( float boundsSizeMetres, float verticalFieldOfViewDegrees )
    {
      if( boundsSizeMetres <= 0f || verticalFieldOfViewDegrees <= 0f )
        return MinDistanceMetres;

      var halfAngleRadians = verticalFieldOfViewDegrees * 0.5 * ( Math.PI / 180.0 );
      var halfHeight = boundsSizeMetres * 0.5 / FramingFillFraction;
      var distance = halfHeight / Math.Tan( halfAngleRadians );

      return ClampDistance( (float)distance );
    }

    private static float Clamp( float value, float min, float max )
    {
      return value < min ? min : ( value > max ? max : value );
    }
  }
}
