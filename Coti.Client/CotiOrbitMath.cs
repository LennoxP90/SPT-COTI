using System;

namespace Coti.Client
{
  /// <summary>
  /// The arithmetic behind the pose editor's preview viewport - orbit, zoom and the
  /// frame-the-COTI button - kept pure and over primitives so it can be tested outside the game.
  /// <see cref="CotiTunerPreview"/> calls into this rather than clamping inline. Source-linked
  /// into Coti.Tests.
  /// </summary>
  public static class CotiOrbitMath
  {
    /// <summary>
    /// Kept short of 90 degrees on either side. At 90 the camera looks straight down (or up) the
    /// yaw axis, where yaw does nothing and a small drag can flip the apparent orbit direction.
    /// </summary>
    public const float MinPitchDegrees = -85f;
    public const float MaxPitchDegrees = 85f;

    /// <summary>
    /// Zoom range in metres: close enough to see a single mount screw, far enough to have the
    /// whole host in frame. It goes closer than EFT's inspect window allows.
    /// </summary>
    public const float MinDistanceMetres = 0.02f;
    public const float MaxDistanceMetres = 3f;

    /// <summary>
    /// How much of the vertical field of view a framed bounds should fill, leaving a margin
    /// around the device.
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
    /// Yaw has no clamp so the drag can orbit all the way around a device, but it is wrapped into
    /// [0, 360) so the stored value cannot grow without bound.
    /// </summary>
    public static float WrapYaw( float yawDegrees )
    {
      var wrapped = yawDegrees % 360f;
      return wrapped < 0f ? wrapped + 360f : wrapped;
    }

    /// <summary>
    /// A drag turned into a new yaw/pitch pair: horizontal movement turns the camera around the
    /// pivot, vertical movement tips it up or down. Routed through <see cref="WrapYaw"/> and
    /// <see cref="ClampPitch"/> here so a drag can never bypass either limit.
    /// </summary>
    public static void ApplyDrag(
        float yawDegrees, float pitchDegrees, float dragDeltaX, float dragDeltaY,
        float degreesPerPixel, out float newYawDegrees, out float newPitchDegrees )
    {
      newYawDegrees = WrapYaw( yawDegrees + dragDeltaX * degreesPerPixel );
      // Added rather than subtracted: IMGUI reports a positive delta.y for a downward drag, and this
      // must turn the same way EFT's inspect window does.
      newPitchDegrees = ClampPitch( pitchDegrees + dragDeltaY * degreesPerPixel );
    }

    /// <summary>
    /// A scroll tick turned into a new distance. Positive scroll (wheel away from the player)
    /// zooms in, matching the rest of the game.
    /// </summary>
    public static float ApplyZoom( float distanceMetres, float scrollDelta, float metresPerScrollUnit )
    {
      return ClampDistance( distanceMetres - scrollDelta * metresPerScrollUnit );
    }

    /// <summary>
    /// How far back a camera needs to be for an object of the given size to fill
    /// <see cref="FramingFillFraction"/> of the frame. Divides by the fraction, since filling less
    /// of the frame means standing further away.
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
