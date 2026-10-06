using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Coti.Shared;
using Xunit;

/// <summary>
/// Properties that must hold for every device COTI ships. Reads only the shipped device files
/// themselves.
///
/// Discovers the files under the embedded Hosts/ path rather than hardcoding names, so a newly
/// added device is covered automatically.
/// </summary>
public class CotiShippedDevicesTests
{
    private static readonly string[] ImageParameterKeys =
    {
        "minimumTemperatureValue", "mainTexColorCoef", "depthFade", "isPixelated", "isNoisy",
        "isMotionBlurred", "unsharpRadiusBlur", "unsharpBias", "overlayContrast", "overlayExposure",
        "compositeMode", "palette", "rampShift", "heatThreshold", "outlineMix", "outlineWidth",
        "overlayIntensity",
    };

    private static Dictionary<string, JsonElement> ShippedDevices()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames()
                        .Where(n => n.Contains(".Hosts.") && n.EndsWith(".json"));

        var devices = new Dictionary<string, JsonElement>();
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var doc = JsonDocument.Parse(stream);
            devices[name] = doc.RootElement.Clone();
        }

        Assert.True(devices.Count >= 10,
            "expected at least 10 device files across hosts/ and addons/, found " + devices.Count);
        return devices;
    }

    [Fact]
    public void EveryDeviceDeclaresCurrentSchema()
    {
        foreach (var (name, device) in ShippedDevices())
            Assert.True(device.GetProperty("schema").GetInt32() == 1, name + " is not schema 1");
    }

    [Fact]
    public void EveryDeviceIsMarkedTuned()
    {
        foreach (var (name, device) in ShippedDevices())
            Assert.True(device.GetProperty("tuned").GetBoolean(), name + " ships untuned");
    }

    [Fact]
    public void EveryDeviceHasANonEmptyDisplayName()
    {
        foreach (var (name, device) in ShippedDevices())
            Assert.False(string.IsNullOrWhiteSpace(device.GetProperty("displayName").GetString()), name + " has no displayName");
    }

    [Fact]
    public void DeviceIdsAreNonEmptyAndUnique()
    {
        var seen = new HashSet<string>();
        foreach (var (name, device) in ShippedDevices())
        {
            var id = device.GetProperty("device").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id), name + " has no device id");
            Assert.True(seen.Add(id!), "device id \"" + id + "\" is used by more than one file (" + name + ")");
        }
    }

    [Fact]
    public void EveryMaskRadiusIsPositive()
    {
        foreach (var (name, device) in ShippedDevices())
        {
            var radius = device.GetProperty("mask").GetProperty("radius").GetSingle();
            Assert.True(radius > 0f, name + " has a non-positive mask radius (" + radius + "), which renders no mask at all");
        }
    }

    [Fact]
    public void HostIdsAreNonEmptyAndUniqueAcrossAllFiles()
    {
        var seenBy = new Dictionary<string, string>();
        foreach (var (name, device) in ShippedDevices())
        {
            foreach (var host in device.GetProperty("hosts").EnumerateArray())
            {
                var id = host.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(id), name + " has a host with no id");

                if (seenBy.TryGetValue(id!, out var owner))
                    Assert.Fail("host id \"" + id + "\" appears in both " + owner + " and " + name + " - one host cannot have two poses");

                seenBy[id!] = name;
            }
        }
    }

    [Fact]
    public void NoShippedDeviceCarriesImageParameters()
    {
        foreach (var (name, device) in ShippedDevices())
        {
            foreach (var key in ImageParameterKeys)
                Assert.False(device.TryGetProperty(key, out _), name + " carries image parameter \"" + key + "\", which belongs in F12 globals, not the device file");
        }
    }

    /// <summary>
    /// Every mask and mount value of every device, pinned. These are measured poses, and a moved
    /// pose is invisible until someone looks at that device in a raid, so this catches an
    /// accidental edit.
    /// </summary>
    // The mask is the legacy pair's: the home tube's circle at 16:9 (pvs5a 0.5021/0.5056/0.24311, quad 0.5371/0.4992/
    // 0.28506, dual 0.5353/0.4991/0.27362, mono 0.5006/0.4992/0.27359). The mounts are the home tubes', straightened in the
    // viewer on 2026-10-06 to stop clipping.
    [Theory]
    [InlineData("com.c11.truenorth4_anpvs5a.json",
        0.5021f, 0.5056f, 0.24311f, 0.01f,
        "anpvs5",
        0.0365f, -0.023f, -0.057f,
        0f, 0f, 0f,
        0f, 0f, 0f, 1.46f)]
    [InlineData("com.c11.truenorth4_argus_chimera.json",
        0.5371f, 0.4992f, 0.28506f, 0.01f,
        "axis_2",
        0.007f, -0.0415f, -0.0525f,
        0f, 0f, 0f,
        0f, 0f, 0f, 1.518f)]
    [InlineData("com.c11.truenorth4_dtnvs.json",
        0.5353f, 0.4991f, 0.27362f, 0.01f,
        "axis_2",
        -0.0005f, -0.0435f, -0.038f,
        0f, 0f, 0f,
        0f, 0f, 0f, 1.065f)]
    [InlineData("com.wtt.cag_dtnvs.json",
        0.5353f, 0.4991f, 0.27362f, 0.01f,
        "",
        0.036f, -0.067f, 0.05f,
        -90f, 0f, 0f,
        0f, 0f, 0f, 1.24f)]
    [InlineData("com.wtt.contentbackport_pvs31a.json",
        0.5353f, 0.4991f, 0.27362f, 0.01f,
        "",
        0.034f, -0.073f, 0.03f,
        -90f, 0f, 0f,
        0f, 0f, 0f, 1.32f)]
    [InlineData("vanilla_gpnvg.json",
        0.5371f, 0.4992f, 0.28506f, 0.01f,
        "axis",
        0.027f, -0.037f, -0.075f,
        -90f, 0f, 0f,
        6f, 90f, 2f, 1.46f)]
    [InlineData("vanilla_n15.json",
        0.5353f, 0.4991f, 0.27362f, 0.01f,
        "",
        0.0305f, -0.0205f, 0.034f,
        -90f, 0f, 0f,
        0f, 0f, 0f, 1.2f)]
    // The mono layout's circle: Borkel's PNV-10T mask is one hole, like the PVS-14's.
    [InlineData("vanilla_pnv10t.json",
        0.5006f, 0.4992f, 0.27359f, 0.01f,
        "",
        0.001f, -0.035f, 0.075f,
        0f, 0f, 0f,
        12f, -90f, -11f, 1f)]
    [InlineData("vanilla_pnv57e.json",
        0.5353f, 0.4991f, 0.27362f, 0.01f,
        "axis",
        0.036f, -0.119f, -0.081f,
        0f, 0f, 0f,
        0f, 0f, 0f, 1.32f)]
    [InlineData("vanilla_pvs14.json",
        0.5006f, 0.4992f, 0.27359f, 0.01f,
        "",
        0.015f, -0.024f, 0.039f,
        -90f, 0f, 0f,
        104f, 0f, 0f, 1.16f)]
    public void MaskAndMountMatchTheHandTunedValues(
        string deviceFile, float centerX, float centerY, float radius, float feather,
        string anchorBone, float positionX, float positionY, float positionZ,
        float rotationX, float rotationY, float rotationZ,
        float rollDegrees, float pitchDegrees, float yawDegrees, float scale)
    {
        var device = ShippedDevices().Single(kv => kv.Key.EndsWith(deviceFile)).Value;
        var mask = device.GetProperty("mask");
        var mount = device.GetProperty("mount");

        Assert.Equal(centerX, mask.GetProperty("centerX").GetSingle());
        Assert.Equal(centerY, mask.GetProperty("centerY").GetSingle());
        Assert.Equal(radius, mask.GetProperty("radius").GetSingle());
        Assert.Equal(feather, mask.GetProperty("feather").GetSingle());

        Assert.Equal(anchorBone, mount.GetProperty("anchorBone").GetString());
        Assert.Equal(positionX, mount.GetProperty("positionX").GetSingle());
        Assert.Equal(positionY, mount.GetProperty("positionY").GetSingle());
        Assert.Equal(positionZ, mount.GetProperty("positionZ").GetSingle());
        Assert.Equal(rotationX, mount.GetProperty("rotationX").GetSingle());
        Assert.Equal(rotationY, mount.GetProperty("rotationY").GetSingle());
        Assert.Equal(rotationZ, mount.GetProperty("rotationZ").GetSingle());
        Assert.Equal(rollDegrees, mount.GetProperty("rollDegrees").GetSingle());
        Assert.Equal(pitchDegrees, mount.GetProperty("pitchDegrees").GetSingle());
        Assert.Equal(yawDegrees, mount.GetProperty("yawDegrees").GetSingle());
        Assert.Equal(scale, mount.GetProperty("scale").GetSingle());
    }

    [Fact]
    public void DtnvsCarriesBothPhosphorHostsWithTheSharedPrefab()
    {
        // Looked up by file name across everything embedded.
        var dtnvs = ShippedDevices().Single(kv => kv.Key.EndsWith(".com.wtt.cag_dtnvs.json")).Value;
        var hosts = dtnvs.GetProperty("hosts").EnumerateArray().ToArray();

        Assert.Equal(2, hosts.Length);

        var ids = hosts.Select(h => h.GetProperty("id").GetString()).ToArray();
        Assert.Contains("6974ce066e50d4be623b8d9b", ids);
        Assert.Contains("6974cf52ee1fb8a0683b8d9d", ids);

        // Both ids resolve to the same prefab because they share one mesh and one pose.
        // Resolution only falls back to prefab when an id is missing, and an ambiguous fallback
        // is skipped with a warning rather than guessed, so the two hosts keep one prefab path.
        var prefabs = hosts.Select(h => h.GetProperty("prefab").GetString()).Distinct().ToArray();
        Assert.Single(prefabs);
    }

    [Fact]
    public void OnlyDevicesThatNeedAnotherModDeclareRequires()
    {
        // A vanilla device must not gate itself behind a mod, and a device built on another mod's
        // items must gate itself. Without the gate, a player lacking the host mod gets a
        // prefab-ambiguity warning instead of one line naming what is missing.
        var vanilla = new[] { "vanilla_gpnvg.json", "vanilla_n15.json", "vanilla_pvs14.json", "vanilla_pnv57e.json", "vanilla_pnv10t.json" };

        foreach (var (name, device) in ShippedDevices())
        {
            var isVanilla = vanilla.Any(v => name.EndsWith("." + v));
            var hasRequires = device.TryGetProperty("requires", out var req)
                              && !string.IsNullOrWhiteSpace(req.GetString());

            Assert.True(isVanilla != hasRequires,
                isVanilla
                    ? name + " is a vanilla device and must not declare requires"
                    : name + " depends on another mod and must declare requires");
        }
    }

    [Theory]
    [InlineData("com.c11.truenorth4_argus_chimera.json", "com.c11.truenorth4")]
    [InlineData("com.c11.truenorth4_anpvs5a.json", "com.c11.truenorth4")]
    [InlineData("com.c11.truenorth4_dtnvs.json", "com.c11.truenorth4")]
    [InlineData("com.wtt.cag_dtnvs.json", "com.wtt.cag")]
    [InlineData("com.wtt.contentbackport_pvs31a.json", "com.wtt.contentbackport")]
    [InlineData("com.crackbone.artem-wtt_gpnvg18.json", "com.crackbone.artem-wtt")]
    [InlineData("com.crackbone.artem-wtt_pvs31.json", "com.crackbone.artem-wtt")]
    [InlineData("com.crackbone.artem-wtt_pvs31_wide.json", "com.crackbone.artem-wtt")]
    public void EachAddonDeclaresTheGuidItsHostModActuallyRegisters(string deviceFile, string guid)
    {
        // Pinned per device because these are copied by hand and cannot be derived. A guid no mod
        // registers disables the whole device.
        var device = ShippedDevices().Single(kv => kv.Key.EndsWith("." + deviceFile)).Value;

        Assert.Equal(guid, device.GetProperty("requires").GetString());
    }

    [Fact]
    public void EveryDeviceNameCarriesItsSourceAsAPrefix()
    {
        // The device name is what merge dedupes on and what publish names the file after, so it must be
        // unique across every addon anyone writes. No path scheme can enforce that, because the
        // collision lives inside the file; a guid prefix is unique by construction.
        foreach (var (name, device) in ShippedDevices())
        {
            var deviceName = device.GetProperty("device").GetString()!;
            var hasRequires = device.TryGetProperty("requires", out var req)
                              && !string.IsNullOrWhiteSpace(req.GetString());

            var expected = hasRequires ? req.GetString() + "_" : "vanilla_";

            Assert.True(deviceName.StartsWith(expected, System.StringComparison.Ordinal),
                $"{name}: device \"{deviceName}\" should start with \"{expected}\"");
        }
    }

    [Fact]
    public void EveryFileIsNamedAfterItsDevice()
    {
        // Publish writes <device>.json, so a file whose name disagrees with its device gets a
        // second file on the first republish - two files, one host, and a duplicate warning.
        foreach (var (name, device) in ShippedDevices())
        {
            var deviceName = device.GetProperty("device").GetString()!;
            Assert.EndsWith("." + deviceName + ".json", name, System.StringComparison.Ordinal);
        }
    }

    private static CotiDeviceFile Parse(JsonElement element)
    {
        return JsonSerializer.Deserialize<Coti.Server.CotiDeviceDto>(element.GetRawText())!.ToShared();
    }

    private static bool SameMount(CotiMountBlock a, CotiMountBlock b)
    {
        return (a.AnchorBone ?? "") == (b.AnchorBone ?? "")
               && a.PositionX == b.PositionX && a.PositionY == b.PositionY && a.PositionZ == b.PositionZ
               && a.RotationX == b.RotationX && a.RotationY == b.RotationY && a.RotationZ == b.RotationZ
               && a.RollDegrees == b.RollDegrees && a.PitchDegrees == b.PitchDegrees && a.YawDegrees == b.YawDegrees
               && a.Scale == b.Scale;
    }

    private static bool Near(float a, float b)
    {
        return Math.Abs(a - b) < 1e-6f;
    }

    [Fact]
    public void EveryDeviceHasTubesThatValidateWithoutWarnings()
    {
        foreach (var (name, element) in ShippedDevices())
        {
            var device = Parse(element);
            var warnings = new List<string>();
            CotiTubeValidation.Normalise(device, name, warnings);

            Assert.True(warnings.Count == 0, name + ": " + string.Join("; ", warnings));
            Assert.True(device.IsMultiTube, name + " has no layout and tubes");
        }
    }

    [Fact]
    public void EveryTubeOfTheLayoutIsPosed()
    {
        // A tube missing from the file mounts at the legacy mount in game, on top of the home tube's COTI.
        foreach (var (name, element) in ShippedDevices())
        {
            var device = Parse(element);
            Assert.True(CotiLayouts.TryGet(device.Layout, out var layout), name + ": unknown layout \"" + device.Layout + "\"");
            foreach (var tube in layout.Tubes)
                Assert.True(device.Tubes!.ContainsKey(tube.Label), name + " does not pose " + tube.Label);
        }
    }

    [Fact]
    public void TheLegacyPairIsTheHomeTube()
    {
        // Every COTI before 3.3.0 reads only mask and mount, so they must be the home tube's: its mount, and its circle
        // at 16:9 rounded as the model viewer writes it.
        foreach (var (name, element) in ShippedDevices())
        {
            var device = Parse(element);
            Assert.True(CotiLayouts.TryGet(device.Layout, out var layout), name + ": unknown layout \"" + device.Layout + "\"");
            var circle = CotiCircles.LegacyMask(layout.Find(layout.Home)!);

            Assert.True(Near(circle.CenterX, device.Mask.CenterX) && Near(circle.CenterY, device.Mask.CenterY)
                        && Near(circle.Radius, device.Mask.Radius) && Near(circle.Feather, device.Mask.Feather),
                $"{name}: mask {device.Mask.CenterX}/{device.Mask.CenterY}/{device.Mask.Radius}/{device.Mask.Feather} " +
                $"is not the home tube's {circle.CenterX}/{circle.CenterY}/{circle.Radius}/{circle.Feather}");
            Assert.True(SameMount(device.Tubes![layout.Home].Mount, device.Mount), name + ": mount is not the home tube's");
        }
    }

    /// <summary>
    /// C11 rotates each pod bone between deployed and stowed; the files hold the deployed rotation (C11's On quaternion:
    /// -33 degrees about X on the Chimera, -90 on the DTNVS) and leave the bone to default to the tube's anchor.
    /// </summary>
    [Theory]
    [InlineData("com.c11.truenorth4_argus_chimera.json", "tube_0", "axis_3", -33f)]
    [InlineData("com.c11.truenorth4_argus_chimera.json", "tube_1", "axis_3", -33f)]
    [InlineData("com.c11.truenorth4_argus_chimera.json", "tube_2", "axis_2", -33f)]
    [InlineData("com.c11.truenorth4_argus_chimera.json", "tube_3", "axis_2", -33f)]
    [InlineData("com.c11.truenorth4_dtnvs.json", "tube_1", "axis_1", -90f)]
    [InlineData("com.c11.truenorth4_dtnvs.json", "tube_2", "axis_2", -90f)]
    public void C11PodsAreDownAtTheirDeployedRotation(string deviceFile, string label, string podBone, float downX)
    {
        var device = Parse(ShippedDevices().Single(kv => kv.Key.EndsWith("." + deviceFile)).Value);
        var tube = device.Tubes![label];

        Assert.Equal(podBone, tube.Mount.AnchorBone);
        Assert.NotNull(tube.Pod);
        Assert.Null(tube.Pod!.Bone);
        Assert.Equal(downX, tube.Pod.DownX);
        Assert.Equal(0f, tube.Pod.DownY);
        Assert.Equal(0f, tube.Pod.DownZ);
    }

    [Fact]
    public void OnlyTheC11ChimeraAndDtnvsDeclarePods()
    {
        // Every other goggle flips as one unit, which the night vision's own on and off already covers.
        foreach (var (name, element) in ShippedDevices())
        {
            var podded = Parse(element).Tubes!.Values.Any(t => t.Pod != null);
            var c11 = name.EndsWith(".com.c11.truenorth4_argus_chimera.json") || name.EndsWith(".com.c11.truenorth4_dtnvs.json");
            Assert.True(podded == c11, name + (podded ? " declares a pod" : " declares no pod"));
        }
    }

    [Fact]
    public void TheArtemGpnvgWearsTheVanillaGpnvgPoses()
    {
        // Artem's three GPNVG-18s are the vanilla mesh and axis bone with new textures, so their poses are the vanilla ones.
        var devices = ShippedDevices();
        var vanilla = Parse(devices.Single(kv => kv.Key.EndsWith(".vanilla_gpnvg.json")).Value);
        var artem = Parse(devices.Single(kv => kv.Key.EndsWith(".com.crackbone.artem-wtt_gpnvg18.json")).Value);

        Assert.Equal(vanilla.Layout, artem.Layout);
        Assert.Equal(vanilla.Tubes!.Keys.OrderBy(k => k), artem.Tubes!.Keys.OrderBy(k => k));
        foreach (var (label, tube) in vanilla.Tubes)
            Assert.True(SameMount(tube.Mount, artem.Tubes[label].Mount), label + " differs from the vanilla GPNVG-18's");
    }
}
