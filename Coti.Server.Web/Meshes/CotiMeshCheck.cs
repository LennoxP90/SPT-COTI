using System.Numerics;
using SharpGLTF.Schema2;

namespace Coti.Server.Web.Meshes;

/// <summary>
/// Reads a written mesh back and places it the way the viewer will, then compares that against
/// the geometry the reader found. A half that lands anywhere but where the source sat draws in
/// the wrong place, and reads as a broken model rather than a broken export.
///
/// Comparing vertices does not catch it: the export was a quarter turn out for a while with
/// every vertex correct, because the node transform was missing.
/// </summary>
internal static class CotiMeshCheck
{
    public const float ToleranceMm = 0.5f;

    /// <summary>How far the written mesh sits from where its source geometry was, in millimetres.</summary>
    public static float MillimetresOff(
        CotiBundleMeshReader.Result read, CotiHostMeshes.MeshEntry entry, string folder)
    {
        var worst = 0f;

        if (read.Static is not null)
        {
            worst = MathF.Max(worst, Delta(
                read.Static.Positions.Select(CotiSpace.ToViewer),
                Rendered(Path.Combine(folder, entry.Slug + ".glb"))));
        }

        if (read.Pivot is not null
            && entry.Pivot is not null
            && read.Bones.TryGetValue(entry.Pivot, out var frame))
        {
            // The reader hands back pivot geometry in the bone's local space; the viewer puts it
            // back with the bone's world transform.
            worst = MathF.Max(worst, Delta(
                read.Pivot.Positions.Select(p => CotiSpace.ToViewer(Vector3.Transform(p, frame.Rot) + frame.Pos)),
                Rendered(Path.Combine(folder, entry.Slug + "_pivot.glb"))
                    .Select(p => Vector3.Transform(p, CotiSpace.ToViewer(frame.Rot)) + CotiSpace.ToViewer(frame.Pos))));
        }

        return worst * 1000f;
    }

    /// <summary>Vertices as GLTFLoader yields them, node transforms included.</summary>
    private static List<Vector3> Rendered(string path)
    {
        var points = new List<Vector3>();
        var model = ModelRoot.Load(path);

        foreach (var node in model.LogicalNodes.Where(n => n.Mesh is not null))
        {
            foreach (var primitive in node.Mesh.Primitives)
            {
                foreach (var vertex in primitive.GetVertexAccessor("POSITION").AsVector3Array())
                {
                    points.Add(Vector3.Transform(vertex, node.WorldMatrix));
                }
            }
        }

        return points;
    }

    /// <summary>Bounds, since writing the mesh welds and reorders its vertices.</summary>
    private static float Delta(IEnumerable<Vector3> expected, IEnumerable<Vector3> actual)
    {
        var (wantMin, wantMax) = Bounds(expected);
        var (gotMin, gotMax) = Bounds(actual);
        return MathF.Max((wantMin - gotMin).Length(), (wantMax - gotMax).Length());
    }

    private static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return (min, max);
    }
}
