using System.Collections.Generic;
using System.Diagnostics;
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
    private const string RenderType = "RenderType";
    private static readonly HashSet<Material> Tagged = new HashSet<Material>();
    // Every material any raid this session found to be world glass; a copy sweep never touches one.
    private static readonly HashSet<int> WorldGlassIds = new HashSet<int>();
    private static readonly List<Renderer> Renderers = new List<Renderer>();
    private static readonly List<Material> Materials = new List<Material>();
    private static readonly List<CollimatorSight> Collimators = new List<CollimatorSight>();
    // The built-in red dot lenses of the prefab being tagged, as boxes in its root's frame.
    private static readonly List<Bounds> Lenses = new List<Bounds>();

    // The mode the copies made so far hold; mixed once a scope loaded under another mode since the last sweep.
    private static CotiGlassMode? _mode;
    private static bool _mixed;

    /// <summary>
    /// Tags the matching materials of a loaded scope bundle's prefabs, leaving any material its dependency bundles hold or
    /// draw (shared with other items); returns how many were newly tagged.
    /// </summary>
    internal static int Tag( Object[] assets, HashSet<int> shared, CotiGlassMode mode )
    {
      if( _mode == null )
        _mode = mode;
      else if( mode != _mode )
        _mixed = true;

      var tag = CotiRenderTypeTag.GlassTag( mode );
      var count = 0;
      foreach( var asset in assets )
      {
        if( !( asset is GameObject prefab ) || prefab == null )
          continue;
        var root = prefab.transform;
        FindLenses( root );
        prefab.GetComponentsInChildren( true, Renderers );
        foreach( var renderer in Renderers )
        {
          renderer.GetSharedMaterials( Materials );
          string objectName = null;
          for( var index = 0; index < Materials.Count; index++ )
          {
            var material = Materials[index];
            if( material == null || Tagged.Contains( material ) || shared.Contains( material.GetInstanceID() ) )
              continue;
            if( objectName == null )
              objectName = renderer.gameObject.name;
            if( !CotiScopeGlass.IsScopeGlass( material.name, objectName, true ) )
              continue;
            if( Lenses.Count > 0 && IsRedDotWindow( root, renderer, objectName, index ) )
            {
              if( Plugin.Config != null && Plugin.Config.VerboseLogging )
                Plugin.Log.LogInfo( $"[COTI] scope glass: {material.name} in {prefab.name} left untagged, it is the red dot's window" );
              continue;
            }
            material.SetOverrideTag( RenderType, tag );
            Tagged.Add( material );
            count++;
          }
        }
      }
      Renderers.Clear();
      Materials.Clear();
      Lenses.Clear();
      return count;
    }

    // Each built-in red dot lens (a CollimatorSight's flat mesh) of a prefab, as a box in its root's frame.
    private static void FindLenses( Transform root )
    {
      Lenses.Clear();
      root.GetComponentsInChildren( true, Collimators );
      foreach( var sight in Collimators )
      {
        var lens = sight.CollimatorMeshRenderer != null ? (Component)sight.CollimatorMeshRenderer : sight;
        var filter = lens.GetComponent<MeshFilter>();
        if( filter != null && filter.sharedMesh != null )
          Lenses.Add( InRoot( root, filter.transform, filter.sharedMesh.bounds ) );
      }
      Collimators.Clear();
    }

    // Front glass outside the magnifier's node whose submesh sits on a red dot lens's line of sight: the window of a
    // red dot paired with a magnifier, which must stay see-through. Material i draws submesh i, the last one past the end.
    private static bool IsRedDotWindow( Transform root, Renderer renderer, string objectName, int index )
    {
      var filter = renderer.GetComponent<MeshFilter>();
      var mesh = filter != null ? filter.sharedMesh : null;
      if( mesh == null || mesh.subMeshCount == 0 )
        return false;
      var glass = InRoot( root, renderer.transform, mesh.GetSubMesh( Mathf.Min( index, mesh.subMeshCount - 1 ) ).bounds );
      var covers = false;
      foreach( var lens in Lenses )
        covers |= CotiScopeGlass.CoversLens( Corner( glass.min ), Corner( glass.max ), Corner( lens.min ), Corner( lens.max ) );
      var underOptic = renderer.GetComponentInParent<EFT.CameraControl.OpticSight>( true ) != null;
      return CotiScopeGlass.IsRedDotWindow( objectName, underOptic, covers );
    }

    // A mesh-space box carried into the root's frame: the box around the transformed one.
    private static Bounds InRoot( Transform root, Transform node, Bounds local )
    {
      var m = root.worldToLocalMatrix * node.localToWorldMatrix;
      var e = local.extents;
      var size = new Vector3(
        Mathf.Abs( m.m00 ) * e.x + Mathf.Abs( m.m01 ) * e.y + Mathf.Abs( m.m02 ) * e.z,
        Mathf.Abs( m.m10 ) * e.x + Mathf.Abs( m.m11 ) * e.y + Mathf.Abs( m.m12 ) * e.z,
        Mathf.Abs( m.m20 ) * e.x + Mathf.Abs( m.m21 ) * e.y + Mathf.Abs( m.m22 ) * e.z );
      return new Bounds( m.MultiplyPoint3x4( local.center ), 2f * size );
    }

    private static float[] Corner( Vector3 v )
    {
      return new[] { v.x, v.y, v.z };
    }

    /// <summary>Adds the instance id of every material a bundle's assets are or draw.</summary>
    internal static void CollectMaterials( Object[] assets, HashSet<int> into )
    {
      foreach( var asset in assets )
      {
        if( asset is Material material && material != null )
          into.Add( material.GetInstanceID() );
        else if( asset is GameObject prefab && prefab != null )
        {
          prefab.GetComponentsInChildren( true, Renderers );
          foreach( var renderer in Renderers )
          {
            renderer.GetSharedMaterials( Materials );
            foreach( var drawn in Materials )
              if( drawn != null )
                into.Add( drawn.GetInstanceID() );
          }
        }
      }
      Renderers.Clear();
      Materials.Clear();
    }

    /// <summary>
    /// Re-tags scope glass for a new mode. The prefab materials carry it to scopes made from now on; a scope already
    /// built has copies of them, each holding the tag it was copied with, so when the mode changes the loaded materials
    /// are swept once for COTI glass that has never been world glass.
    /// </summary>
    internal static void Retag( CotiGlassMode mode, List<Material> worldGlass )
    {
      foreach( var material in worldGlass )
        if( material != null )
          WorldGlassIds.Add( material.GetInstanceID() );
      Tagged.RemoveWhere( material => material == null );
      var tag = CotiRenderTypeTag.GlassTag( mode );
      foreach( var material in Tagged )
        material.SetOverrideTag( RenderType, tag );
      if( _mode == mode && !_mixed )
        return;

      var verbose = Plugin.Config != null && Plugin.Config.VerboseLogging;
      var started = verbose ? Stopwatch.GetTimestamp() : 0L;
      var copies = _mode == null ? 0 : RetagCopies( tag );
      _mode = mode;
      _mixed = false;
      if( verbose )
        Plugin.Log.LogInfo( $"[COTI] scope glass: {Tagged.Count} material(s) and {copies} live copies tagged {tag} in {( Stopwatch.GetTimestamp() - started ) * 1000.0 / Stopwatch.Frequency:0.0} ms" );
    }

    // Every loaded material, so a copy on a pooled, inactive scope is reached too. Tagged prefabs already hold the tag.
    private static int RetagCopies( string tag )
    {
      var count = 0;
      foreach( var material in Resources.FindObjectsOfTypeAll<Material>() )
      {
        if( material == null )
          continue;
        var current = material.GetTag( RenderType, false );
        if( current == tag || !CotiRenderTypeTag.IsGlassTag( current ) || WorldGlassIds.Contains( material.GetInstanceID() ) )
          continue;
        material.SetOverrideTag( RenderType, tag );
        count++;
      }
      return count;
    }
  }
}
