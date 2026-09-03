using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coti.Server.Web;

/// <summary>
/// Which glTF file a host uses, and the world transform of every bone a mount can anchor to.
/// Written by scripts/export-host-meshes.py and read once.
///
/// Two folders, merged. "meshes" ships with the mod and covers the vanilla hosts. "meshes-local"
/// is generated per install for mod-added hosts, and is neither committed nor shipped. A local
/// entry wins. A host with no entry in either has no preview.
/// </summary>
public static class CotiHostMeshes
{
  public const string ShippedFolder = "meshes";
  public const string LocalFolder = "meshes-local";

  private static readonly Lazy<Dictionary<string, MeshEntry>> Entries = new(Load);

  public static bool TryGetSlug(string hostId, out string slug)
  {
    if (Entries.Value.TryGetValue(hostId, out var entry))
    {
      slug = entry.Slug;
      return true;
    }

    slug = string.Empty;
    return false;
  }

  /// <summary>
  /// The transform the goggles flip about, and whether a separate mesh was exported for it.
  /// </summary>
  public static (string? Pivot, bool HasPivotMesh) PivotFor(string hostId)
  {
    return Entries.Value.TryGetValue(hostId, out var entry)
      ? (entry.Pivot, entry.HasPivotMesh)
      : (null, false);
  }

  public static IReadOnlyDictionary<string, Bone> BonesFor(string hostId)
  {
    return Entries.Value.TryGetValue(hostId, out var entry)
      ? entry.Bones
      : new Dictionary<string, Bone>();
  }

  /// <summary>The directory holding a slug's glTF, local first. Empty when neither has it.</summary>
  public static string FolderFor(string slug)
  {
    var root = MeshRoot();

    foreach (var folder in new[] { LocalFolder, ShippedFolder })
    {
      if (File.Exists(Path.Combine(root, folder, slug + ".glb")))
      {
        return Path.Combine(root, folder);
      }
    }

    return string.Empty;
  }

  private static string MeshRoot()
  {
    return Path.Combine(
      Path.GetDirectoryName(typeof(CotiHostMeshes).Assembly.Location) ?? string.Empty, "wwwroot");
  }

  private static Dictionary<string, MeshEntry> Load()
  {
    var merged = new Dictionary<string, MeshEntry>();

    // Shipped first, then local: a local entry replaces a shipped one of the same host.
    foreach (var folder in new[] { ShippedFolder, LocalFolder })
    {
      foreach (var (hostId, entry) in ReadIndex(Path.Combine(MeshRoot(), folder, "index.json")))
      {
        merged[hostId] = entry;
      }
    }

    return merged;
  }

  private static Dictionary<string, MeshEntry> ReadIndex(string path)
  {
    try
    {
      return !File.Exists(path)
        ? new Dictionary<string, MeshEntry>()
        : JsonSerializer.Deserialize<Dictionary<string, MeshEntry>>(File.ReadAllText(path))
          ?? new Dictionary<string, MeshEntry>();
    }
    catch (Exception)
    {
      // A malformed index yields no entries.
      return new Dictionary<string, MeshEntry>();
    }
  }

  public sealed class MeshEntry
  {
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("pivot")]
    public string? Pivot { get; set; }

    [JsonPropertyName("hasPivotMesh")]
    public bool HasPivotMesh { get; set; }

    [JsonPropertyName("bones")]
    public Dictionary<string, Bone> Bones { get; set; } = new();
  }

  /// <summary>World transform in Unity space; the viewer converts.</summary>
  public sealed class Bone
  {
    [JsonPropertyName("pos")]
    public float[] Pos { get; set; } = new float[3];

    [JsonPropertyName("quat")]
    public float[] Quat { get; set; } = { 0f, 0f, 0f, 1f };
  }
}
