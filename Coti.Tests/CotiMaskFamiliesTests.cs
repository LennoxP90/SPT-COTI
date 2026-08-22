using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

public class CotiMaskFamiliesTests
{
    private static CotiDeviceFile Tuned(string name, float radius) => new CotiDeviceFile
    {
        Schema = 1, Device = name, DisplayName = name, Tuned = true,
        Mask = new CotiMaskBlock { CenterX = 0.525f, CenterY = 0.5f, Radius = radius, Feather = 0.01f },
    };

    private static readonly Dictionary<string, string> Family = new()
    {
        ["gpnvg"] = "Anvis", ["n15"] = "Binocular", ["pvs14"] = "OldMonocular",
    };

    private static string? FamilyOf(CotiDeviceFile d) => d.Device == null ? null : Family[d.Device];

    [Fact]
    public void SeedsFromATunedDeviceOfTheSameFamily()
    {
        var seed = CotiMaskFamilies.SeedFor("Anvis", new[] { Tuned("gpnvg", 0.285f), Tuned("n15", 0.274f) }, FamilyOf);

        Assert.Equal(0.285f, seed.Mask.Radius);
    }

    [Fact]
    public void UntunedDevicesAreNeverUsedAsASeed()
    {
        // Seeding from an unposed stub propagates a wrong mask to every later device of that
        // family, and it looks measured.
        var stub = Tuned("gpnvg", 0.9f);
        stub.Tuned = false;

        var seed = CotiMaskFamilies.SeedFor("Anvis", new[] { stub }, FamilyOf);

        Assert.Equal(CotiMaskFamilies.Fallback.Radius, seed.Mask.Radius);
    }

    [Fact]
    public void AnUnknownFamilyFallsBackToACentredCircle()
    {
        var seed = CotiMaskFamilies.SeedFor("SomethingNew", new[] { Tuned("gpnvg", 0.285f) }, FamilyOf);

        Assert.Equal(CotiMaskFamilies.Fallback.Radius, seed.Mask.Radius);
        Assert.True(seed.Mask.Radius > 0f, "a non-positive radius generates no mask at all");
    }

    [Fact]
    public void ANullFamilyFallsBackRatherThanThrowing()
    {
        var seed = CotiMaskFamilies.SeedFor(null, new[] { Tuned("gpnvg", 0.285f) }, FamilyOf);

        Assert.True(seed.Mask.Radius > 0f);
    }

    [Fact]
    public void TheSeedIsACopySoEditingItCannotMutateAShippedDevice()
    {
        var gpnvg = Tuned("gpnvg", 0.285f);
        var seed = CotiMaskFamilies.SeedFor("Anvis", new[] { gpnvg }, FamilyOf);

        seed.Mask.Radius = 0.1f;

        Assert.Equal(0.285f, gpnvg.Mask.Radius);
    }

    [Fact]
    public void SeededFromNamesTheMatchedDeviceOrIsNullOnFallback()
    {
        var gpnvg = Tuned("gpnvg", 0.285f);

        var matched = CotiMaskFamilies.SeedFor("Anvis", new[] { gpnvg }, FamilyOf);
        Assert.Same(gpnvg, matched.SeededFrom);

        var fellBack = CotiMaskFamilies.SeedFor("SomethingNew", new[] { gpnvg }, FamilyOf);
        Assert.Null(fellBack.SeededFrom);
    }
}
