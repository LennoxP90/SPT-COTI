using System;

namespace Coti.Shared
{
  public struct CotiVec3
  {
    public float X;
    public float Y;
    public float Z;

    public CotiVec3( float x, float y, float z )
    {
      X = x;
      Y = y;
      Z = z;
    }
  }

  public struct CotiQuat
  {
    public float X;
    public float Y;
    public float Z;
    public float W;

    public CotiQuat( float x, float y, float z, float w )
    {
      X = x;
      Y = y;
      Z = z;
      W = w;
    }

    public static CotiQuat Identity
    {
      get { return new CotiQuat( 0f, 0f, 0f, 1f ); }
    }
  }

  public struct CotiPose
  {
    public CotiVec3 Position;
    public CotiQuat Rotation;
    public float Scale;
  }

  /// <summary>
  /// Where the ECOTI sits on its host, as a local transform against the anchor. Unity's own
  /// conventions, in plain floats. The client assigns the result to a Transform, the web editor
  /// hands it to Three.js.
  /// </summary>
  public static class CotiMountTransform
  {
    /// <summary>The floor a tuned scale is clamped to.</summary>
    public const float MinimumScale = 0.1f;

    public static CotiPose Compute( CotiMountBlock mount )
    {
      return Compute( mount, default( CotiVec3 ), default( CotiVec3 ), 0f );
    }

    /// <summary>
    /// The deltas are the live tuning offsets. <paramref name="rotationDelta"/> is not in
    /// mount-field order: X drives pitch, Y yaw, Z roll.
    /// </summary>
    public static CotiPose Compute(
        CotiMountBlock mount, CotiVec3 positionDelta, CotiVec3 rotationDelta, float scaleDelta )
    {
      var position = new CotiVec3( positionDelta.X, positionDelta.Y, positionDelta.Z );
      var rotation = CotiQuat.Identity;
      var scale = 1f;

      if( mount != null )
      {
        position.X += mount.PositionX;
        position.Y += mount.PositionY;
        position.Z += mount.PositionZ;

        // A host entry predating the scale field deserialises to 0.
        scale = mount.Scale > 0f ? mount.Scale : 1f;

        rotation = Multiply(
            AngleAxis( mount.YawDegrees + rotationDelta.Y, 0f, 1f, 0f ),
            Multiply(
                AngleAxis( mount.PitchDegrees + rotationDelta.X, 1f, 0f, 0f ),
                Multiply(
                    AngleAxis( mount.RollDegrees + rotationDelta.Z, 0f, 0f, 1f ),
                    Euler( mount.RotationX, mount.RotationY, mount.RotationZ ) ) ) );
      }
      else
      {
        rotation = Multiply(
            AngleAxis( rotationDelta.Y, 0f, 1f, 0f ),
            Multiply(
                AngleAxis( rotationDelta.X, 1f, 0f, 0f ),
                AngleAxis( rotationDelta.Z, 0f, 0f, 1f ) ) );
      }

      return new CotiPose
      {
        Position = position,
        Rotation = rotation,
        Scale = Math.Max( scale + scaleDelta, MinimumScale ),
      };
    }

    /// <summary>
    /// Unity's Quaternion.Euler, which applies Z, then X, then Y.
    /// </summary>
    private static CotiQuat Euler( float x, float y, float z )
    {
      return Multiply(
          AngleAxis( y, 0f, 1f, 0f ),
          Multiply( AngleAxis( x, 1f, 0f, 0f ), AngleAxis( z, 0f, 0f, 1f ) ) );
    }

    private static CotiQuat AngleAxis( float degrees, float axisX, float axisY, float axisZ )
    {
      var half = degrees * (float)( Math.PI / 180.0 ) * 0.5f;
      var sin = (float)Math.Sin( half );

      return new CotiQuat( axisX * sin, axisY * sin, axisZ * sin, (float)Math.Cos( half ) );
    }

    private static CotiQuat Multiply( CotiQuat a, CotiQuat b )
    {
      return new CotiQuat(
          a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
          a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
          a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
          a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z );
    }
  }
}
