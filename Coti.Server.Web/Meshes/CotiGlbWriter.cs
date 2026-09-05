using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Coti.Server.Web.Meshes;

/// <summary>
/// Writes reader output as the glTF the viewer loads, in the viewer's own space and on an
/// identity node, so nothing has to cancel anything later.
///
/// The shipped meshes came out of Blender and carry its two halves instead: vertices a quarter
/// turn out with a node rotation that undoes it. Both render the same. CotiMeshCheck measures
/// where a vertex lands, so it holds either to one standard.
/// </summary>
internal static class CotiGlbWriter
{
    public static void Write(CotiBundleMeshReader.Geometry geometry, string path)
    {
        var positions = new Vector3[geometry.Positions.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = CotiSpace.ToViewer(geometry.Positions[i]);
        }

        var triangles = FlipWinding(geometry.Triangles);
        var normals = SmoothNormals(positions, triangles);

        var mesh = new MeshBuilder<VertexPositionNormal>("mesh");
        var primitive = mesh.UsePrimitive(new MaterialBuilder("default"));

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            primitive.AddTriangle(
                Vertex(positions, normals, triangles[i]),
                Vertex(positions, normals, triangles[i + 1]),
                Vertex(positions, normals, triangles[i + 2]));
        }

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        scene.ToGltf2().SaveGLB(path);
    }

    private static VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty> Vertex(
        Vector3[] positions, Vector3[] normals, int index)
    {
        return new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(
            new VertexPositionNormal(positions[index], normals[index]));
    }

    /// <summary>Changing handedness mirrors the mesh, so the triangles reverse to match.</summary>
    private static int[] FlipWinding(List<int> triangles)
    {
        var flipped = new int[triangles.Count];
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            flipped[i] = triangles[i + 2];
            flipped[i + 1] = triangles[i + 1];
            flipped[i + 2] = triangles[i];
        }

        return flipped;
    }

    private static Vector3[] SmoothNormals(Vector3[] positions, int[] triangles)
    {
        var normals = new Vector3[positions.Length];

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i];
            var b = triangles[i + 1];
            var c = triangles[i + 2];

            // unnormalised, so larger faces weigh more, which is what Blender does
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
        }

        return normals;
    }
}
