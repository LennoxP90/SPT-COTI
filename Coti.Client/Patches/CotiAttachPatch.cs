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
    protected override MethodBase GetTargetMethod()
    {
      return EftCompat.InsertItemMethod();
    }

    [PatchPostfix]
    private static void Postfix( Item item, GameObject itemView )
    {
      CotiPatchGuard.Run( "CotiAttachPatch", () => Attach( itemView ) );
    }

    private static void Attach( GameObject itemView )
    {
      if( itemView == null )
        return;

      var bone = itemView.transform.parent;
      if( bone == null || !CotiTubes.IsCotiSlot( bone.name ) )
        return;

      // Match the goggles' own visibility - see CotiDressMirror. The host already knows whether it
      // belongs to the wearer, so the bone's owner is never worked out here.
      CotiDressMirror.Register( itemView, bone );
      CotiShaderRebind.Apply( itemView );

      CotiDevTools.ReportAttach( itemView, bone );
    }
  }
}
