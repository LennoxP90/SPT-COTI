using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

/// <summary>
/// Properties that must hold for every device COTI ships, independent of the migration from
/// coti-defaults.json. This test outlives CotiDeviceMigrationTests: it reads only the hosts/
/// files themselves, so it keeps covering the shipped set after the old defaults file, and the
/// migration test that pins against it, are deleted.
///
/// Discovers the files under the embedded Hosts/ path rather than hardcoding the five names, so
/// a sixth device added later is covered automatically instead of silently escaping the checks.
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
    /// Every mask and mount value of every device, pinned. These are measured poses confirmed in
    /// game, and a moved pose is invisible until someone looks at that device in a raid - so this is
    /// what stands between an accidental edit and a device that mounts wrong for everyone.
    /// </summary>
    [Theory]
    [InlineData("com.c11.truenorth4_anpvs5a.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "anpvs5",
        0.0365f, -0.023f, -0.057f,
        0f, 0f, 0f,
        0f, 0f, 34f, 1.46f)]
    [InlineData("com.c11.truenorth4_argus_chimera.json",
        0.525f, 0.5f, 0.285f, 0.01f,
        "axis_2",
        0.007f, -0.0435f, -0.0525f,
        0f, 0f, 0f,
        0f, 0f, 28f, 1.518f)]
    [InlineData("com.c11.truenorth4_dtnvs.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "axis_2",
        -0.0005f, -0.0435f, -0.038f,
        0f, 0f, 0f,
        0f, 0f, 48f, 1.065f)]
    [InlineData("com.wtt.cag_dtnvs.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "",
        0.036f, -0.067f, 0.05f,
        -90f, 0f, 0f,
        -43f, 0f, 0f, 1.24f)]
    [InlineData("com.wtt.contentbackport_pvs31a.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "",
        0.034f, -0.073f, 0.03f,
        -90f, 0f, 0f,
        -43f, 0f, 0f, 1.32f)]
    [InlineData("vanilla_gpnvg.json",
        0.525f, 0.5f, 0.285f, 0.01f,
        "axis",
        0.027f, -0.037f, -0.075f,
        -90f, 0f, 0f,
        -26f, 90f, 2f, 1.46f)]
    [InlineData("vanilla_n15.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "",
        0.033f, -0.023f, 0.037f,
        -90f, 0f, 0f,
        -42f, 0f, 0f, 1.24f)]
    [InlineData("vanilla_pnv10t.json",
        0.5011f, 0.5f, 0.274f, 0.01f,
        "",
        0.0005f, -0.032f, 0.067f,
        0f, 0f, 0f,
        12f, -90f, -11f, 0.91f)]
    [InlineData("vanilla_pnv57e.json",
        0.5361f, 0.5f, 0.274f, 0.01f,
        "axis",
        0.036f, -0.119f, -0.081f,
        0f, 0f, 0f,
        0f, 0f, 40f, 1.32f)]
    [InlineData("vanilla_pvs14.json",
        0.5f, 0.5f, 0.273f, 0.01f,
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
        // Looked up by file name across everything embedded, not by shipped-set membership.
        var dtnvs = ShippedDevices().Single(kv => kv.Key.EndsWith(".com.wtt.cag_dtnvs.json")).Value;
        var hosts = dtnvs.GetProperty("hosts").EnumerateArray().ToArray();

        Assert.Equal(2, hosts.Length);

        var ids = hosts.Select(h => h.GetProperty("id").GetString()).ToArray();
        Assert.Contains("6974ce066e50d4be623b8d9b", ids);
        Assert.Contains("6974cf52ee1fb8a0683b8d9d", ids);

        // Deliberately ambiguous: both ids resolve to the same prefab because they share one
        // mesh and one pose. Resolution only falls back to prefab when an id is missing, and an
        // ambiguous fallback is skipped with a warning rather than guessed - do not "fix" this by
        // inventing two distinct prefab paths.
        var prefabs = hosts.Select(h => h.GetProperty("prefab").GetString()).Distinct().ToArray();
        Assert.Single(prefabs);
    }

    [Fact]
    public void OnlyDevicesThatNeedAnotherModDeclareRequires()
    {
        // The invariant that a wrong guid already violated once. A vanilla device must not gate
        // itself behind a mod, and a device built on another mod's items must gate itself - without
        // it, a player lacking the host mod gets a prefab-ambiguity warning instead of one clear
        // line naming what is missing.
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
    public void EachAddonDeclaresTheGuidItsHostModActuallyRegisters(string deviceFile, string guid)
    {
        // Pinned per device because these are copied by hand and cannot be derived from anything.
        // com.bobinstien.c11truenorth was shipped in a device file and in ADDONS.md's example, was
        // never registered by any mod, and silently disabled the whole device.
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
        // SECOND file on the first republish - two files, one host, and a duplicate warning.
        foreach (var (name, device) in ShippedDevices())
        {
            var deviceName = device.GetProperty("device").GetString()!;
            Assert.EndsWith("." + deviceName + ".json", name, System.StringComparison.Ordinal);
        }
    }
}
