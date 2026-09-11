using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Marks the attach state stale whenever anything moves in the player's inventory, so
  /// UpdateCotiState can hold the answer instead of re-probing the host's slots every frame.
  ///
  /// This handler rather than the night vision observer's own Changed event: that event only fires
  /// when the moved item carries a NightVisionComponent, which a COTI does not, so attaching one to
  /// goggles already worn never reaches it.
  /// </summary>
  public class CotiInventoryChangePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( Player ), nameof( Player.OnItemAddedOrRemoved ) );
    }

    [PatchPostfix]
    private static void Postfix()
    {
      // Not narrowed to COTI-shaped moves. Re-probing costs under a microsecond and only happens on
      // the next frame after an inventory event, whereas a predicate that misses one leaves the
      // device inert with nothing in the log to say why.
      CotiEquippedCoti.Invalidate();
    }
  }
}
