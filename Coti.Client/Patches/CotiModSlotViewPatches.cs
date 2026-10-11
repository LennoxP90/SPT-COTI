using System.Linq;
using System.Reflection;
using Coti.Shared;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
#if SPT40
using ItemContext = ItemContextAbstractClass;
using ItemController = TraderControllerClass;
using TMPro;
#endif

namespace Coti.Client.Patches
{
  /// <summary>
  /// Labels the home slot "ECOTI R" on a host whose home is tube_2. The locale gives mod_coti one label for every
  /// host, "ECOTI", which is right only for a mono or a v1 device.
  /// </summary>
  public class CotiSlotLabelPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( ModSlotView ), nameof( ModSlotView.Show ),
          new[] { typeof( Slot ), typeof( ItemContext ), typeof( ItemController ), typeof( ItemUiContext ) } );
    }

#if SPT40
    // 4.0's Assembly-CSharp is not publicized.
    private static readonly AccessTools.FieldRef<ModSlotView, TextMeshProUGUI> SlotName =
        AccessTools.FieldRefAccess<ModSlotView, TextMeshProUGUI>( "_slotName" );
#endif

    [PatchPostfix]
    private static void Postfix( ModSlotView __instance, Slot slot )
    {
      CotiPatchGuard.Run( "CotiSlotLabelPatch", () => Relabel( __instance, slot ) );
    }

    private static void Relabel( ModSlotView view, Slot slot )
    {
      if( slot?.ID != CotiIds.ModSlotName || slot.ParentItem == null )
        return;

      CotiNvgHostConfig host;
      if( Plugin.Config?.NvgHosts == null || !Plugin.Config.NvgHosts.TryGetValue( slot.ParentItem.StringTemplateId, out host ) || host == null )
        return;

#if SPT40
      SlotName( view ).text = CotiTubes.SlotDisplayName( slot.ID, host.Layout ).ToUpper();
#else
      view._slotName.text = CotiTubes.SlotDisplayName( slot.ID, host.Layout ).ToUpper();
#endif
    }
  }

  /// <summary>
  /// Shows a host's COTI slots left to right as their tubes sit (OL, L, R, OR). The template keeps the auto-pick
  /// order, because EFT fills the first free slot in template order and a single COTI must land on home; only the
  /// views move, within the positions they already hold.
  /// </summary>
  public class CotiSlotOrderPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
#if SPT40
      // CreateModSlots is unnamed in 4.0; method_17 is the one taking the CompoundItem and instantiating the views.
      return AccessTools.Method( typeof( ItemSpecificationPanel ), "method_17", new[] { typeof( CompoundItem ) } );
#else
      return AccessTools.Method( typeof( ItemSpecificationPanel ), nameof( ItemSpecificationPanel.CreateModSlots ) );
#endif
    }

#if SPT40
    private static readonly AccessTools.FieldRef<ItemSpecificationPanel, RectTransform> ModsContainer =
        AccessTools.FieldRefAccess<ItemSpecificationPanel, RectTransform>( "_modsContainer" );
#endif

    [PatchPostfix]
    private static void Postfix( ItemSpecificationPanel __instance, CompoundItem compoundItem )
    {
#if SPT40
      CotiPatchGuard.Run( "CotiSlotOrderPatch", () => Reorder( ModsContainer( __instance ) ) );
#else
      CotiPatchGuard.Run( "CotiSlotOrderPatch", () => Reorder( __instance._modsContainer ) );
#endif
    }

    // The panel lists the slots of every attached item too (a helmet shows its goggles' slots), so each host's COTI
    // views are sorted within the positions that host's views hold, wherever those are.
    private static void Reorder( Transform container )
    {
      if( container == null )
        return;

      var children = container.Cast<Transform>().ToList();
      var hosts = children
          .Select( ( child, index ) => new { index, view = child.GetComponent<ModSlotView>() } )
          .Where( c => c.view != null && c.view.Slot != null && CotiTubes.IsCotiSlot( c.view.Slot.ID ) )
          .GroupBy( c => c.view.Slot.ParentItem )
          .Where( g => g.Count() > 1 )
          .ToList();

      if( hosts.Count == 0 )
        return;

      foreach( var host in hosts )
      {
        var sorted = host.Select( c => c.view.transform ).OrderBy( t => CotiTubes.DisplayRank( t.GetComponent<ModSlotView>().Slot.ID ) );
        foreach( var pair in host.Select( c => c.index ).Zip( sorted, ( index, view ) => new { index, view } ) )
          children[pair.index] = pair.view;
      }

      for( var i = 0; i < children.Count; i++ )
        children[i].SetSiblingIndex( i );
    }
  }
}
