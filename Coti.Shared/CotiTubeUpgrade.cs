using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// Turns a one-tube device into a multi-tube one, and keeps the legacy mask and mount pair, which every COTI before
  /// 3.3.0 reads, in step with the home tube.
  /// </summary>
  public static class CotiTubeUpgrade
  {
    /// <summary>
    /// Poses what a layout needs and <paramref name="mounts"/> lacks: the home tube at the legacy mount, then its
    /// partner as the mirror of the home tube. A posed tube is never replaced, and other tubes stay unposed.
    /// </summary>
    public static void Seed( CotiLayout layout, IDictionary<string, CotiMountBlock> mounts, CotiMountBlock legacy )
    {
      if( !mounts.ContainsKey( layout.Home ) )
        mounts[layout.Home] = legacy.Copy();

      var partner = CotiTubes.PartnerOf( layout.Home );

      if( partner != null && layout.Find( partner ) != null && !mounts.ContainsKey( partner ) )
        mounts[partner] = CotiTubeMirror.Mirror( mounts[layout.Home], null );
    }

    /// <summary>
    /// A copy of <paramref name="device"/> as the mount editor saves it: the layout chosen there, and each of that
    /// layout's tubes found in <paramref name="mounts"/> with the pod the device already gives it. Labels outside the
    /// layout are left out. Each tube's text block is the editor's from <paramref name="texts"/>, or the device's own
    /// when it passes none; the top-level block, which only a file without tubes reads, goes.
    /// </summary>
    public static CotiDeviceFile WithLayout( CotiDeviceFile device, CotiLayout layout,
        IReadOnlyDictionary<string, CotiMountBlock> mounts, IReadOnlyDictionary<string, CotiTextBlock?>? texts = null )
    {
      var saved = device.Copy();
      saved.Layout = layout.Name;
      saved.Tubes = new Dictionary<string, CotiTube>();
      saved.Text = null;

      foreach( var tube in layout.Tubes )
      {
        if( !mounts.TryGetValue( tube.Label, out var mount ) )
          continue;

        CotiTube? old = null;
        device.Tubes?.TryGetValue( tube.Label, out old );
        CotiTextBlock? text = null;
        if( texts != null )
          texts.TryGetValue( tube.Label, out text );
        else
          text = old?.Text;

        saved.Tubes[tube.Label] = new CotiTube { Mount = mount.Copy(), Pod = old?.Pod?.Copy(), Text = text?.Copy() };
      }

      WriteLegacyPair( saved, layout );
      return saved;
    }

    /// <summary>
    /// mount becomes a copy of the home tube's (left as it is when the home tube is not posed) and mask the home
    /// circle at 16:9, so a COTI before 3.3.0 seats one COTI on the home tube.
    /// </summary>
    public static void WriteLegacyPair( CotiDeviceFile device, CotiLayout layout )
    {
      if( device.Tubes != null && device.Tubes.TryGetValue( layout.Home, out var home ) && home?.Mount != null )
        device.Mount = home.Mount.Copy();

      var tube = layout.Find( layout.Home );

      if( tube != null )
        device.Mask = CotiCircles.LegacyMask( tube );
    }
  }
}
