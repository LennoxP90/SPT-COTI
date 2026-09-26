using System;
using System.Collections.Generic;
using System.Linq;

namespace Coti.Shared
{
  /// <summary>
  /// A discovered device starts from the mask of an already-measured device in its own family
  /// rather than from nothing. EFT declares the family per item as OldMonocular, Anvis or
  /// Binocular, and a clone inherits its donor's, so C11's Chimeras arrive as Anvis - the
  /// family whose geometry is already measured.
  ///
  /// The seed is only a starting point: the device stays tuned:false until someone confirms it.
  /// </summary>
  public static class CotiMaskFamilies
  {
    /// <summary>
    /// Used when no tuned device shares the family. A centred circle, positive radius: a
    /// non-positive one generates no mask at all, which reads as the mod being broken.
    /// </summary>
    public static CotiMaskBlock Fallback =>
        new CotiMaskBlock { CenterX = 0.5f, CenterY = 0.5f, Radius = 0.274f, Feather = 0.01f };

    public static CotiMaskSeed SeedFor(
        string? maskFamily, IEnumerable<CotiDeviceFile> devices, Func<CotiDeviceFile, string?> familyOf )
    {
      if( string.IsNullOrWhiteSpace( maskFamily ) || devices == null )
        return new CotiMaskSeed( Fallback, null );

      var match = devices.FirstOrDefault(
          d => d != null
            && d.Tuned
            && d.Mask != null
            && d.Mask.Radius > 0f
            && string.Equals( familyOf( d ), maskFamily, StringComparison.OrdinalIgnoreCase ) );

      if( match == null )
        return new CotiMaskSeed( Fallback, null );

      // A copy. Handing out the shipped device's own block lets a tuner edit mutate it.
      var mask = new CotiMaskBlock
      {
        CenterX = match.Mask.CenterX,
        CenterY = match.Mask.CenterY,
        Radius = match.Mask.Radius,
        Feather = match.Mask.Feather,
      };

      return new CotiMaskSeed( mask, match );
    }
  }

  /// <summary>
  /// What SeedFor found as well as what it built, so a caller can tell whether it matched a tuned
  /// device or fell back without re-testing the match condition and risking drift.
  /// </summary>
  public class CotiMaskSeed
  {
    public CotiMaskSeed( CotiMaskBlock mask, CotiDeviceFile? seededFrom )
    {
      Mask = mask;
      SeededFrom = seededFrom;
    }

    public CotiMaskBlock Mask { get; }

    /// <summary>
    /// The tuned device this mask was copied from, or null when no tuned device shared the family
    /// and the fallback was used.
    /// </summary>
    public CotiDeviceFile? SeededFrom { get; }
  }
}
