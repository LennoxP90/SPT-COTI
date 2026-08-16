using Coti.Shared;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Forces a one-time re-render of any item icon whose contents include a COTI, because the cached
  /// image predates the device being renderable and nothing else will ever invalidate it.
  ///
  /// GetItemIcon answers from the on-disk cache and never renders. Those icons were written when
  /// mod_coti had no bone and the device could not attach - and the hash already included the COTI,
  /// so the stale picture is filed under exactly the hash the correct one would use. It is
  /// self-perpetuating, and nothing else will ever invalidate it.
  ///
  /// Dropping the hash from the file index sends GetItemIcon down its render path instead. The fresh
  /// icon is written back with saveToFile, so this costs one re-render per affected item per
  /// session and nothing thereafter.
  ///
  /// Deliberately NOT ItemIconCache.ClearIconCache(): that deletes all icons and makes the game
  /// re-render every item the player owns, to fix the handful that are wrong.
  /// </summary>
  public class CotiIconCacheInvalidator : ModulePatch
  {
    private static readonly HashSet<int> Invalidated = new HashSet<int>();

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( ItemIconCreator ), nameof( ItemIconCreator.GetItemIcon ) );
    }

    [PatchPrefix]
    private static void Prefix( ItemIconCreator __instance, Item item )
    {
      if( item == null )
        return;
      if( !ContainsCoti( item ) )
        return;

      var hash = IconsHash.GetItemHash( item );
      if( !Invalidated.Add( hash ) )
        return;

      var hadFile = __instance._fileCacheIndex.Remove( hash );
      var hadMemory = __instance._memoryCacheIndex.Remove( hash );

      if( !hadFile && !hadMemory )
        return;

      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        Plugin.Log.LogInfo(
            $"[COTI] Invalidated stale icon for {item.TemplateId} (hash {hash}, " +
            $"file={hadFile} memory={hadMemory}) - it predates the device being renderable" );
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
