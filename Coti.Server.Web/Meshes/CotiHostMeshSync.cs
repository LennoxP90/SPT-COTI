using System.Reflection;
using System.Text.Json;
using Coti.Server;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Coti.Server.Web.Meshes;

/// <summary>
/// Keeps wwwroot/meshes-auto in step with the hosts that actually resolve. Mod-added hosts get
/// their mesh converted from the mod's own bundle; removing the mod removes the mesh. The
/// shipped meshes folder covers vanilla and is never touched here.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 70)]
public sealed class CotiHostMeshSync(
    ISptLogger<CotiHostMeshSync> logger,
    CotiDeviceStore deviceStore,
    TemplateTable templateTable,
    BundleLoader bundleLoader,
    ModHelper modHelper) : IOnLoad
{
    // Bump whenever a converter change alters what a given bundle produces. The bundles
    // themselves never change, so without this the cache would serve the old output forever.
    private const int ConverterVersion = 5;

    private sealed class Source
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public long Ticks { get; set; }
        public string Slug { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Sync();
        }
        catch (Exception e)
        {
            // a preview asset must never take the server down
            logger.Error($"[COTI] mesh sync failed: {e.Message}");
        }

        return Task.CompletedTask;
    }

    private void Sync()
    {
        var folder = Path.Combine(CotiHostMeshes.MeshRootPath(), CotiHostMeshes.GeneratedFolder);
        Directory.CreateDirectory(folder);

        var sources = ReadSources(folder);
        var index = new Dictionary<string, CotiHostMeshes.MeshEntry>();
        var kept = new Dictionary<string, Source>();
        int converted = 0, unchanged = 0, failed = 0;

        foreach (var (hostId, anchorBone) in HostsNeedingMesh())
        {
            var bundle = BundleFor(hostId);
            if (bundle is null)
            {
                continue;
            }

            var info = new FileInfo(bundle);
            var source = new Source
            {
                Path = bundle,
                Size = info.Length,
                Ticks = info.LastWriteTimeUtc.Ticks,
                Slug = CotiHostMeshBuilder.SlugFor(bundle),
                Version = ConverterVersion,
            };

            if (sources.TryGetValue(hostId, out var was)
                && was.Path == source.Path && was.Size == source.Size && was.Ticks == source.Ticks
                && was.Version == source.Version
                && ReadEntry(folder, hostId) is { } cached
                && FilesPresent(folder, cached))
            {
                index[hostId] = cached;
                kept[hostId] = was;
                unchanged++;
                continue;
            }

            // logged before the work so a wedge names the host that caused it
            logger.Info($"[COTI] mesh: converting '{hostId}' from {Path.GetFileName(bundle)}");

            try
            {
                var entry = CotiHostMeshBuilder.Build(bundle, anchorBone, folder);
                if (entry is null)
                {
                    logger.Warning($"[COTI] mesh: no geometry in {Path.GetFileName(bundle)} for '{hostId}'");
                    failed++;
                    continue;
                }

                index[hostId] = entry;
                kept[hostId] = source;
                converted++;
            }
            catch (Exception e)
            {
                logger.Warning($"[COTI] mesh: '{hostId}' failed - {e.Message}");
                failed++;
            }
        }

        var removed = RemoveOrphans(folder, index);
        WriteIndex(folder, index, kept);
        CotiHostMeshes.Reload();

        logger.Info(
            $"[COTI] meshes: {converted} converted, {unchanged} unchanged, {removed} removed, {failed} failed");
    }

    /// <summary>Resolved hosts that have no shipped mesh, with the flip bone their device names.</summary>
    private IEnumerable<(string HostId, string? AnchorBone)> HostsNeedingMesh()
    {
        var seen = new HashSet<string>();
        foreach (var device in deviceStore.Current.ResolvedDevices)
        {
            foreach (var host in device.Hosts)
            {
                if (host?.Id is null || !seen.Add(host.Id) || CotiHostMeshes.HasShippedMesh(host.Id))
                {
                    continue;
                }

                yield return (host.Id, device.Mount?.AnchorBone);
            }
        }
    }

    /// <summary>The item's own prefab path names its bundle, wherever the mod keeps it.</summary>
    private string? BundleFor(string hostId)
    {
        if (!templateTable.Items.TryGetValue(new MongoId(hostId), out var item))
        {
            return null;
        }

        var prefab = item.Properties?.Prefab?.Path;
        if (string.IsNullOrWhiteSpace(prefab))
        {
            return null;
        }

        return FromManifest(prefab) ?? FromModFolders(prefab);
    }

    /// <summary>SPT's own registry, which names the owning mod outright. Populated well before us.</summary>
    private string? FromManifest(string prefab)
    {
        var info = bundleLoader.GetBundle(prefab);
        if (info is null)
        {
            return null;
        }

        var path = Path.Combine(Directory.GetCurrentDirectory(), info.ModPath, "bundles", info.FileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Mods ship bundles their manifest never lists, so a miss above is not the answer.</summary>
    private string? FromModFolders(string prefab)
    {
        var relative = prefab.Replace('/', Path.DirectorySeparatorChar);
        var modsRoot = Directory.GetParent(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()))?.FullName;
        if (modsRoot is null)
        {
            return null;
        }

        foreach (var mod in Directory.EnumerateDirectories(modsRoot))
        {
            var candidate = Path.Combine(mod, "bundles", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool FilesPresent(string folder, CotiHostMeshes.MeshEntry entry)
    {
        return CotiHostMeshBuilder.FilesFor(entry).All(f => File.Exists(Path.Combine(folder, f)));
    }

    private static CotiHostMeshes.MeshEntry? ReadEntry(string folder, string hostId)
    {
        var index = ReadIndex(folder);
        return index.TryGetValue(hostId, out var entry) ? entry : null;
    }

    private static Dictionary<string, CotiHostMeshes.MeshEntry> ReadIndex(string folder)
    {
        var path = Path.Combine(folder, "index.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, CotiHostMeshes.MeshEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CotiHostMeshes.MeshEntry>>(
                File.ReadAllText(path)) ?? new Dictionary<string, CotiHostMeshes.MeshEntry>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, CotiHostMeshes.MeshEntry>();
        }
    }

    private static Dictionary<string, Source> ReadSources(string folder)
    {
        var path = Path.Combine(folder, "sources.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, Source>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Source>>(File.ReadAllText(path))
                   ?? new Dictionary<string, Source>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, Source>();
        }
    }

    /// <summary>Deletes glb files this folder no longer has an entry for. Generated folder only.</summary>
    private static int RemoveOrphans(string folder, Dictionary<string, CotiHostMeshes.MeshEntry> index)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in index.Values)
        {
            foreach (var file in CotiHostMeshBuilder.FilesFor(entry))
            {
                wanted.Add(file);
            }
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.glb"))
        {
            if (wanted.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            File.Delete(file);
            removed++;
        }

        return removed;
    }

    private static void WriteIndex(
        string folder, Dictionary<string, CotiHostMeshes.MeshEntry> index, Dictionary<string, Source> sources)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(folder, "index.json"), JsonSerializer.Serialize(index, options));
        File.WriteAllText(Path.Combine(folder, "sources.json"), JsonSerializer.Serialize(sources, options));
    }
}
