using Coti.Shared;
using System.Reflection;
using Coti.Client.Dev;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Matches the device's visibility to the goggles' and rebinds its shader, at the instant EFT
  /// parents it to the mount bone.
  ///
  /// Hooks ContainerCollectionView.SlotView.InsertItem, the only synchronous point that sees both the
  /// bone and the finished model. ObjectsFactory.AttachMods is async - a postfix there runs long
  /// before the model exists.
  /// </summary>
  public class CotiAttachPatch : ModulePatch
  {
    private const string CotiModSlotName = CotiIds.ModSlotName;

    protected override MethodBase GetTargetMethod()
    {
      // Nested type, so it cannot be named directly - AccessTools.Inner finds SlotView (GClass769)
      // inside ContainerCollectionView (GClass768).
      var slotView = AccessTools.Inner( typeof( GClass768 ), "GClass769" );
      return AccessTools.Method( slotView, "InsertItem" );
    }

    // Positional (__0/__1), not by name: InsertItem's own parameter names don't survive
    // obfuscation, so Harmony can only bind these by position.
    [PatchPostfix]
    private static void Postfix( Item __0, GameObject __1 )
    {
      var itemView = __1;

      if( itemView == null )
        return;

      var bone = itemView.transform.parent;
      if( bone == null || bone.name != CotiModSlotName )
        return;

      // Match the goggles' own visibility - see CotiDressMirror. This replaces three failed attempts
      // to work out whether the bone belonged to the wearer; the host already knows.
      CotiDressMirror.Register( itemView, bone );
      CotiShaderRebind.Apply( itemView );

      CotiDevTools.ReportAttach( itemView, bone );
    }
  }
}
