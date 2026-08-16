using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Places the COTI on its host's mount bone. The delta overload is the one composition point for
  /// config and dev tuning alike - applying them separately previously let a pooled rebuild keep a
  /// position nudge and drop the matching rotation nudge.
  /// </summary>
  public static class CotiMountPose
  {
    public static void Apply( Transform bone, CotiNvgHostConfig host )
    {
      Apply( bone, host, Vector3.zero, Vector3.zero );
    }

    public static void Apply( Transform bone, CotiNvgHostConfig host, Vector3 positionDelta, Vector3 rotationDelta )
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
