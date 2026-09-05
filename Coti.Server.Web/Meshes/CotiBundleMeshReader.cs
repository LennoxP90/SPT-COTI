using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Coti.Server.Web.Meshes;

/// <summary>
/// LOD0 geometry and world bone transforms from a Unity bundle, in Unity space.
/// Mirrors scripts/export-host-meshes.py so a host read here matches one it exported.
/// </summary>
internal static class CotiBundleMeshReader
{
    internal sealed class Geometry
    {
        public List<Vector3> Positions { get; } = new();
        public List<int> Triangles { get; } = new();
    }

    internal sealed class Result
    {
        public Geometry? Static { get; init; }

        /// <summary>Geometry under the pivot, in the pivot's local space.</summary>
        public Geometry? Pivot { get; init; }

        public Dictionary<string, (Vector3 Pos, Quaternion Rot)> Bones { get; init; } = new();
    }

    /// <param name="flatten">Take meshes in raw local space, ignoring the prefab's transforms.</param>
    public static Result Read(string bundlePath, string? pivotName, bool flatten = false)
    {
        var mgr = new AssetsManager();
        try
        {
            var bundle = mgr.LoadBundleFile(bundlePath, true);
            var streams = ReadStreamFiles(bundle);

            var transforms = new Dictionary<long, AssetTypeValueField>();
            var gameObjects = new Dictionary<long, AssetTypeValueField>();
            var meshes = new Dictionary<long, AssetTypeValueField>();
            var meshOfGameObject = new Dictionary<long, long>();

            CollectAssets(mgr, bundle, transforms, gameObjects, meshes, meshOfGameObject);

            var walker = new Walker(transforms, gameObjects, meshes, meshOfGameObject, streams, pivotName, flatten);
            foreach (var (pathId, t) in transforms)
            {
                if (t["m_Father"]["m_PathID"].AsLong == 0)
                {
                    walker.Walk(pathId, Vector3.Zero, Quaternion.Identity, false);
                }
            }

            return walker.ToResult();
        }
        finally
        {
            mgr.UnloadAll();
        }
    }

    private static void CollectAssets(
        AssetsManager mgr,
        BundleFileInstance bundle,
        Dictionary<long, AssetTypeValueField> transforms,
        Dictionary<long, AssetTypeValueField> gameObjects,
        Dictionary<long, AssetTypeValueField> meshes,
        Dictionary<long, long> meshOfGameObject)
    {
        for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
        {
            if (!bundle.file.IsAssetsFile(i))
            {
                continue;
            }

            var afi = mgr.LoadAssetsFileFromBundle(bundle, i, false);
            if (afi is null)
            {
                continue;
            }

            foreach (var info in afi.file.AssetInfos)
            {
                var field = mgr.GetBaseField(afi, info);
                switch ((AssetClassID)info.TypeId)
                {
                    case AssetClassID.Transform:
                        transforms[info.PathId] = field;
                        break;
                    case AssetClassID.GameObject:
                        gameObjects[info.PathId] = field;
                        break;
                    case AssetClassID.Mesh:
                        meshes[info.PathId] = field;
                        break;
                    case AssetClassID.MeshFilter:
                        meshOfGameObject[field["m_GameObject"]["m_PathID"].AsLong] =
                            field["m_Mesh"]["m_PathID"].AsLong;
                        break;
                }
            }
        }
    }

    private static Dictionary<string, byte[]> ReadStreamFiles(BundleFileInstance bundle)
    {
        var streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in bundle.file.BlockAndDirInfo.DirectoryInfos)
        {
            if (!dir.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase)
                && !dir.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = bundle.file.GetFileIndex(dir.Name);
            bundle.file.GetFileRange(index, out var offset, out var length);
            bundle.file.DataReader.Position = offset;
            streams[dir.Name] = bundle.file.DataReader.ReadBytes((int)length);
        }

        return streams;
    }

    private sealed class Walker(
        Dictionary<long, AssetTypeValueField> transforms,
        Dictionary<long, AssetTypeValueField> gameObjects,
        Dictionary<long, AssetTypeValueField> meshes,
        Dictionary<long, long> meshOfGameObject,
        Dictionary<string, byte[]> streams,
        string? pivotName,
        bool flatten)
    {
        private readonly Geometry _static = new();
        private readonly Geometry _pivot = new();
        private readonly Dictionary<string, (Vector3, Quaternion)> _bones = new();
        private (Vector3 Pos, Quaternion Rot)? _pivotFrame;

        public void Walk(long pathId, Vector3 parentPos, Quaternion parentRot, bool underPivot)
        {
            if (!transforms.TryGetValue(pathId, out var t))
            {
                return;
            }

            var lp = t["m_LocalPosition"];
            var lr = t["m_LocalRotation"];
            var localPos = new Vector3(lp["x"].AsFloat, lp["y"].AsFloat, lp["z"].AsFloat);
            var localRot = new Quaternion(lr["x"].AsFloat, lr["y"].AsFloat, lr["z"].AsFloat, lr["w"].AsFloat);

            var worldPos = parentPos + Vector3.Transform(localPos, parentRot);
            var worldRot = Quaternion.Concatenate(localRot, parentRot);

            var goId = t["m_GameObject"]["m_PathID"].AsLong;
            var name = gameObjects.TryGetValue(goId, out var go) ? go["m_Name"].AsString : "?";
            _bones[name] = (worldPos, worldRot);

            if (pivotName is not null && name == pivotName)
            {
                underPivot = true;
                _pivotFrame = (worldPos, worldRot);
            }

            AddMeshFor(goId, name, worldPos, worldRot, underPivot);

            foreach (var child in t["m_Children"]["Array"].Children)
            {
                Walk(child["m_PathID"].AsLong, worldPos, worldRot, underPivot);
            }
        }

        public Result ToResult()
        {
            return new Result
            {
                Static = _static.Positions.Count > 0 ? _static : null,
                Pivot = _pivot.Positions.Count > 0 ? _pivot : null,
                Bones = _bones,
            };
        }

        private void AddMeshFor(long goId, string name, Vector3 worldPos, Quaternion worldRot, bool underPivot)
        {
            if (IsLowerLod(name)
                || !meshOfGameObject.TryGetValue(goId, out var meshId)
                || !meshes.TryGetValue(meshId, out var mesh))
            {
                return;
            }

            var decoded = CotiMeshDecoder.Decode(mesh, streams);
            if (decoded is null || decoded.Positions.Count == 0)
            {
                return;
            }

            var intoPivot = underPivot && _pivotFrame is not null;
            var target = intoPivot ? _pivot : _static;
            var baseIndex = target.Positions.Count;

            foreach (var v in decoded.Positions)
            {
                var p = flatten ? v : Vector3.Transform(v, worldRot) + worldPos;
                if (intoPivot && _pivotFrame is { } frame)
                {
                    p = Vector3.Transform(p - frame.Pos, Quaternion.Inverse(frame.Rot));
                }

                target.Positions.Add(p);
            }

            foreach (var index in decoded.Triangles)
            {
                target.Triangles.Add(index + baseIndex);
            }
        }

        private static bool IsLowerLod(string name)
        {
            var at = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var suffix = name[(at + 4)..];
            return suffix.Length > 0 && suffix.All(char.IsAsciiDigit) && int.Parse(suffix) > 0;
        }
    }
}
