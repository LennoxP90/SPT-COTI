using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Suppresses the game's own goggle toggle while the COTI's modifier is held, so a modifier+key
  /// binding can exist at all.
  ///
  /// EFT does not require an exact modifier match on its keybinds: holding Ctrl or Alt and pressing
  /// N still fires ToggleGoggles. Input reaches the game before any plugin can consume it, so the
  /// game's handler has to stand down for that one keypress.
  ///
  /// It patches EFT.Player.ToggleGoggles and only declines the call while the configured modifier
  /// is physically down, so an ordinary N is unaffected.
  /// </summary>
  public class GoggleToggleSuppressPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return EftCompat.ToggleGogglesMethod();
    }

    [PatchPrefix]
    private static bool Prefix( Player __instance )
    {
      // Fails open: if this throws, the goggles toggle as they normally would rather than night
      // vision silently ignoring its own keybind.
      return CotiPatchGuard.Run( "GoggleToggleSuppressPatch", () => ShouldRunOriginal( __instance ), onFailure: true );
    }

    private static bool ShouldRunOriginal( Player __instance )
    {
      // Only the local player's goggles are affected by what is held down on this keyboard.
      if( __instance == null || !__instance.IsYourPlayer )
        return true;

      if( !CotiPowerToggle.ModifierHeld )
        return true;

      // Returning false skips the original: the goggles stay as they are and CotiPowerToggle
      // handles the same keypress as a COTI power toggle instead.
      return false;
    }
  }
}
