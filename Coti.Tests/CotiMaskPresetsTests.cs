using System;
using Coti.Shared;
using Xunit;

namespace Coti.Tests;

public class CotiMaskPresetsTests
{
  [Fact]
  public void NamesEachPreset()
  {
    Assert.Equal(CotiMaskPresets.Single, CotiMaskPresets.NameFor(CotiMaskPresets.SingleTube));
    Assert.Equal(CotiMaskPresets.Dual, CotiMaskPresets.NameFor(CotiMaskPresets.DualTube));
    Assert.Equal(CotiMaskPresets.Quad, CotiMaskPresets.NameFor(CotiMaskPresets.QuadTube));
  }

  [Fact]
  public void QuadIsNotDual()
  {
    Assert.NotEqual(CotiMaskPresets.Quad, CotiMaskPresets.NameFor(CotiMaskPresets.DualTube));
    Assert.NotEqual(CotiMaskPresets.DualTube.CenterX, CotiMaskPresets.QuadTube.CenterX);
    Assert.NotEqual(CotiMaskPresets.DualTube.Radius, CotiMaskPresets.QuadTube.Radius);
  }

  [Theory]
  // The values the shipped devices actually carry, so a preset that drifts off them is caught.
  [InlineData(0.5f, 0.5f, 0.273f, 0.01f, "Single tube")]
  [InlineData(0.5361f, 0.5f, 0.274f, 0.01f, "Dual tube")]
  [InlineData(0.525f, 0.5f, 0.285f, 0.01f, "Quad tube")]
  // The PNV-10T biocular: one tube, two eyes, and the same centred position as the PVS-14 to
  // within a thousandth. Two hand-tunings of one preset, not two presets.
  [InlineData(0.5011f, 0.5f, 0.274f, 0.01f, "Single tube")]
  // Far enough from all three to be someone's own measurement.
  [InlineData(0.62f, 0.5f, 0.31f, 0.01f, "Custom")]
  public void MatchesTheShippedDevices(float x, float y, float radius, float feather, string expected)
  {
    var mask = new CotiMaskBlock { CenterX = x, CenterY = y, Radius = radius, Feather = feather };

    Assert.Equal(expected, CotiMaskPresets.NameFor(mask));
  }

  [Fact]
  public void TreatsAnAbsentMaskAsCustom()
  {
    Assert.Equal(CotiMaskPresets.Custom, CotiMaskPresets.NameFor(null));
  }

  [Fact]
  public void ByNameRoundTrips()
  {
    foreach (var name in new[] { CotiMaskPresets.Single, CotiMaskPresets.Dual, CotiMaskPresets.Quad })
    {
      Assert.Equal(name, CotiMaskPresets.NameFor(CotiMaskPresets.ByName(name)));
    }
  }

  [Fact]
  public void ByNameRejectsAnythingElse()
  {
    Assert.Null(CotiMaskPresets.ByName(CotiMaskPresets.Custom));
    Assert.Null(CotiMaskPresets.ByName("dual"));
    Assert.Null(CotiMaskPresets.ByName((string?)null));
  }

  [Fact]
  public void KeepsThePresetsApartAtTheLooserTolerance()
  {
    // Widening the match to take in the PNV-10T must not let the real presets bleed together.
    Assert.Equal(CotiMaskPresets.Single, CotiMaskPresets.NameFor(CotiMaskPresets.SingleTube));
    Assert.Equal(CotiMaskPresets.Dual, CotiMaskPresets.NameFor(CotiMaskPresets.DualTube));
    Assert.Equal(CotiMaskPresets.Quad, CotiMaskPresets.NameFor(CotiMaskPresets.QuadTube));
  }

  [Fact]
  public void GuidanceNamesAVanillaGoggleForEachLayout()
  {
    // The blurb is the only guidance in the panel now, so it has to name devices a reader can
    // actually look at. Every one of these is in the base game.
    Assert.Contains("PVS-14", CotiMaskPresets.Guidance);
    Assert.Contains("N-15", CotiMaskPresets.Guidance);
    Assert.Contains("GPNVG-18", CotiMaskPresets.Guidance);

    foreach (var modded in new[] { "DTNVS", "Chimera", "PVS-31A", "Aishi", "SOCOM" })
    {
      Assert.DoesNotContain(modded, CotiMaskPresets.Guidance, StringComparison.OrdinalIgnoreCase);
    }
  }

  [Fact]
  public void SaysSomethingElseWhenTheMaskIsHandTuned()
  {
    Assert.NotEqual(CotiMaskPresets.Guidance, CotiMaskPresets.CustomGuidance);
    Assert.False(string.IsNullOrWhiteSpace(CotiMaskPresets.CustomGuidance));
  }

  [Fact]
  public void HandsOutACopyRatherThanTheSharedBlock()
  {
    var first = CotiMaskPresets.ByName(CotiMaskPresets.Dual)!;
    first.Radius = 9f;

    Assert.Equal(CotiMaskPresets.Dual, CotiMaskPresets.NameFor(CotiMaskPresets.ByName(CotiMaskPresets.Dual)));
  }
}
