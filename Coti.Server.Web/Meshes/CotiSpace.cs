using System.Numerics;

namespace Coti.Server.Web.Meshes;

/// <summary>
/// The one map out of a bundle's Unity prefab space, which is what EFT resolves every mesh,
/// bone and mount into. Unity is left handed with +Z forward, glTF and three are right handed
/// with +Z back, so z negates and the winding reverses with it.
///
/// Must stay identical to toThreeVec and toThreeQuat in cotiViewer.js. Geometry, bones and
/// mount values only agree if one map took them all there.
/// </summary>
internal static class CotiSpace
{
    public static Vector3 ToViewer(Vector3 v) => new(v.X, v.Y, -v.Z);

    public static Quaternion ToViewer(Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);
}
