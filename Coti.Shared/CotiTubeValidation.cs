using System.Collections.Generic;
using System.Linq;

namespace Coti.Shared
{
  /// <summary>
  /// The layout and tubes rules of a device file, applied in place once CotiDeviceMerge's own checks have passed. A
  /// fault falls back towards a single COTI on the legacy mount and never further: Mask and Mount are not touched,
  /// and a known layout is never removed, because the slots come from it and a typo in one tube must not take away a
  /// slot that holds a COTI.
  /// </summary>
  public static class CotiTubeValidation
  {
    public static void Normalise( CotiDeviceFile device, string source, List<string> warnings )
    {
      device.Text = Readable( device.Text, source, "text", warnings );

      if( device.Layout == null && device.Tubes == null )
        return;

      if( device.Tubes == null || !CotiLayouts.TryGet( device.Layout, out var layout ) )
      {
        var why = device.Tubes == null ? "has a layout but no tubes"
            : device.Layout == null ? "has tubes but no layout"
            : $"names layout \"{device.Layout}\", which is not one of {string.Join( ", ", CotiLayouts.All.Select( l => l.Name ) )}";
        warnings.Add( $"{source}: {why} - loaded as one COTI on the legacy mount" );
        device.Layout = null;
        device.Tubes = null;
        return;
      }

      if( device.Text != null )
        warnings.Add( $"{source}: has a top-level text block and tubes - ignored, each tube's own text block places it" );

      foreach( var label in device.Tubes.Keys.ToList() )
      {
        var tube = device.Tubes[label];
        var problem = layout.Find( label ) == null ? $"is not a tube of the {layout.Name} layout"
            : tube?.Mount == null ? "has no mount"
            : !Finite( tube.Mount ) ? "has a mount value that is not a finite number"
            : null;

        if( problem != null )
        {
          warnings.Add( $"{source}: tube \"{label}\" {problem} - dropped" );
          device.Tubes.Remove( label );
        }
        else if( tube!.Pod != null && !( Finite( tube.Pod.DownX ) && Finite( tube.Pod.DownY ) && Finite( tube.Pod.DownZ ) ) )
        {
          warnings.Add( $"{source}: tube \"{label}\" has a pod down value that is not a finite number - pod dropped, " +
              "the tube follows the goggles" );
          tube.Pod = null;
        }

        if( tube != null && device.Tubes.ContainsKey( label ) )
          tube.Text = Readable( tube.Text, source, $"tube \"{label}\"'s text", warnings );
      }
    }

    /// <summary>A text block with an unknown align or a value that is not a finite number is dropped: the rule places it.</summary>
    private static CotiTextBlock? Readable( CotiTextBlock? text, string source, string what, List<string> warnings )
    {
      if( text == null )
        return null;

      var problem = text.Align != null && !CotiTextBlock.TryParseAlign( text.Align, out _ )
          ? $"has align \"{text.Align}\", which is not left, right or center"
          : !Finite( text.Edge ?? 0f ) || !Finite( text.Y ?? 0f ) ? "has a value that is not a finite number"
          : null;

      if( problem == null )
        return text;

      warnings.Add( $"{source}: {what} {problem} - dropped, the messages sit where the rule puts them" );
      return null;
    }

    public static CotiMountBlock MountFor( CotiDeviceFile device, string label )
    {
      return device.Tubes != null && device.Tubes.TryGetValue( label, out var tube ) && tube?.Mount != null
          ? tube.Mount
          : device.Mount;
    }

    private static bool Finite( CotiMountBlock m )
    {
      return Finite( m.PositionX ) && Finite( m.PositionY ) && Finite( m.PositionZ )
          && Finite( m.RotationX ) && Finite( m.RotationY ) && Finite( m.RotationZ )
          && Finite( m.RollDegrees ) && Finite( m.PitchDegrees ) && Finite( m.YawDegrees )
          && Finite( m.Scale );
    }

    // float.IsFinite is not in net472.
    private static bool Finite( float v )
    {
      return !float.IsNaN( v ) && !float.IsInfinity( v );
    }
  }
}
