using System.Numerics;
using AssetsTools.NET;

namespace Coti.Server.Web.Meshes;

internal static class CotiMeshDecoder
{
    internal sealed class Decoded
    {
        public List<Vector3> Positions { get; } = new();
        public List<int> Triangles { get; } = new();
    }

    public static Decoded? Decode(AssetTypeValueField mesh, IReadOnlyDictionary<string, byte[]> streams)
    {
        if (IsPacked(mesh))
        {
            return null;
        }

        var vertexData = mesh["m_VertexData"];
        var vertexCount = vertexData["m_VertexCount"].AsInt;
        var vertexBytes = ResolveVertexBytes(mesh, vertexData, streams);
        if (vertexCount <= 0 || vertexBytes is null)
        {
            return null;
        }

        var channels = vertexData["m_Channels"]["Array"].Children;
        if (channels.Count == 0 || !IsFloat3(channels[0]))
        {
            return null;
        }

        var position = channels[0];
        var offset = position["offset"].AsByte;
        var stride = StrideOf(channels, position["stream"].AsByte);
        if (stride <= 0)
        {
            return null;
        }

        var decoded = new Decoded();
        for (var i = 0; i < vertexCount; i++)
        {
            var at = i * stride + offset;
            if (at + 12 > vertexBytes.Length)
            {
                return null;
            }

            decoded.Positions.Add(new Vector3(
                BitConverter.ToSingle(vertexBytes, at),
                BitConverter.ToSingle(vertexBytes, at + 4),
                BitConverter.ToSingle(vertexBytes, at + 8)));
        }

        AppendTriangles(mesh, decoded, vertexCount);
        return decoded;
    }

    /// <summary>A quantised bitstream this cannot read. EFT does not ship them.</summary>
    private static bool IsPacked(AssetTypeValueField mesh)
    {
        return mesh["m_CompressedMesh"]["m_Vertices"]["m_NumItems"].AsInt > 0;
    }

    private static bool IsFloat3(AssetTypeValueField channel)
    {
        return channel["format"].AsByte == 0 && (channel["dimension"].AsByte & 0x0F) == 3;
    }

    private static void AppendTriangles(AssetTypeValueField mesh, Decoded decoded, int vertexCount)
    {
        var indexBytes = ByteArrayOf(mesh["m_IndexBuffer"]);
        if (indexBytes is null || indexBytes.Length == 0)
        {
            return;
        }

        var wide = mesh["m_IndexFormat"].AsInt == 1;
        var indexSize = wide ? 4 : 2;

        foreach (var sub in mesh["m_SubMeshes"]["Array"].Children)
        {
            const int triangleTopology = 0;
            if (sub["topology"].AsInt != triangleTopology)
            {
                continue;
            }

            var firstByte = (int)sub["firstByte"].AsUInt;
            var indexCount = (int)sub["indexCount"].AsUInt;
            var baseVertex = (int)sub["baseVertex"].AsUInt;

            for (var i = 0; i + 2 < indexCount; i += 3)
            {
                var at = firstByte + i * indexSize;
                if (at + 3 * indexSize > indexBytes.Length)
                {
                    return;
                }

                var a = ReadIndex(indexBytes, at, wide) + baseVertex;
                var b = ReadIndex(indexBytes, at + indexSize, wide) + baseVertex;
                var c = ReadIndex(indexBytes, at + 2 * indexSize, wide) + baseVertex;

                if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                {
                    continue;
                }

                decoded.Triangles.Add(a);
                decoded.Triangles.Add(b);
                decoded.Triangles.Add(c);
            }
        }
    }

    private static int ReadIndex(byte[] buffer, int at, bool wide)
    {
        return wide ? (int)BitConverter.ToUInt32(buffer, at) : BitConverter.ToUInt16(buffer, at);
    }

    /// <summary>Blobs are the field itself or a vector wrapping "Array"; the wrong one throws.</summary>
    private static byte[]? ByteArrayOf(AssetTypeValueField field)
    {
        if (field is null || field.IsDummy)
        {
            return null;
        }

        if (field.Value?.ValueType == AssetValueType.ByteArray)
        {
            return field.AsByteArray;
        }

        var array = field["Array"];
        return array is not null && !array.IsDummy && array.Value?.ValueType == AssetValueType.ByteArray
            ? array.AsByteArray
            : null;
    }

    private static int StrideOf(List<AssetTypeValueField> channels, byte stream)
    {
        var stride = 0;
        foreach (var c in channels)
        {
            var dimension = c["dimension"].AsByte & 0x0F;
            if (c["stream"].AsByte != stream || dimension == 0)
            {
                continue;
            }

            stride = Math.Max(stride, c["offset"].AsByte + dimension * FormatSize(c["format"].AsByte));
        }

        return stride;
    }

    private static int FormatSize(byte format)
    {
        return format switch
        {
            0 or 10 or 11 => 4,
            1 or 4 or 5 or 8 or 9 => 2,
            2 or 3 or 6 or 7 => 1,
            _ => 4,
        };
    }

    private static byte[]? ResolveVertexBytes(
        AssetTypeValueField mesh, AssetTypeValueField vertexData, IReadOnlyDictionary<string, byte[]> streams)
    {
        var streamData = mesh["m_StreamData"];
        var path = streamData["path"].AsString;

        if (string.IsNullOrEmpty(path))
        {
            return ByteArrayOf(vertexData["m_DataSize"]);
        }

        var wanted = Path.GetFileName(path);
        var key = streams.Keys.FirstOrDefault(
            k => k.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                 || path.EndsWith(k, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            return null;
        }

        var blob = streams[key];
        var offset = (int)streamData["offset"].AsULong;
        var size = (int)streamData["size"].AsULong;
        return offset < 0 || size <= 0 || offset + size > blob.Length
            ? null
            : blob.AsSpan(offset, size).ToArray();
    }
}
