using System.Linq;
using BepInEx.Configuration;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The COTI's own power button, driving CotiPowerSequence.
  ///
  /// A clip-on is a separate device with its own switch, so killing the thermal while keeping night
  /// vision is faithful as well as useful.
  /// </summary>
  internal static class CotiPowerToggle
  {
    private static readonly CotiPowerTimings Timings = new CotiPowerTimings();
    private static readonly CotiPowerSequence Sequence = new CotiPowerSequence( Timings );

    private static ConfigEntry<KeyboardShortcut> _shortcut;

    /// <summary>
    /// This frame's power state. Read by CotiState, the compositor and the click.
    /// </summary>
    internal static CotiPowerFrame Frame { get; private set; } = CotiPowerFrame.Steady( CotiPowerPhase.On );

    internal static bool PoweredOn => Frame.ThermalOn;

    internal static void Bind( ConfigEntry<KeyboardShortcut> shortcut )
    {
      _shortcut = shortcut;
    }

    /// <summary>
    /// True while the bind's modifiers are held and nothing else is - the same test as
    /// KeyboardShortcut.IsDown. A looser test would suppress the goggles on presses that never fire
    /// the toggle, swallowing the key.
    /// </summary>
    internal static bool ModifierHeld
    {
      get
      {
        if( _shortcut == null )
          return false;

        var modifiers = _shortcut.Value.Modifiers;
        var any = false;

        foreach( var modifier in modifiers )
        {
          if( !Input.GetKey( modifier ) )
            return false;

          any = true;
        }

        if( !any )
          return false;

        foreach( var candidate in ModifierKeys )
        {
          if( Input.GetKey( candidate ) && !modifiers.Contains( candidate ) )
            return false;
        }

        return true;
      }
    }

    private static readonly KeyCode[] ModifierKeys =
    {
      KeyCode.LeftControl, KeyCode.RightControl,
      KeyCode.LeftShift, KeyCode.RightShift,
      KeyCode.LeftAlt, KeyCode.RightAlt,
    };

    internal static void Tick()
    {
      ApplySettings();

      var now = (double)Time.realtimeSinceStartup;

      // IsDown also requires that no other modifier is held, so the bind cannot fire as a side effect
      // of a larger combination that happens to contain it.
      if( _shortcut != null && _shortcut.Value.IsDown() )
      {
        Sequence.Press( now );
        Plugin.Log.LogInfo( $"[COTI] Power toggle pressed - {Sequence.Phase}" );
      }

      var previous = Frame.Phase;
      Frame = Sequence.Advance( now );

      if( Frame.Phase != previous && ( Plugin.Config?.VerboseLogging ?? false ) )
        Plugin.Log.LogInfo( $"[COTI] Power {previous} -> {Frame.Phase}" );
    }

    private static void ApplySettings()
    {
      var settings = Plugin.Config?.PowerSequence;
      if( settings == null )
        return;

      settings.CopyTo( Timings );
      Sequence.Enabled = settings.Enabled;
    }
  }
}
