using Coti.Shared;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Rebinds the shader and starts the visibility mirror for item views built for the WORLD.
  /// CotiAttachReportPatch does the same off ContainerCollectionView.SlotView, which is inventory UI
  /// only, so in raid neither ran.
  /// </summary>
  public class CotiWorldViewPatch : ModulePatch
  {
    private const string CotiModSlotName = CotiIds.ModSlotName;

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( ObjectsFactory ), nameof( ObjectsFactory.CreateItemAsync ) );
    }

    /// <summary>
    /// The GameObject does not exist until the returned task completes, so the postfix replaces the
    /// task with one that awaits it first: a postfix on an async method sees the Task handle, not
    /// the value it will eventually produce.
    /// </summary>
    [PatchPostfix]
    private static void Postfix( Item item, ref Task<GameObject> __result )
    {
      if( item == null || __result == null )
        return;
      if( !ContainsCoti( item ) )
        return;

      __result = DressWhenReady( __result, item.TemplateId == CotiIds.TplId );
    }

    private static async Task<GameObject> DressWhenReady( Task<GameObject> inner, bool viewIsDevice )
    {
      var view = await inner;
      if( view == null )
        return null;

      Dress( view, viewIsDevice );
      return view;
    }

    /// <summary>
    /// Only ever the DEVICE is rebound, never the view around it: the host's own renderers use the
    /// same shader, and rebinding those both touches items that are not ours and corrupts the
    /// lookup - see CotiShaderRebind.GameShader.
    /// </summary>
    internal static void Dress( GameObject view, bool viewIsDevice )
    {
      if( viewIsDevice )
        CotiShaderRebind.Apply( view );

      foreach( var bone in view.GetComponentsInChildren<Transform>( includeInactive: true ) )
      {
        if( bone.name != CotiModSlotName )
          continue;

        for( var i = 0; i < bone.childCount; i++ )
        {
          var device = bone.GetChild( i ).gameObject;

          CotiShaderRebind.Apply( device );
          CotiDressMirror.Register( device, bone );
        }
      }
    }

    private static bool ContainsCoti( Item item )
    {
      foreach( var child in item.GetAllItems() )
      {
        if( child != null && child.TemplateId == CotiIds.TplId )
          return true;
      }

      return false;
    }

    /// <summary>
    /// Re-equipping goggles reattaches to the pooled view instead of building a new one, so
    /// CreateItemAsync never runs and both faults came back. CotiMountBonePatch prefixes this same
    /// method to create the bone; this is the other half, after the mods are on it.
    /// </summary>
    public class OnAttachMods : ModulePatch
    {
      protected override MethodBase GetTargetMethod()
      {
        return AccessTools.Method( typeof( ObjectsFactory ), nameof( ObjectsFactory.AttachMods ) );
      }

      [PatchPostfix]
      private static void Postfix( ContainerCollection containerCollection,
          ContainerCollectionView collectionView, ref Task __result )
      {
        if( containerCollection == null || collectionView == null || __result == null )
          return;
        if( collectionView.GameObject == null || !HasCotiSlot( containerCollection ) )
          return;

        __result = DressWhenReady( __result, collectionView.GameObject );
      }

      private static async Task DressWhenReady( Task inner, GameObject view )
      {
        await inner;

        if( view == null )
          return;

        Dress( view, viewIsDevice: false );
      }

      private static bool HasCotiSlot( ContainerCollection containerCollection )
      {
        foreach( var container in containerCollection.Containers )
        {
          if( container is Slot slot && slot.ID == CotiModSlotName )
            return true;
        }

        return false;
      }
    }
  }
}
