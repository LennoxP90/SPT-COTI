using Coti.Shared;
using Xunit;

public class CotiDeviceMergeTests
{
    private static CotiParsedFile File(string path, string device, params string[] hostIds)
    {
        var d = new CotiDeviceFile
        {
            Schema = 1,
            Device = device,
            DisplayName = device,
            Tuned = true,
            Mask = new CotiMaskBlock { CenterX = 0.5f, CenterY = 0.5f, Radius = 0.27f, Feather = 0.01f },
            Mount = new CotiMountBlock { Scale = 1f },
        };
        foreach (var id in hostIds)
            d.Hosts.Add(new CotiHostRef { Id = id });
        return new CotiParsedFile { Path = path, Device = d };
    }

    private static CotiParsedFile Requiring(string path, string device, string requires, params string[] hostIds)
    {
        var f = File(path, device, hostIds);
        f.Device!.Requires = requires;
        return f;
    }

    [Fact]
    public void ADeviceWhoseRequiredModIsAbsentLeavesItsHostsFreeForAnotherFile()
    {
        // The requirement, stated as a consequence rather than as an implementation detail: if a
        // device cannot be used, whatever else covers that host must be free to.
        //
        // "a-gated" sorts BEFORE "z-fallback", so without gating before the host claim the gated
        // device wins the host, the fallback is warned off as a duplicate, and the gate then drops
        // the winner - leaving the host covered by nothing at all. That is exactly what happened
        // in a real session: auto-discovery re-stubbed the host every restart and each reload
        // logged "already belongs to" against a file that was never going to be used.
        var r = CotiDeviceMerge.Merge(
            new[]
            {
                Requiring("a-gated.json", "gated", "com.absent.mod", "H1"),
                File("z-fallback.json", "fallback", "H1"),
            },
            new[] { "com.present.mod" });

        Assert.Single(r.Devices);
        Assert.Equal("fallback", r.Devices[0].Device);
        Assert.Empty(r.Warnings);
        Assert.Contains(r.Notes, n => n.Contains("com.absent.mod"));
    }

    [Fact]
    public void ADeviceWhoseRequiredModIsLoadedKeepsItsHosts()
    {
        var r = CotiDeviceMerge.Merge(
            new[] { Requiring("a.json", "chimera", "com.present.mod", "H1") },
            new[] { "COM.PRESENT.MOD" });   // case-insensitive, as the log prints it either way

        Assert.Single(r.Devices);
        Assert.Equal("chimera", r.Devices[0].Device);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void WithNoLoadedModListNothingIsGated()
    {
        // Merge is called this way by the tests above and by anything that has no server context;
        // a null list must not silently drop every device that declares a requirement.
        var r = CotiDeviceMerge.Merge(new[] { Requiring("a.json", "chimera", "com.absent.mod", "H1") });

        Assert.Single(r.Devices);
        Assert.Empty(r.Notes);
    }

    private static CotiParsedFile Untuned(string path, string device, params string[] hostIds)
    {
        var f = File(path, device, hostIds);
        f.Device!.Tuned = false;
        return f;
    }

    [Fact]
    public void AMeasuredPoseBeatsAnAutoDiscoveredStubWhateverTheFileNames()
    {
        // The scenario this exists for: a player runs with a host mod but no addon, so
        // auto-discovery writes a seeded stub to the folder ROOT. They install the addon later,
        // which lives in a SUBFOLDER. Both files then claim the host.
        //
        // Sorted by path, "nvg_dtnvg.json" comes before "wtt-cag/dtnvs.json" - so ordering by path
        // alone handed the host to the stub and the measured pose was silently ignored.
        var r = CotiDeviceMerge.Merge(new[]
        {
            Untuned("nvg_dtnvg.json", "nvg_dtnvg", "H1"),
            File("wtt-cag/dtnvs.json", "dtnvs", "H1"),
        });

        Assert.Single(r.Devices);
        Assert.Equal("dtnvs", r.Devices[0].Device);
        Assert.True(r.Devices[0].Tuned);
    }

    [Fact]
    public void TheSupersededStubIsANoteNotAWarning()
    {
        // Installing an addon over a stub is the intended workflow, so it must not look like a
        // fault - but it is worth saying, because the stub is now dead weight.
        var r = CotiDeviceMerge.Merge(new[]
        {
            Untuned("nvg_dtnvg.json", "nvg_dtnvg", "H1"),
            File("wtt-cag/dtnvs.json", "dtnvs", "H1"),
        });

        Assert.Empty(r.Warnings);
        Assert.Contains(r.Notes, n => n.Contains("superseded") && n.Contains("nvg_dtnvg.json"));
    }

    [Fact]
    public void TwoTunedDevicesClashingIsStillAWarning()
    {
        // Two measured poses for one host is a real conflict a human has to resolve, and path
        // order still decides it deterministically.
        var r = CotiDeviceMerge.Merge(new[]
        {
            File("z-second.json", "second", "H1"),
            File("a-first.json", "first", "H1"),
        });

        Assert.Single(r.Devices);
        Assert.Equal("first", r.Devices[0].Device);
        Assert.Contains(r.Warnings, w => w.Contains("z-second.json"));
    }

    [Fact]
    public void TwoUntunedStubsStillResolveByPath()
    {
        var r = CotiDeviceMerge.Merge(new[]
        {
            Untuned("z.json", "z", "H1"),
            Untuned("a.json", "a", "H1"),
        });

        Assert.Single(r.Devices);
        Assert.Equal("a", r.Devices[0].Device);
    }

    [Fact]
    public void MergesDistinctDevices()
    {
        var r = CotiDeviceMerge.Merge(new[] { File("a.json", "pvs14", "111"), File("b.json", "gpnvg", "222") });

        Assert.Equal(2, r.Devices.Count);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void DuplicateDeviceNameKeepsTheFirstSortedPathAndWarns()
    {
        // Sorted by path, so "a.json" wins over "z.json" deterministically. Order of the
        // INPUT must not decide the outcome, which is what makes this reproducible across
        // filesystems that enumerate differently.
        var r = CotiDeviceMerge.Merge(new[] { File("z.json", "pvs14", "222"), File("a.json", "pvs14", "111") });

        Assert.Single(r.Devices);
        Assert.Equal("111", r.Devices[0].Hosts[0].Id);
        Assert.Contains(r.Warnings, w => w.Contains("pvs14") && w.Contains("z.json"));
    }

    [Fact]
    public void DuplicateHostIdAcrossDevicesDropsTheLoserAndWarns()
    {
        // One id cannot have two poses. The whole losing FILE is dropped, not just the
        // clashing entry, because a file claiming someone else's host is not trustworthy.
        var r = CotiDeviceMerge.Merge(new[] { File("a.json", "pvs14", "111"), File("b.json", "clone", "111") });

        Assert.Single(r.Devices);
        Assert.Equal("pvs14", r.Devices[0].Device);
        Assert.Contains(r.Warnings, w => w.Contains("111"));
    }

    [Fact]
    public void AMalformedFileSkipsItselfOnly()
    {
        var broken = new CotiParsedFile { Path = "bad.json", Device = null, ParseError = "unexpected token" };
        var r = CotiDeviceMerge.Merge(new[] { broken, File("a.json", "pvs14", "111") });

        Assert.Single(r.Devices);
        Assert.Contains(r.Warnings, w => w.Contains("bad.json") && w.Contains("unexpected token"));
    }

    [Fact]
    public void AnUnknownSchemaSkipsTheFileRatherThanBindingDefaults()
    {
        var f = File("future.json", "whatever", "111");
        f.Device!.Schema = 2;
        var r = CotiDeviceMerge.Merge(new[] { f });

        Assert.Empty(r.Devices);
        Assert.Contains(r.Warnings, w => w.Contains("schema"));
    }

    [Fact]
    public void ANonPositiveMaskRadiusSkipsTheFile()
    {
        // MaskGenerator returns null on a non-positive radius, which reads as COTI being
        // broken rather than as a bad file. Catch it at load, where it can be named.
        var f = File("flat.json", "flat", "111");
        f.Device!.Mask.Radius = 0f;
        var r = CotiDeviceMerge.Merge(new[] { f });

        Assert.Empty(r.Devices);
        Assert.Contains(r.Warnings, w => w.Contains("flat.json"));
    }

    [Fact]
    public void ANullHostsListSkipsTheFileInsteadOfThrowing()
    {
        // An explicit "hosts": null in a hand-edited file overwrites the property
        // initialiser, so the guard cannot rely on construction. Merge throwing here would
        // take down the whole table over one bad file, which is exactly what the
        // skips-itself-only contract forbids.
        var f = File("nullhosts.json", "broken", "111");
        f.Device!.Hosts = null!;

        var r = CotiDeviceMerge.Merge(new[] { f });

        Assert.Empty(r.Devices);
        Assert.Contains(r.Warnings, w => w.Contains("nullhosts.json"));
    }

    [Fact]
    public void AnEmptyHostsListSkipsTheFileToo()
    {
        // Empty is as useless as null: nothing can mount the device, and it would otherwise
        // vanish silently at resolve time with no line naming the file. It is also what a
        // published "hosts": null or "hosts": [null] now arrives as, since the DTO substitutes
        // rather than throwing on the ungated publish route.
        var f = File("nohosts.json", "broken", "111");
        f.Device!.Hosts.Clear();

        var r = CotiDeviceMerge.Merge(new[] { f });

        Assert.Empty(r.Devices);
        Assert.Contains(r.Warnings, w => w.Contains("nohosts.json"));
    }

    [Fact]
    public void ANullHostEntrySkipsThatEntryInsteadOfThrowing()
    {
        // A hand-edited "hosts": [null, {...}] deserialises to a list containing a null entry.
        // Both the clash scan and the registration loop walk Hosts, so a null entry must not
        // throw in either one - the entry is skipped, not the whole file.
        var f = File("nullentry.json", "pvs14", "111");
        f.Device!.Hosts.Add(null!);

        var r = CotiDeviceMerge.Merge(new[] { f });

        Assert.Single(r.Devices);
        Assert.Equal("pvs14", r.Devices[0].Device);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void SlotNameMatchesTheTransformTheClientCreates()
    {
        // Pins the literal rather than the constant, so a rename of the constant cannot silently
        // change what the client and server agree the slot is called.
        Assert.Equal("mod_coti", CotiIds.ModSlotName);
    }
}
