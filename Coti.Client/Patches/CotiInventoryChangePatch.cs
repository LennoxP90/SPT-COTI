using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Marks the local player's attach state stale when an item moves into or out of a slot on them, so UpdateCotiState
  /// can keep its answer instead of re-probing the host's slots every frame.
  ///
  /// Other players are skipped because both caches describe the local player's goggles. Grid and stack-slot moves are
  /// skipped because nothing worn or attached sits at those addresses: a move that changes what is worn always raises
  /// its other event with a slot address. The game's own goggles observer relies on the same rule.
  /// The filter is not narrowed further to COTI-shaped moves. A predicate that missed one would leave the device inert
  /// with nothing in the log.
  ///
  /// This patch hooks the inventory handler, not the night vision observer's Changed event. That event fires only when
  /// the moved item carries a NightVisionComponent. A COTI does not, so attaching one to goggles already worn would
  /// never reach it.
  /// </summary>
  public class CotiInventoryChangePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( Player ), nameof( Player.OnItemAddedOrRemoved ) );
    }

    [PatchPostfix]
    private static void Postfix( Player __instance, ItemAddress location )
    {
      if( !__instance.IsYourPlayer || location is GridItemAddress || location is StackSlotItemAddress )
        return;

      CotiEquippedCoti.Invalidate();
      CotiPodWatch.Invalidate();
    }
  }
}
