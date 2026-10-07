using System.Linq;
using BepInEx.Configuration;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The COTI's own power and mode buttons, driving CotiPowerSequence.
  ///
  /// A clip-on is a separate device with its own switch, so killing the thermal while keeping night
  /// vision is faithful as well as useful.
  /// </summary>
  internal static class CotiPowerToggle
  {
    private static readonly CotiPowerTimings Timings = new CotiPowerTimings();
    private static readonly CotiPowerSequence Sequence = new CotiPowerSequence( Timings );

    private static ConfigEntry<KeyboardShortcut> _shortcut;
    private static ConfigEntry<KeyboardShortcut> _modeShortcut;
    private static ConfigEntry<CotiThermalMode> _mode;

    /// <summary>
    /// This frame's power state. Read by CotiState, the compositor and the click.
    /// </summary>
    internal static CotiPowerFrame Frame { get; private set; } = CotiPowerFrame.Steady( CotiPowerPhase.On );

    internal static bool PoweredOn => Frame.ThermalOn;

    internal static void Bind(
        ConfigEntry<KeyboardShortcut> shortcut, ConfigEntry<KeyboardShortcut> modeShortcut, ConfigEntry<CotiThermalMode> mode )
    {
      _shortcut = shortcut;
      _modeShortcut = modeShortcut;
      _mode = mode;
    }

    /// <summary>
    /// True while either bind's modifiers are held and nothing else is - the same test as
    /// KeyboardShortcut.IsDown. A looser test would suppress the goggles on presses that never fire
    /// a COTI bind, swallowing the key.
    /// </summary>
    internal static bool ModifierHeld => Held( _shortcut ) || Held( _modeShortcut );

    private static bool Held( ConfigEntry<KeyboardShortcut> shortcut )
    {
      if( shortcut == null )
        return false;

      var modifiers = shortcut.Value.Modifiers;
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
        if( Plugin.Config?.VerboseLogging ?? false )
          Plugin.Log.LogInfo( $"[COTI] Power toggle pressed - {Sequence.Phase}" );
      }

      if( _modeShortcut != null && _mode != null && _modeShortcut.Value.IsDown() )
      {
        _mode.Value = CotiThermalModes.Next( _mode.Value );
        Sequence.ShowMode( now );
        if( Plugin.Config?.VerboseLogging ?? false )
          Plugin.Log.LogInfo( $"[COTI] Mode toggle pressed - {_mode.Value}" );
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
