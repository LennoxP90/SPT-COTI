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
    /// EFT does NOT require an exact modifier match on its keybinds: holding Ctrl or Alt and pressing
    /// N still fires ToggleGoggles. The player tried both and got night vision each time. Since input
    /// reaches the game before any plugin can consume it, the only way to keep a modifier+N binding
    /// for the COTI is to make the game's handler stand down for that one keypress.
    ///
    /// Deliberately narrow. It patches EFT.Player.ToggleGoggles and it only declines the call
    /// while the configured modifier is physically down, so an ordinary N is completely unaffected.
    /// </summary>
    public class GoggleToggleSuppressPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(Player),
                nameof(Player.method_15));
        }
        
      return AccessTools.Method( typeof( Player ), nameof( Player.method_15 ) );
    }

        [PatchPrefix]
        private static bool Prefix(Player __instance)
        {
            // Only ever suppress for the local player. A remote or AI player's goggles have nothing to
            // do with what is held down on this keyboard.
            if (__instance == null || !__instance.IsYourPlayer)
                return true;

            if (!CotiPowerToggle.ModifierHeld)
                return true;

            // Returning false skips the original: the goggles stay as they are and CotiPowerToggle
            // handles the same keypress as a COTI power toggle instead.
            return false;
        }
    }
}
