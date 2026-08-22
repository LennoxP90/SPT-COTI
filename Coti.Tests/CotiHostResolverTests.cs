using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

public class CotiHostResolverTests
{
    private sealed class FakeItems : ICotiItemView
    {
        public readonly Dictionary<string, (string? prefab, string? parent)> Items = new();
        public bool Exists(string id) => Items.ContainsKey(id);
        public string? PrefabPath(string id) => Items.TryGetValue(id, out var v) ? v.prefab : null;
        public string? ParentOf(string id) => Items.TryGetValue(id, out var v) ? v.parent : null;
        public IEnumerable<string> AllIds() => Items.Keys;
    }

    private static CotiMergeResult Merged(params CotiDeviceFile[] devices)
    {
        var r = new CotiMergeResult();
        r.Devices.AddRange(devices);
        return r;
    }

    private static CotiDeviceFile Device(string name, string? requires, params CotiHostRef[] hosts)
    {
        var d = new CotiDeviceFile
        {
            Schema = 1, Device = name, DisplayName = name, Tuned = true, Requires = requires,
            Mask = new CotiMaskBlock { Radius = 0.27f },
        };
        d.Hosts.AddRange(hosts);
        return d;
    }

    [Fact]
    public void AnIdThatExistsResolvesDirectly()
    {
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(Merged(Device("chimera", null, new CotiHostRef { Id = "111" })),
                                        items, new HashSet<string>());

        Assert.True(r.ByHostId.ContainsKey("111"));
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void AMovedIdIsRefoundByPrefabPath()
    {
        // The whole point: the host mod renumbered its items, so the addon's id is stale but
        // the MESH is the same, and the pose is a function of the mesh.
        var items = new FakeItems();
        items.Items["999"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null, new CotiHostRef { Id = "111", Prefab = "chimera.bundle" })),
            items, new HashSet<string>());

        Assert.True(r.ByHostId.ContainsKey("999"));
        Assert.False(r.ByHostId.ContainsKey("111"));
        Assert.Contains(r.Warnings, w => w.Contains("111") && w.Contains("999"));
    }

    [Fact]
    public void OneDeviceCannotStealAHostAnotherHasAlreadyResolved()
    {
        // CotiDeviceMerge's duplicate-host guard compares DECLARED ids only, so it cannot see a
        // prefab fallback landing on an id another device declared outright. Without an occupancy
        // check the later claim silently wins and the host mounts with the wrong pose and mask.
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var owner = Device("chimera", null, new CotiHostRef { Id = "111" });
        var thief = Device("knockoff", null, new CotiHostRef { Id = "222", Prefab = "chimera.bundle" });

        var r = CotiHostResolver.Resolve(Merged(owner, thief), items, new HashSet<string>());

        Assert.Same(owner, r.ByHostId["111"].Device);
        Assert.Single(r.Devices);
        Assert.Contains(r.Warnings,
            w => w.Contains("knockoff") && w.Contains("111") && w.Contains("chimera"));
    }

    [Fact]
    public void ARefusedHostIsNotPlacedOnTheWireByTheDeviceThatLostIt()
    {
        // Here the loser loses on the EXACT-id path, so its own declared id IS the contested one.
        // Emitting it anyway would hand the client the same key twice and let the losing device's
        // pose and mask win there, while the server had fitted the slot for the winner - the
        // occupancy guard's own failure mode, leaking through the wire instead of the table.
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");
        items.Items["444"] = ("other.bundle", "nv");

        var winner = Device("a_chimera", null, new CotiHostRef { Id = "222", Prefab = "chimera.bundle" });
        var loser = Device("b_other", null,
            new CotiHostRef { Id = "111" }, new CotiHostRef { Id = "444" });

        var r = CotiHostResolver.Resolve(Merged(winner, loser), items, new HashSet<string>());

        Assert.Same(winner, r.ByHostId["111"].Device);

        var loserWire = r.ResolvedDevices.Single(d => d.Device == "b_other").Hosts;
        Assert.Equal(new[] { "444" }, loserWire.Select(h => h.Id));

        var everyWireId = r.ResolvedDevices.SelectMany(d => d.Hosts).Select(h => h.Id).ToList();
        Assert.Equal(everyWireId.Count, everyWireId.Distinct().Count());
    }

    [Fact]
    public void TheWireCarriesTheResolvedIdWhileTheFileKeepsTheDeclaredOne()
    {
        // The whole point of the two lists: the client keys its config, its slot patch and its
        // inspect gate on what it is handed, so it must be handed the id the server FITTED - while
        // the device file itself is never rewritten behind the addon author's back.
        var items = new FakeItems();
        items.Items["999"] = ("chimera.bundle", "nv");

        var declared = new CotiHostRef { Id = "111", Prefab = "chimera.bundle", Label = "Tan" };

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null, declared)), items, new HashSet<string>());

        Assert.Equal("111", r.Devices.Single().Hosts.Single().Id);

        var wire = r.ResolvedDevices.Single().Hosts.Single();
        Assert.Equal("999", wire.Id);
        Assert.Equal("chimera.bundle", wire.Prefab);
        Assert.Equal("Tan", wire.Label);
    }

    [Fact]
    public void TheWireKeepsTheDeclaredIdOfAHostThatResolvedToNothing()
    {
        // Dropping it would truncate the author's host list the first time anyone published a
        // pose for a device whose other variants are not installed - Publish writes this list back.
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null,
                new CotiHostRef { Id = "111" },
                new CotiHostRef { Id = "222", Label = "MultiCam" })),
            items, new HashSet<string>());

        var wire = r.ResolvedDevices.Single().Hosts;
        Assert.Equal(new[] { "111", "222" }, wire.Select(h => h.Id));
        Assert.Equal("MultiCam", wire[1].Label);
    }

    [Fact]
    public void ADeviceThatResolvedNothingIsOnNeitherList()
    {
        var r = CotiHostResolver.Resolve(
            Merged(Device("pvs31a", null, new CotiHostRef { Id = "111" })),
            new FakeItems(), new HashSet<string>());

        Assert.Empty(r.Devices);
        Assert.Empty(r.ResolvedDevices);
    }

    [Fact]
    public void TwoPrefabMatchesAreAmbiguousAndSkipped()
    {
        var items = new FakeItems();
        items.Items["901"] = ("chimera.bundle", "nv");
        items.Items["902"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null, new CotiHostRef { Id = "111", Prefab = "chimera.bundle" })),
            items, new HashSet<string>());

        Assert.Empty(r.ByHostId);
        Assert.Contains(r.Warnings, w => w.Contains("ambiguous"));
    }

    [Fact]
    public void AnEmptyPrefabPathNeverMatches()
    {
        // Every prefab-less item in the database would otherwise collide into one bucket and
        // the first host with a blank prefab would adopt a random item.
        var items = new FakeItems();
        items.Items["901"] = ("", "nv");
        items.Items["902"] = (null, "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("ghost", null, new CotiHostRef { Id = "111", Prefab = "" })),
            items, new HashSet<string>());

        Assert.Empty(r.ByHostId);
    }

    [Fact]
    public void AnItemWithNoPrefabPathNeverEntersTheIndex()
    {
        // The host-level empty-prefab guard short-circuits before the index is built, so this is
        // the only test that reaches BuildPrefabIndex's own exclusion. Without it, removing that
        // exclusion stays green here and throws in production: real items declare no prefab, and
        // a null dictionary key is an ArgumentNullException the first time any device needs the
        // fallback at all.
        var items = new FakeItems();
        items.Items["901"] = (null, "nv");
        items.Items["902"] = ("", "nv");
        items.Items["903"] = ("something_else.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null, new CotiHostRef { Id = "111", Prefab = "chimera.bundle" })),
            items, new HashSet<string>());

        Assert.Empty(r.ByHostId);
        Assert.NotEmpty(r.Notes);
    }

    [Fact]
    public void ANullHostEntrySkipsThatEntryInsteadOfThrowing()
    {
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", null, null!, new CotiHostRef { Id = "111" })),
            items, new HashSet<string>());

        Assert.True(r.ByHostId.ContainsKey("111"));
    }

    [Fact]
    public void AnAbsentHostIsANoteNotAWarning()
    {
        // A supported-but-absent host is the NORMAL case: several hosts come from optional
        // mods. Warning about it makes a healthy install look broken.
        var r = CotiHostResolver.Resolve(
            Merged(Device("pvs31a", null, new CotiHostRef { Id = "111" })),
            new FakeItems(), new HashSet<string>());

        Assert.Empty(r.ByHostId);
        Assert.Empty(r.Warnings);
        Assert.NotEmpty(r.Notes);
    }

    [Fact]
    public void RequiresGateSkipsTheDeviceWhenTheModIsAbsentAndNamesIt()
    {
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", "com.c11.truenorth4", new CotiHostRef { Id = "111" })),
            items, new HashSet<string>());

        Assert.Empty(r.ByHostId);
        Assert.Contains(r.Warnings, w => w.Contains("com.c11.truenorth4"));
    }

    [Fact]
    public void RequiresGatePassesWhenTheModIsLoaded()
    {
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", "com.c11.truenorth4", new CotiHostRef { Id = "111" })),
            items, new HashSet<string> { "com.c11.truenorth4" });

        Assert.True(r.ByHostId.ContainsKey("111"));
    }

    [Fact]
    public void TheRequiresGateIsCaseInsensitive()
    {
        var items = new FakeItems();
        items.Items["111"] = ("chimera.bundle", "nv");

        var r = CotiHostResolver.Resolve(
            Merged(Device("chimera", "COM.C11.TrueNorth4", new CotiHostRef { Id = "111" })),
            items, new HashSet<string> { "com.c11.truenorth4" });

        Assert.True(r.ByHostId.ContainsKey("111"));
    }
}
