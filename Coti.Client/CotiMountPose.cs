using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Applies the mount pose in one pass, config and tuning together, so a pooled rebuild cannot
  /// keep half of it.
  /// </summary>
  public static class CotiMountPose
  {
    /// <summary>
    /// Below this the model is small enough to read as missing rather than small, which during
    /// tuning is indistinguishable from the mod having broken.
    /// </summary>
    private const float MinimumScale = 0.1f;

    public static void Apply( Transform bone, CotiNvgHostConfig host )
    {
      Apply( bone, host, Vector3.zero, Vector3.zero, 0f );
    }

    public static void Apply( Transform bone, CotiNvgHostConfig host, Vector3 positionDelta, Vector3 rotationDelta, float scaleDelta )
    {
      if( bone == null )
        return;

      var position = Vector3.zero;
      var basis = Quaternion.identity;
      var yaw = 0f;
      var pitch = 0f;
      var roll = 0f;
      var scale = 1f;

      if( host != null )
      {
        position = new Vector3( host.MountPositionX, host.MountPositionY, host.MountPositionZ );
        basis = Quaternion.Euler( host.MountRotationX, host.MountRotationY, host.MountRotationZ );
        yaw = host.MountYawDegrees;
        pitch = host.MountPitchDegrees;
        roll = host.MountRollDegrees;

        // A host entry predating these fields deserialises MountScale to 0, and a zero scale
        // collapses the model to nothing - indistinguishable from the mod being broken.
        scale = host.MountScale > 0f ? host.MountScale : 1f;
      }

      scale = Mathf.Max( scale + scaleDelta, MinimumScale );

      bone.localPosition = position + positionDelta;
      bone.localRotation =
          Quaternion.AngleAxis( yaw + rotationDelta.y, Vector3.up ) *
          Quaternion.AngleAxis( pitch + rotationDelta.x, Vector3.right ) *
          Quaternion.AngleAxis( roll + rotationDelta.z, Vector3.forward ) *
          basis;
      bone.localScale = new Vector3( scale, scale, scale );
    }
  }
}
