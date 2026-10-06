using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Tags a magnified scope bundle's own glass materials as COTI glass, once per material, as the bundle loads. EFT gives
  /// each instance of a weapon part its own copies of the prefab's materials, made with whatever tag the prefab carries
  /// then, so tagging at load covers every copy made from then on: carried, on a bot, or lying as loot. A later mode
  /// change reaches the copies already made through <see cref="Retag"/>.
  /// </summary>
  internal static class CotiScopeGlassTagger
  {
    private static readonly HashSet<Material> Tagged = new HashSet<Material>();

    /// <summary>
    /// Tags the matching materials of a loaded scope bundle's prefabs, leaving any material its dependency bundles hold or
    /// draw (shared with other items); returns how many were newly tagged.
    /// </summary>
    internal static int Tag( Object[] assets, HashSet<Material> shared, CotiGlassMode mode )
    {
      var tag = CotiRenderTypeTag.GlassTag( mode );
      var count = 0;
      // The mode prefabs were tagged with, so a raid whose mode differs (changed in the menu) sweeps the copies made at load.
      if( _mode == null )
        _mode = mode;
      foreach( var asset in assets )
      {
        if( !( asset is GameObject prefab ) || prefab == null )
          continue;
        foreach( var renderer in prefab.GetComponentsInChildren<Renderer>( true ) )
          foreach( var material in renderer.sharedMaterials )
            if( material != null && CotiScopeGlass.IsScopeGlass( material.name, renderer.gameObject.name, !shared.Contains( material ) ) && Tagged.Add( material ) )
            {
              material.SetOverrideTag( "RenderType", tag );
              count++;
            }
      }
      return count;
    }

    /// <summary>Adds every material a bundle's assets are or draw.</summary>
    internal static void CollectMaterials( Object[] assets, HashSet<Material> into )
    {
      foreach( var asset in assets )
      {
        if( asset is Material material && material != null )
          into.Add( material );
        else if( asset is GameObject prefab && prefab != null )
          foreach( var renderer in prefab.GetComponentsInChildren<Renderer>( true ) )
            foreach( var drawn in renderer.sharedMaterials )
              if( drawn != null )
                into.Add( drawn );
      }
    }

    private static CotiGlassMode? _mode;

    /// <summary>
    /// Re-tags scope glass for a new mode. The prefab materials carry it to scopes made from now on; a scope already
    /// built has copies of them (EFT gives every weapon part instance its own), each holding the tag it was copied with, so
    /// when the mode changes the live renderers are swept once for COTI glass that is not world glass.
    /// </summary>
    internal static void Retag( CotiGlassMode mode, ICollection<Material> worldGlass )
    {
      Tagged.RemoveWhere( material => material == null );
      var tag = CotiRenderTypeTag.GlassTag( mode );
      foreach( var material in Tagged )
        material.SetOverrideTag( "RenderType", tag );
      if( _mode == mode )
        return;
      var copies = _mode == null ? 0 : RetagCopies( tag, worldGlass );
      _mode = mode;
      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        Plugin.Log.LogInfo( $"[COTI] scope glass: {Tagged.Count} material(s) and {copies} live copies tagged {tag}" );
    }

    private static int RetagCopies( string tag, ICollection<Material> worldGlass )
    {
      var count = 0;
      var world = new HashSet<Material>( worldGlass );
      // Inactive renderers too: a pooled scope comes back out of the pool with whatever tag its copies hold.
      foreach( var renderer in Resources.FindObjectsOfTypeAll<Renderer>() )
        foreach( var material in renderer.sharedMaterials )
          if( material != null && !Tagged.Contains( material ) && !world.Contains( material )
              && CotiRenderTypeTag.IsGlassTag( material.GetTag( "RenderType", false ) ) && material.GetTag( "RenderType", false ) != tag )
          {
            material.SetOverrideTag( "RenderType", tag );
            count++;
          }
      return count;
    }
  }
}
