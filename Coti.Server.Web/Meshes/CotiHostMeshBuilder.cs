using System.Text.RegularExpressions;

namespace Coti.Server.Web.Meshes;

/// <summary>Converts one host's bundle into the glb files and index entry the viewer needs.</summary>
internal static partial class CotiHostMeshBuilder
{
    public static CotiHostMeshes.MeshEntry? Build(string bundlePath, string? anchorBone, string outputDir)
    {
        // Device files write an unset anchor as "", not as an absent key, and an empty bone name
        // matches nothing - so without this the fallback never runs and the host cannot flip.
        var anchor = string.IsNullOrWhiteSpace(anchorBone) ? null : anchorBone;

        // with no tuned pose the flip bone is conventionally "axis"
        var candidate = anchor ?? "axis";
        var read = CotiBundleMeshReader.Read(bundlePath, candidate);
        if (read.Static is null && read.Pivot is null)
        {
            return null;
        }

        var slug = SlugFor(bundlePath);

        if (read.Static is not null)
        {
            CotiGlbWriter.Write(read.Static, Path.Combine(outputDir, slug + ".glb"));
        }

        if (read.Pivot is not null)
        {
            CotiGlbWriter.Write(read.Pivot, Path.Combine(outputDir, slug + "_pivot.glb"));
        }

        var entry = new CotiHostMeshes.MeshEntry
        {
            Slug = slug,
            // the fallback only counts when geometry actually hangs off it
            Pivot = anchor ?? (read.Pivot is not null ? candidate : null),
            HasPivotMesh = read.Pivot is not null,
            HasStaticMesh = read.Static is not null,
            Bones = BonesFor(read),
        };

        var off = CotiMeshCheck.MillimetresOff(read, entry, outputDir);
        if (off > CotiMeshCheck.ToleranceMm)
        {
            throw new InvalidOperationException(
                $"the written mesh sits {off:F1} mm from the source geometry, so it would draw in "
                + "the wrong place");
        }

        return entry;
    }

    public static string SlugFor(string bundlePath)
    {
        var name = Path.GetFileNameWithoutExtension(bundlePath).ToLowerInvariant();
        return UnsafeChars().Replace(name, "_").Trim('_');
    }

    public static IEnumerable<string> FilesFor(CotiHostMeshes.MeshEntry entry)
    {
        if (entry.HasStaticMesh)
        {
            yield return entry.Slug + ".glb";
        }

        if (entry.HasPivotMesh)
        {
            yield return entry.Slug + "_pivot.glb";
        }
    }

    private static Dictionary<string, CotiHostMeshes.Bone> BonesFor(CotiBundleMeshReader.Result read)
    {
        var bones = new Dictionary<string, CotiHostMeshes.Bone>();
        foreach (var (name, (pos, rot)) in read.Bones)
        {
            if (name.StartsWith("point light", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bones[name] = new CotiHostMeshes.Bone
            {
                Pos = [pos.X, pos.Y, pos.Z],
                Quat = [rot.X, rot.Y, rot.Z, rot.W],
            };
        }

        return bones;
    }

    [GeneratedRegex("[^a-z0-9_]+")]
    private static partial Regex UnsafeChars();
}
