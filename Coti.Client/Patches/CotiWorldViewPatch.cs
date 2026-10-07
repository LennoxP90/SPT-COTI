using Coti.Shared;
using System;
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
  /// Rebinds the shader and starts the visibility mirror for item views built for the world.
  /// CotiAttachPatch does the same off ContainerCollectionView.SlotView, which is inventory UI
  /// only and never runs in raid.
  /// </summary>
  public class CotiWorldViewPatch : ModulePatch
  {
    private const string Site = "CotiWorldViewPatch";
    private static readonly MongoID CotiTemplate = new MongoID( CotiIds.TplId );

    protected override MethodBase GetTargetMethod()
    {
      return EftCompat.CreateItemAsyncMethod();
    }

    /// <summary>
    /// The GameObject does not exist until the returned task completes, so the postfix replaces the
    /// task with one that awaits it first: a postfix on an async method sees the Task handle, not
    /// the value it will eventually produce.
    /// </summary>
    [PatchPostfix]
    private static void Postfix( Item item, ref Task<GameObject> __result )
    {
      if( __result == null || !CarriesCoti( item, Site ) )
        return;

      var replacement = __result;
      var viewIsDevice = item.TemplateId == CotiTemplate;
      CotiPatchGuard.Run( Site, () => replacement = DressWhenReady( replacement, viewIsDevice ) );
      __result = replacement;
    }

    /// <summary>
    /// Whether the item is a COTI or holds one through its slots, the only place a COTI is drawn: grid contents are
    /// never part of a view or an icon. Allocation-free, and it runs outside the guard so an item without a COTI costs
    /// nothing more. A fault is reported through the guard and answers false, which leaves the game's result alone.
    /// </summary>
    internal static bool CarriesCoti( Item item, string site )
    {
      try
      {
        return Carries( item );
      }
      catch( Exception ex )
      {
        ReportPrefilterFault( site, ex );
        return false;
      }
    }

    private static bool Carries( Item item )
    {
      if( item == null )
        return false;
      if( item.TemplateId == CotiTemplate )
        return true;

      var slots = ( item as CompoundItem )?.Slots;
      for( var i = 0; slots != null && i < slots.Length; i++ )
      {
        if( slots[i] != null && Carries( slots[i].ContainedItem ) )
          return true;
      }

      return false;
    }

    /// <summary>Logs a prefilter fault once per site, the way a guarded body's fault is logged.</summary>
    private static void ReportPrefilterFault( string site, Exception ex )
    {
      CotiPatchGuard.Run( site, () => throw new InvalidOperationException( site + " prefilter failed", ex ) );
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
    /// Only the device is rebound, never the view around it: the host's own renderers use the
    /// same shader, and rebinding those touches other items and corrupts the lookup. See
    /// CotiShaderRebind.GameShader.
    /// </summary>
    internal static void Dress( GameObject view, bool viewIsDevice )
    {
      if( viewIsDevice )
        CotiShaderRebind.Apply( view );

      foreach( var bone in view.GetComponentsInChildren<Transform>( includeInactive: true ) )
      {
        if( !CotiTubes.IsCotiSlot( bone.name ) )
          continue;

        for( var i = 0; i < bone.childCount; i++ )
        {
          var device = bone.GetChild( i ).gameObject;

          CotiShaderRebind.Apply( device );
          CotiDressMirror.Register( device, bone );
        }
      }
    }

    /// <summary>
    /// Re-equipping goggles reattaches to the pooled view instead of building a new one, so
    /// CreateItemAsync never runs. CotiMountBonePatch prefixes this same method to create the bone;
    /// this is the other half, after the mods are on it.
    /// </summary>
    public class OnAttachMods : ModulePatch
    {
      private const string Site = "CotiWorldViewPatch.Mods";

      protected override MethodBase GetTargetMethod()
      {
        return EftCompat.AttachModsMethod();
      }

      [PatchPostfix]
      private static void Postfix( object containerCollection, object collectionView, ref Task __result )
      {
        if( __result == null || collectionView == null || !HasCotiSlot( containerCollection ) )
          return;

        var replacement = __result;
        CotiPatchGuard.Run( Site, () => replacement = WrapResult( collectionView, replacement ) );
        __result = replacement;
      }

      private static Task WrapResult( object collectionView, Task result )
      {
        var gameObject = EftCompat.ViewGameObject( collectionView );
        return gameObject == null ? result : DressWhenReady( result, gameObject );
      }

      private static async Task DressWhenReady( Task inner, GameObject view )
      {
        await inner;

        if( view == null )
          return;

        Dress( view, viewIsDevice: false );
      }

      /// <summary>Every COTI slot is one of the item's Slots: the patcher and the server injector add them nowhere else.</summary>
      private static bool HasCotiSlot( object containerCollection )
      {
        try
        {
          var slots = ( containerCollection as CompoundItem )?.Slots;
          for( var i = 0; slots != null && i < slots.Length; i++ )
          {
            if( slots[i] != null && CotiTubes.IsCotiSlot( slots[i].ID ) )
              return true;
          }

          return false;
        }
        catch( Exception ex )
        {
          ReportPrefilterFault( Site, ex );
          return false;
        }
      }
    }
  }
}
