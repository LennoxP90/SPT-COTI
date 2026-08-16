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
      // Nested type, so it cannot be named directly - AccessTools.Inner finds SlotView inside
      // ContainerCollectionView.
      var slotView = AccessTools.Inner( typeof( ContainerCollectionView ), "SlotView" );
      return AccessTools.Method( slotView, "InsertItem" );
    }

    [PatchPostfix]
    private static void Postfix( Item item, GameObject itemView )
    {
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
