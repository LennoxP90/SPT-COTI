using Coti.Shared;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Creates the attachment point of every COTI slot on host NVGs: one bone per slot, each posed
  /// from its own tube's mount.
  ///
  /// EFT attaches a mod by finding a transform whose name matches the slot id. Host meshes are baked
  /// with mod_nvg/mod_scope/mod_mount only, so a new slot matches no bone, and a failed lookup skips
  /// AddBone so the mod's prefab is never created.
  ///
  /// EFT only requires that the bone exists and is named correctly.
  /// </summary>
  public class CotiMountBonePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return EftCompat.AttachModsMethod();
    }

    /// <summary>
    /// A prefix on an async method runs before the state machine starts, which is the window
    /// needed: this work is synchronous and only has to finish before AttachMods' first loop.
    /// </summary>
    [PatchPrefix]
    private static void Prefix( object containerCollection, object collectionView )
    {
      CotiPatchGuard.Run( "CotiMountBonePatch", () => Mount( containerCollection, collectionView ) );
    }

    private static void Mount( object containerCollection, object collectionView )
    {
      if( containerCollection == null || collectionView == null )
        return;

      var gameObject = EftCompat.ViewGameObject( collectionView );
      var root = gameObject == null ? null : gameObject.transform;
      if( root == null )
        return;

      // The slots the item really has, not the ones its device file lists: a COTI left in a slot the
      // file no longer gives still gets a bone, at the legacy mount.
      var slotNames = CotiSlotNames( containerCollection );
      if( slotNames.Count == 0 )
        return;

      var templateId = EftCompat.ContainerTemplateId( containerCollection );
      var host = GetNvgHostConfig( templateId );

#if SPT40
      CotiPoseTuner.ReportHostBones( templateId, root );
#endif

      foreach( var slotName in slotNames )
      {
        var bone = PlaceBone( root, slotName, host == null ? null : host.MountForSlot( slotName ) );

#if SPT40
        // The tuner edits the legacy mount. On a multi-tube host that would undo the home tube's
        // own pose, so only a v1 host hands its bone over.
        if( slotName == CotiIds.ModSlotName && ( host == null || host.Layout == null ) )
          CotiPoseTuner.OnMountPosed( bone, host, templateId, gameObject.name, root );
#endif

        if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        {
          Plugin.Log.LogInfo(
              $"[COTI] Created {slotName} on {gameObject.name} " +
              $"(host {templateId}) under '{bone.parent.name}' " +
              $"at {bone.localPosition}" );
        }
      }
    }

    /// <summary>
    /// An existing bone is reused and repositioned rather than skipped. Host GameObjects come
    /// from an object pool, so a bone outlives the item view it was made for, and a pose applied
    /// only at creation would ignore config changes until the pool handed out a fresh instance.
    /// Re-applying every time makes mount tuning a config edit rather than a client relaunch.
    /// </summary>
    private static Transform PlaceBone( Transform root, string slotName, CotiMountBlock mount )
    {
      var existing = EftCompat.FindTransformRecursive( root, slotName, ignoreCase: true );
      var anchor = ResolveAnchor( root, mount == null ? null : mount.AnchorBone );
      var bone = existing != null ? existing.gameObject : new GameObject( slotName );

      // SetParent even when reusing: the configured anchor bone may have changed.
      bone.transform.SetParent( anchor, worldPositionStays: false );

      // Inherit the anchor's layer, and push it through anything already attached.
      SetLayerRecursively( bone, anchor.gameObject.layer );

      CotiMountPose.Apply( bone.transform, mount );
      return bone.transform;
    }

    private static void SetLayerRecursively( GameObject target, int layer )
    {
      target.layer = layer;

      for( var i = 0; i < target.transform.childCount; i++ )
      {
        SetLayerRecursively( target.transform.GetChild( i ).gameObject, layer );
      }
    }

    private static List<string> CotiSlotNames( object containerCollection )
    {
      var names = new List<string>();

      foreach( var container in EftCompat.Containers( containerCollection ) )
      {
        if( container is Slot slot && CotiTubes.IsCotiSlot( slot.ID ) )
          names.Add( slot.ID );
      }

      return names;
    }

    private static CotiNvgHostConfig GetNvgHostConfig( string templateId )
    {
      var hosts = Plugin.Config == null ? null : Plugin.Config.NvgHosts;
      if( hosts == null || templateId == null )
        return null;

      CotiNvgHostConfig host;
      return hosts.TryGetValue( templateId, out host ) ? host : null;
    }

    /// <summary>
    /// The named bone if it exists, otherwise the host's root. A typo or a bone name that
    /// differs between hosts then leaves the COTI visible in the wrong place, which can be
    /// diagnosed, rather than invisible.
    /// </summary>
    internal static Transform ResolveAnchor( Transform root, string anchorBone )
    {
      if( string.IsNullOrEmpty( anchorBone ) )
        return root;

      var anchor = EftCompat.FindTransformRecursive( root, anchorBone, ignoreCase: true );
      if( anchor != null )
        return anchor;

      Plugin.Log.LogWarning(
          $"[COTI] Anchor bone '{anchorBone}' not found on {root.name} - using the root instead" );

      return root;
    }
  }
}
