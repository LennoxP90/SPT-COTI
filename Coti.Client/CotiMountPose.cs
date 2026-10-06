using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Applies the mount pose in one pass, config and tuning together, so a pooled rebuild cannot
  /// keep half of it. The maths lives in CotiMountTransform; this only converts to Unity types.
  /// </summary>
  public static class CotiMountPose
  {
    /// <summary>One tube's mount, with no tuning. A null mount leaves the bone on its anchor.</summary>
    public static void Apply( Transform bone, CotiMountBlock mount )
    {
      if( bone == null )
        return;

      Set( bone, CotiMountTransform.Compute( mount ) );
    }

    public static void Apply( Transform bone, CotiNvgHostConfig host, Vector3 positionDelta, Vector3 rotationDelta, float scaleDelta )
    {
      if( bone == null )
        return;

      Set( bone, CotiMountTransform.Compute(
          host?.ToMountBlock(),
          new CotiVec3( positionDelta.x, positionDelta.y, positionDelta.z ),
          new CotiVec3( rotationDelta.x, rotationDelta.y, rotationDelta.z ),
          scaleDelta ) );
    }

    private static void Set( Transform bone, CotiPose pose )
    {
      bone.localPosition = new Vector3( pose.Position.X, pose.Position.Y, pose.Position.Z );
      bone.localRotation = new Quaternion( pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W );
      bone.localScale = new Vector3( pose.Scale, pose.Scale, pose.Scale );
    }
  }
}
