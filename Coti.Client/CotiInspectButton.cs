using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using Coti.Client.Patches;
using Coti.Shared;
using EFT.InventoryLogic;
using EFT.UI;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Adds a COTI Pose button to the inspect window's action row, cloned from the container's own
  /// button template so it inherits the game's styling.
  ///
  /// Three states: enabled for a night vision host with a COTI fitted, disabled for a host with
  /// an empty slot, and absent entirely for anything else.
  /// </summary>
  internal static class CotiInspectButton
  {
    private const string CotiModSlotName = CotiIds.ModSlotName;
    private const string ButtonKey = "coti_pose";
    private const string ButtonCaption = "COTI Pose";
    private const string LockedReason = "Attach a COTI to adjust its pose";

    /// <summary>Raised with the inspected host item when the button is clicked.</summary>
    public static event Action<Item> OpenRequested;

    public static void Install()
    {
      new ShowPatch().Enable();
    }

    /// <summary>Resolved by decompiling both builds; the member names differ between them.</summary>
    private sealed class ShowPatch : ModulePatch
    {
      protected override MethodBase GetTargetMethod()
      {
        return EftCompat.ItemSpecificationPanelShowMethod();
      }

      [PatchPostfix]
      private static void Postfix( object __instance )
      {
        CotiPatchGuard.Run( "CotiInspectButton", () => AddButton( __instance ) );
      }
    }

    private static void AddButton( object panel )
    {
      if( panel == null )
        return;

      // The host check comes FIRST and gates creation itself, not just interactability - a
      // weapon or any other non-host item must get no button at all, per the brief. The earlier
      // draft of this method only fed the gate into SetButtonInteraction, which left a
      // permanently-disabled button on every single item in the game.
      var item = EftCompat.InspectedItemField().GetValue( panel ) as Item;
      var gate = ResolveGate( item );
      if( gate == CotiInspectGate.NoButton )
        return;

      var container = EftCompat.InteractionButtonsContainerField().GetValue( panel );
      if( container == null )
        return;

      var template = EftCompat.ButtonTemplateField().GetValue( container );
      var buttonsContainer = EftCompat.ButtonsContainerField().GetValue( container ) as RectTransform;
      if( template == null || buttonsContainer == null )
        return;

      // autoClose:false, matching the panel's own choice for its built-in row
      // (InitInteractionButtonsPanel calls _interactionButtonsContainer.Show(..., autoClose:
      // false)) - clicking an action here does not close whatever "Close" means to this element.
      Action onClicked = () => OpenRequested?.Invoke( item );

      var button = EftCompat.CreateContextButtonMethod().Invoke( container, new object[]
      {
          ButtonKey, ButtonCaption, template, buttonsContainer,
          /* sprite */ null, onClicked, /* onMouseHover */ null,
          /* subMenu */ false, /* autoClose */ false,
      } ) as SimpleContextMenuButton;

      if( button == null )
        return;

      try
      {
        // Bound into the SAME redraw cycle the built-in buttons use - see the class comment.
        EftCompat.BindButtonMethod().Invoke( container, new object[] { button } );
      }
      catch
      {
        // BindButton is what would otherwise register this button's own teardown. If BindButton
        // itself is what threw, nothing else will ever clean this instance up - do it here rather
        // than leaving an untracked button behind, then let CotiPatchGuard log the failure.
        DestroyOrphanButton( button );
        throw;
      }

      button.SetButtonInteraction( gate == CotiInspectGate.Enabled ? SuccessfulResult.New : new FailedResult( LockedReason ) );
    }

    /// <summary>
    /// Mirrors what BindButton's own dispose action does to a button it tracked (Close(), then
    /// destroy unless it is the template's SingleInstance) - the same cleanup this button would
    /// have received on the next redraw, done immediately instead because binding it into that
    /// cycle is exactly what failed.
    /// </summary>
    private static void DestroyOrphanButton( SimpleContextMenuButton button )
    {
      try
      {
        button.Close();
      }
      finally
      {
        if( !button.SingleInstance )
          UnityEngine.Object.DestroyImmediate( button.GameObject );
      }
    }

    private static CotiInspectGate ResolveGate( Item item )
    {
      if( item == null )
        return CotiInspectGate.NoButton;

      var hosts = Plugin.Config?.NvgHosts;
      if( hosts == null )
        return CotiInspectGate.NoButton;

      var templateId = item.StringTemplateId;
      var isKnownHost = templateId != null && hosts.ContainsKey( templateId );
      var cotiSlotFilled = CotiSlotProbe.HasFilledSlot( item, CotiModSlotName );

      return CotiInspectGateResolver.Resolve( isKnownHost, cotiSlotFilled );
    }
  }
}
