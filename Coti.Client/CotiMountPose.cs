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
    public static void Apply( Transform bone, CotiNvgHostConfig host )
    {
      Apply( bone, host, Vector3.zero, Vector3.zero, 0f );
    }

    public static void Apply( Transform bone, CotiNvgHostConfig host, Vector3 positionDelta, Vector3 rotationDelta, float scaleDelta )
    {
      if( bone == null )
        return;

      var pose = CotiMountTransform.Compute(
          host?.ToMountBlock(),
          new CotiVec3( positionDelta.x, positionDelta.y, positionDelta.z ),
          new CotiVec3( rotationDelta.x, rotationDelta.y, rotationDelta.z ),
          scaleDelta );

      bone.localPosition = new Vector3( pose.Position.X, pose.Position.Y, pose.Position.Z );
      bone.localRotation = new Quaternion( pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W );
      bone.localScale = new Vector3( pose.Scale, pose.Scale, pose.Scale );
    }
  }
}
