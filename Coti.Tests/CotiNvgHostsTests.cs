using System.Reflection;
using System.Text.Json;
using Coti.Shared;
using Xunit;

public class CotiNvgHostsTests
{
    /// <summary>
    /// A host with no defaults block still mounts, which is what makes this worth a test: it lands
    /// at the host's root with the fallback mask and reports nothing, so it reads as a broken mod
    /// rather than a missing config. The two halves are edited separately and nothing else pairs
    /// them.
    /// </summary>
    [Fact]
    public void EveryHostHasADefaultsBlock()
    {
        var hosts = ReadDefaultsHosts();

        foreach (var host in CotiNvgHosts.All)
        {
            Assert.True(hosts.TryGetValue(host.TemplateId, out var block),
                $"{host.DisplayName} ({host.TemplateId}) has no block in coti-defaults.json");

            Assert.True(block.TryGetProperty("maskRadius", out var radius) && radius.GetSingle() > 0f,
                $"{host.DisplayName} has a missing or non-positive maskRadius, which generates no mask at all");

            Assert.True(block.TryGetProperty("maskName", out var maskName) && maskName.GetString() == host.MaskName,
                $"{host.DisplayName} is \"{host.MaskName}\" in the host table but " +
                $"\"{(block.TryGetProperty("maskName", out var found) ? found.GetString() : "absent")}\" in the defaults");
        }
    }

    private static Dictionary<string, JsonElement> ReadDefaultsHosts()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // By suffix rather than the full name: the manifest name is derived from the link path and
        // the root namespace, and a rename of either would otherwise fail as "no hosts" instead of
        // "resource missing".
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("coti-defaults.json"));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("nvgHosts")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public void EveryHostHasAUniqueTemplateId()
    {
        var ids = new HashSet<string>();
        foreach (var host in CotiNvgHosts.All)
            Assert.True(ids.Add(host.TemplateId), $"duplicate template id: {host.TemplateId}");
    }

    [Fact]
    public void EveryHostHasAUniqueMaskName()
    {
        var names = new HashSet<string>();
        foreach (var host in CotiNvgHosts.All)
            Assert.True(names.Add(host.MaskName), $"duplicate mask name: {host.MaskName}");
    }

    [Fact]
    public void NoHostFieldIsBlank()
    {
        foreach (var host in CotiNvgHosts.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(host.TemplateId));
            Assert.False(string.IsNullOrWhiteSpace(host.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(host.MaskName));
        }
    }

    [Fact]
    public void SlotNameMatchesTheTransformTheClientCreates()
    {
        // This pins the literal rather than checking the two halves agree - they share the
        // constant, so they cannot disagree. What it guards is a RENAME. The string is written
        // into the saved inventory of every profile carrying a mounted COTI, so changing it
        // orphans them exactly the way changing the template id would. Failing here is the
        // reminder that a rename is a profile migration, not a refactor.
        Assert.Equal("mod_coti", CotiIds.ModSlotName);
    }
}
