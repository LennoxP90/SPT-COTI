using System;

namespace Coti.Shared
{
  /// <summary>
  /// Whether a tube's pod is flipped up: its bone's live rotation is more than <see cref="UpDegrees"/> from the down
  /// rotation the device file declares. C11's pods swing 90.5 (Chimera) and 119 (DTNVS) degrees between down and up,
  /// so 45 never misreads.
  /// </summary>
  public static class CotiPodGate
  {
    public const float UpDegrees = 45f;

    /// <summary>The declared down rotation, in Unity Euler degrees as the mount's rotationX/Y/Z are.</summary>
    public static CotiQuat Down( CotiPodBlock pod )
    {
      return CotiMountTransform.Euler( pod.DownX, pod.DownY, pod.DownZ );
    }

    /// <summary>The angle between two rotations; q and -q are the same rotation.</summary>
    public static float AngleDegrees( CotiQuat a, CotiQuat b )
    {
      var dot = Math.Abs( (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W );
      return (float)( 2.0 * Math.Acos( Math.Min( 1.0, dot ) ) * 180.0 / Math.PI );
    }

    public static bool IsUp( CotiQuat live, CotiPodBlock pod )
    {
      return AngleDegrees( live, Down( pod ) ) > UpDegrees;
    }
  }
}
