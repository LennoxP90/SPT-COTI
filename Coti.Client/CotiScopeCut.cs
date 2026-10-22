using System.Collections.Generic;
using Coti.Shared;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Where the goggle COTI's 1x image must not draw this frame, as a screen-sized mask: white over the aimed magnified
  /// scope's own meshes (housing, lens, reticle and anything mounted on the scope, from the model in hand, tilt
  /// included), and over the thermal riser's red dot window while the riser draws there. The 1x overlay multiplies its
  /// coverage by one minus this (<c>_CutTex</c>), so quad tubes beside a scope keep their heat and a circle half behind
  /// the scope keeps its uncovered part.
  ///
  /// Recorded into the overlay's own command buffer at the main camera's pre-cull, the way the red dot mask is, so it
  /// lands on the meshes as drawn. No depth test: a hand in front of the scope is cut as well.
  /// </summary>
  internal static class CotiScopeCut
  {
    private const int AddPass = 1;

    private static readonly int MainTexId = Shader.PropertyToID( "_MainTex" );
    private static readonly int CutTexId = Shader.PropertyToID( "_CutTex" );

    private struct Part
    {
      internal Renderer Renderer;
      internal int Submeshes;
    }

    private static readonly List<Part> ScopeParts = new List<Part>();
    private static readonly List<Renderer> Found = new List<Renderer>();
    private static readonly List<LODGroup> Groups = new List<LODGroup>();
    private static Object _partsOf;

    private static RenderTexture _mask;
    private static Material _maskMaterial;

    internal static RenderTexture Mask => _mask;

    /// <summary>
    /// Whether anything is cut this frame: an aimed sight renders a picture-in-picture image (an OpticSight with its
    /// optic camera, so a scope at any zoom; red dots have none), or the riser draws in its red dot window. A thermal
    /// sight is not here: the 1x overlay stands down entirely for one.
    /// </summary>
    internal static bool Wanted => CotiOpticCamera.CutSight != null || CotiCollimatorCompositor.DrawsInWindow;

    /// <summary>
    /// Frees the mask while nothing is cut, so a texture dump never reads the last cut frame and the screen-sized target
    /// is not held for nothing. Ensure makes it again when a cut is next wanted.
    /// </summary>
    internal static void Drop()
    {
      if( _mask == null )
        return;

      _mask.Release();
      Object.Destroy( _mask );
      _mask = null;
    }

    /// <summary>The mask target at the camera's size, and the white material that draws into it. False without the bundle.</summary>
    internal static bool Ensure( Camera main )
    {
      if( _maskMaterial == null )
      {
        if( CotiShaderBundle.OverlayMaterial == null )
          return false;

        // The copy pass with a white source paints each mesh white, as the red dot mask does.
        _maskMaterial = new Material( CotiShaderBundle.OverlayMaterial ) { name = "CotiScopeCutMask" };
        _maskMaterial.SetTexture( MainTexId, Texture2D.whiteTexture );
        _maskMaterial.SetTexture( CutTexId, Texture2D.blackTexture );
        CotiShaderBundle.KeepLoaded( _maskMaterial );
      }

      if( _mask != null && _mask.width == main.pixelWidth && _mask.height == main.pixelHeight )
        return true;

      if( _mask != null )
        _mask.Release();

      // Four samples a pixel soften the mesh edges; the mask is resolved before the overlay reads it.
      _mask = new RenderTexture( main.pixelWidth, main.pixelHeight, 0, RenderTextureFormat.R8 )
      {
        name = "CotiScopeCut",
        antiAliasing = 4,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };
      _mask.Create();
      return true;
    }

    /// <summary>Records the mask into <paramref name="buffer"/>, leaving the camera's view and projection set on it.</summary>
    internal static void Record( CommandBuffer buffer, Camera main )
    {
      buffer.SetRenderTarget( _mask );
      buffer.ClearRenderTarget( false, true, Color.clear );
      buffer.SetViewProjectionMatrices( main.worldToCameraMatrix, main.projectionMatrix );

      var sight = CotiOpticCamera.CutSight;
      if( sight != null )
      {
        CollectScope( sight );
        foreach( var part in ScopeParts )
        {
          var renderer = part.Renderer;
          if( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
            continue;

          // An emptied lens (PiP-Disabler) still covers its glass, from the mesh it had at the hip.
          var hidden = CotiLensMeshes.Hidden( renderer );
          if( hidden != null )
          {
            buffer.DrawMesh( hidden, renderer.localToWorldMatrix, _maskMaterial, 0, AddPass );
            continue;
          }

          for( var sub = 0; sub < part.Submeshes; sub++ )
            buffer.DrawRenderer( renderer, _maskMaterial, sub, AddPass );
        }
      }

      if( CotiCollimatorCompositor.DrawsInWindow )
      {
        foreach( var lens in CotiCollimatorCompositor.WindowLenses( main.transform.position ) )
          CotiCollimatorCompositor.DrawWindow( buffer, lens, _maskMaterial, AddPass );
      }
    }

    /// <summary>
    /// Every renderer of the sight's item, kept per sight: the item's root is its ScopePrefabCache, and a mode switch only
    /// toggles objects under it, which the per-frame enabled check drops. Mounts and rings are the item's parents, so
    /// they are not cut. A renderer only in a coarser LOD level is left out (CotiLodCut): LOD groups cull rather than
    /// disable, so the HHS-1's low-poly G33 would otherwise draw a faceted outline around the round tube.
    /// </summary>
    private static void CollectScope( OpticSight sight )
    {
      if( ReferenceEquals( _partsOf, sight ) )
        return;

      _partsOf = sight;
      ScopeParts.Clear();

      var cache = sight.GetComponentInParent<ScopePrefabCache>();
      var root = cache != null ? cache.transform : sight.transform;
      root.GetComponentsInChildren( true, Groups );
      var coarse = CotiLodCut.CoarseOnly( LodEntries( Groups ) );
      Groups.Clear();

      root.GetComponentsInChildren( true, Found );
      foreach( var renderer in Found )
      {
        if( renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer
            || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || coarse.Contains( renderer ) )
          continue;

        ScopeParts.Add( new Part { Renderer = renderer, Submeshes = Submeshes( renderer ) } );
      }

      Found.Clear();

      if( Plugin.Config?.VerboseLogging ?? false )
        Plugin.Log.LogInfo( $"[COTI] Scope cut: {ScopeParts.Count} renderer(s) under {root.name}, {coarse.Count} coarser LOD renderer(s) left out" );
    }

    private static IEnumerable<(int, Renderer)> LodEntries( List<LODGroup> groups )
    {
      foreach( var group in groups )
      {
        var lods = group.GetLODs();
        for( var level = 0; level < lods.Length; level++ )
        {
          foreach( var renderer in lods[level].renderers )
            yield return ( level, renderer );
        }
      }
    }

    private static int Submeshes( Renderer renderer )
    {
      Mesh mesh = null;
      var skinned = renderer as SkinnedMeshRenderer;
      if( skinned != null )
        mesh = skinned.sharedMesh;
      else
      {
        var filter = renderer.GetComponent<MeshFilter>();
        if( filter != null )
          mesh = filter.sharedMesh;
      }

      return mesh != null ? Mathf.Max( 1, mesh.subMeshCount ) : 1;
    }
  }
}
