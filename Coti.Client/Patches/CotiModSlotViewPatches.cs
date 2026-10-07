using System.Linq;
using System.Reflection;
using Coti.Shared;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

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

      view._slotName.text = CotiTubes.SlotDisplayName( slot.ID, host.Layout ).ToUpper();
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
      return AccessTools.Method( typeof( ItemSpecificationPanel ), nameof( ItemSpecificationPanel.CreateModSlots ) );
    }

    [PatchPostfix]
    private static void Postfix( ItemSpecificationPanel __instance, CompoundItem compoundItem )
    {
      CotiPatchGuard.Run( "CotiSlotOrderPatch", () => Reorder( __instance._modsContainer ) );
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
