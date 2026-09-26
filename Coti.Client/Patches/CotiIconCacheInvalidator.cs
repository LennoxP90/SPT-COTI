using Coti.Shared;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Forces a one-time re-render of any item icon whose contents include a COTI.
  ///
  /// GetItemIcon answers from the on-disk cache and never renders. An icon cached without the
  /// device drawn on it is filed under the same hash the correct one would use, since the hash
  /// already includes the COTI, so nothing else ever invalidates it.
  ///
  /// Dropping the hash from the file index sends GetItemIcon down its render path instead. The fresh
  /// icon is written back with saveToFile, so this costs one re-render per affected item per
  /// session and nothing thereafter.
  ///
  /// ItemIconCache.ClearIconCache() is avoided because it deletes every icon and makes the game
  /// re-render every item the player owns.
  /// </summary>
  public class CotiIconCacheInvalidator : ModulePatch
  {
    private static readonly HashSet<int> Invalidated = new HashSet<int>();

    protected override MethodBase GetTargetMethod()
    {
      return EftCompat.GetItemIconMethod();
    }

    [PatchPrefix]
    private static void Prefix( object __instance, Item item )
    {
      CotiPatchGuard.Run( "CotiIconCacheInvalidator", () => Invalidate( __instance, item ) );
    }

    private static void Invalidate( object __instance, Item item )
    {
      if( item == null )
        return;
      if( !ContainsCoti( item ) )
        return;

      var hash = EftCompat.GetItemHash( item );
      if( !Invalidated.Add( hash ) )
        return;

      var ( hadFile, hadMemory ) = EftCompat.RemoveFromIconCaches( __instance, hash );

      if( !hadFile && !hadMemory )
        return;

      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        Plugin.Log.LogInfo(
            $"[COTI] Invalidated stale icon for {item.TemplateId} (hash {hash}, " +
            $"file={hadFile} memory={hadMemory}) - it was cached without the device drawn on it" );
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
  }
}
