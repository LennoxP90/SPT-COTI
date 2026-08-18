using Coti.Shared;
using System.Reflection;
using Coti.Client.Dev;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Creates the mod_coti attachment point on host NVGs.
  ///
  /// EFT attaches a mod by finding a TRANSFORM whose name matches the slot id. Host meshes are baked
  /// with mod_nvg/mod_scope/mod_mount and nothing else, so an invented slot matches no bone - and a
  /// failed lookup skips AddBone, after which the mod's prefab is never created at all. The model
  /// loads perfectly and has nowhere to go.
  ///
  /// EFT does not care where the bone came from, only that it exists and is named correctly.
  /// </summary>
  public class CotiMountBonePatch : ModulePatch
  {
    private const string CotiModSlotName = CotiIds.ModSlotName;

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

      if( !HasCotiSlot( containerCollection ) )
        return;

      var templateId = EftCompat.ContainerTemplateId( containerCollection );
      var host = GetNvgHostConfig( templateId );

      // An existing bone is REUSED AND REPOSITIONED rather than skipped. Host GameObjects come
      // from an object pool, so a bone created earlier outlives the item view it was made for -
      // and if the pose were only applied at creation, a config change would appear to do
      // nothing until the pool happened to hand out a fresh instance. Re-applying every time
      // makes mount tuning a config edit rather than a client relaunch.
      var existing = EftCompat.FindTransformRecursive( root, CotiModSlotName, ignoreCase: true );

      CotiDevTools.ReportHostBones( templateId, root );

      var anchor = ResolveAnchor( root, host );

      var bone = existing != null ? existing.gameObject : new GameObject( CotiModSlotName );

      // SetParent even when reusing: the configured anchor bone may have changed.
      bone.transform.SetParent( anchor, worldPositionStays: false );

      // Inherit the anchor's layer, and push it through anything already attached.
      SetLayerRecursively( bone, anchor.gameObject.layer );

      CotiMountPose.Apply( bone.transform, host );
      CotiDevTools.OnMountPosed( bone.transform, host, templateId, gameObject.name );

      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        Plugin.Log.LogInfo(
            $"[COTI] Created {CotiModSlotName} on {gameObject.name} " +
            $"(host {templateId}) under '{anchor.name}' " +
            $"at {bone.transform.localPosition}" );
      }
    }

    private static void SetLayerRecursively( GameObject target, int layer )
    {
      target.layer = layer;

      for( var i = 0; i < target.transform.childCount; i++ )
      {
        SetLayerRecursively( target.transform.GetChild( i ).gameObject, layer );
      }
    }

    private static bool HasCotiSlot( object containerCollection )
    {
      foreach( var container in EftCompat.Containers( containerCollection ) )
      {
        if( container is Slot slot && slot.ID == CotiModSlotName )
          return true;
      }

      return false;
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
    /// The configured bone if it exists, otherwise the host's root. Falling back rather than
    /// giving up matters: a typo or a bone name that differs between hosts would otherwise take
    /// the device back to invisible, and a COTI in the wrong place is a far better failure than
    /// no COTI at all - it is visible, so it can be diagnosed.
    /// </summary>
    private static Transform ResolveAnchor( Transform root, CotiNvgHostConfig host )
    {
      if( host == null || string.IsNullOrEmpty( host.MountAnchorBone ) )
        return root;

      var anchor = EftCompat.FindTransformRecursive( root, host.MountAnchorBone, ignoreCase: true );
      if( anchor != null )
        return anchor;

      Plugin.Log.LogWarning(
          $"[COTI] Anchor bone '{host.MountAnchorBone}' not found on {root.name} - using the root instead" );

      return root;
    }
  }
}
