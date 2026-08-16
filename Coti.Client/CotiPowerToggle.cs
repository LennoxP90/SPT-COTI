using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The COTI's own power button.
  ///
  /// A clip-on is a separate device with its own switch, so killing the thermal while keeping night
  /// vision is faithful as well as useful.
  /// </summary>
  internal static class CotiPowerToggle
  {
    internal static bool PoweredOn { get; private set; } = true;

    private static ConfigEntry<KeyboardShortcut> _shortcut;

    internal static void Bind( ConfigEntry<KeyboardShortcut> shortcut )
    {
      _shortcut = shortcut;
    }

    /// <summary>
    /// True while every modifier of the bind is held. GoggleToggleSuppressPatch reads this to stand
    /// the goggles down for the same keypress; a bind with no modifier is never worth suppressing for.
    /// </summary>
    /// <summary>
    /// True while the bind's modifiers are held and nothing else is - the same test as
    /// KeyboardShortcut.IsDown. A looser one here suppresses the goggles on presses that never fire
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
      // IsDown also requires that no OTHER modifier is held, so the bind cannot fire as a side effect
      // of a larger combination that happens to contain it.
      if( _shortcut == null || !_shortcut.Value.IsDown() )
        return;

      PoweredOn = !PoweredOn;

      Plugin.Log.LogInfo( $"[COTI] Device powered {( PoweredOn ? "ON" : "OFF" )} by keybind" );
    }
  }
}
