using System;

namespace Coti.Shared
{
  /// <summary>
  /// The measured masks, one per tube count. Every tuned device in the shipped set lands on one of
  /// the three, and the devices that share one agree to four decimals: the dual tubes on
  /// 0.5361/0.274, the quad tubes on 0.525/0.285.
  ///
  /// The quad mask sits further left and is about four percent wider than the dual one, so a quad
  /// device seeded from a dual donor looks subtly wrong.
  ///
  /// EFT's own family (OldMonocular, Anvis, Binocular) does not decide this: it tags the Aishi
  /// PVS-31A, a dual tube, into the same family as the GPNVGs. <see cref="CotiMaskFamilies"/>
  /// seeds from the family, so it gives that device a quad mask.
  /// </summary>
  public static class CotiMaskPresets
  {
    public const string Single = "Single tube";
    public const string Dual = "Dual tube";
    public const string Quad = "Quad tube";
    public const string Custom = "Custom";

    /// <summary>Vanilla PVS-14 and PNV-10T. One tube on the centre line, one eye or both.</summary>
    public static CotiMaskBlock SingleTube =>
        new CotiMaskBlock { CenterX = 0.5f, CenterY = 0.5f, Radius = 0.273f, Feather = 0.01f };

    /// <summary>Vanilla N-15 and PNV-57E; modded DTNVS, PVS-31A and AN/PVS-5A.</summary>
    public static CotiMaskBlock DualTube =>
        new CotiMaskBlock { CenterX = 0.5361f, CenterY = 0.5f, Radius = 0.274f, Feather = 0.01f };

    /// <summary>Vanilla GPNVG-18; modded Argus Chimera.</summary>
    public static CotiMaskBlock QuadTube =>
        new CotiMaskBlock { CenterX = 0.525f, CenterY = 0.5f, Radius = 0.285f, Feather = 0.01f };

    /// <summary>
    /// The one line under the picker. Names a vanilla goggle per layout, since most devices on a
    /// given mask are modded and the reader may not have them installed.
    /// </summary>
    public const string Guidance =
        "Match the tube count on the model: single like the PVS-14, dual like the N-15, "
      + "quad like the GPNVG-18.";

    /// <summary>Said when a device carries a mask of its own, so nothing is selected.</summary>
    public const string CustomGuidance = "Tuned by hand, matching no preset. Picking one replaces it.";

    /// <summary>The preset a block matches, or Custom. A hand-tuned mask must stay hand-tuned.</summary>
    public static string NameFor( CotiMaskBlock? mask )
    {
      if( mask == null )
        return Custom;

      if( Matches( mask, SingleTube ) )
        return Single;

      if( Matches( mask, DualTube ) )
        return Dual;

      if( Matches( mask, QuadTube ) )
        return Quad;

      return Custom;
    }

    /// <summary>A fresh copy, so an edit cannot mutate the preset every other device reads.</summary>
    public static CotiMaskBlock? ByName( string? name )
    {
      if( string.Equals( name, Single, StringComparison.OrdinalIgnoreCase ) )
        return SingleTube;

      if( string.Equals( name, Dual, StringComparison.OrdinalIgnoreCase ) )
        return DualTube;

      if( string.Equals( name, Quad, StringComparison.OrdinalIgnoreCase ) )
        return QuadTube;

      return null;
    }

    /// <summary>
    /// Loose to two thousandths. The stored values are hand-measured, so the same position tuned
    /// twice lands a thousandth apart: PVS-14 at 0.5/0.273 and the PNV-10T biocular at
    /// 0.5011/0.274 are one preset.
    ///
    /// The nearest two distinct presets, dual and quad, are eleven thousandths apart on both
    /// centre and radius, so the tolerance cannot merge them.
    /// </summary>
    private static bool Matches( CotiMaskBlock a, CotiMaskBlock b )
    {
      return Near( a.CenterX, b.CenterX )
          && Near( a.CenterY, b.CenterY )
          && Near( a.Radius, b.Radius )
          && Near( a.Feather, b.Feather );
    }

    private static bool Near( float a, float b )
    {
      return Math.Abs( a - b ) < 0.002f;
    }
  }
}
