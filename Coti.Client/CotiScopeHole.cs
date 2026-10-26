using System.Collections.Generic;
using Comfort.Common;
using Coti.Shared;
using EFT;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Under PiP-Disabler, a scope on a COTI device shows the world through its eyepiece with nothing of the rifle or the
  /// arms in it, and everything around the eyepiece as it is, cut by the GPU's depth test per pixel and per frame. While
  /// such a sight is aimed (<see cref="CotiOpticCamera.ScreenSight"/>), the player's own rifle and arms draw before the
  /// world, on copies of their materials in queue <see cref="BodyQueue"/>, between two copies of the eyepiece lens drawn
  /// into depth only (Coti/ScopeHole): the first at the lens's own depth, so their pixels behind the eyepiece fail inside
  /// its circle, the second on the far plane, so the world then draws there. PiP-Disabler's own mesh cut is skipped for
  /// these sights (PipSkipMeshCutPatch). The 1x thermal camera draws the same discs (Coti/HeatOnly's CotiScopeHole
  /// sub-shader), so the riser's heat in the lens is the world's and the hands keep theirs around it. The scope's own glass
  /// renderers draw after the world, so no depth trick reaches them; they are switched off instead, as PiP-Disabler's cut
  /// removed them.
  /// </summary>
  internal static class CotiScopeHole
  {
    private const int BlockQueue = 1001;
    private const int BodyQueue = 1002;
    private const int ResetQueue = 1003;
    private const int LastDeferredQueue = 2500;
    private static readonly int FarId = Shader.PropertyToID( "_Far" );

    private static readonly List<Renderer> Bodies = new List<Renderer>();
    private static readonly List<Material[]> Originals = new List<Material[]>();
    private static readonly List<Renderer> Found = new List<Renderer>();
    private static readonly List<GameObject> Discs = new List<GameObject>();
    private static readonly List<Renderer> Glass = new List<Renderer>();
    private static readonly List<Material> Slots = new List<Material>();
    // Every material made here, destroyed on restore even when the raid ended first and took the renderers with it.
    private static readonly List<Material> Made = new List<Material>();
    private static readonly List<Renderer> Leds = new List<Renderer>();
    private static OpticSight _sight;
    private static Renderer _lens;
    private static Mesh _lensMesh;

    /// <summary>A sight on a COTI device, while the bundle carries the shader that cuts its view.</summary>
    internal static bool Takes( Transform sight )
    {
      var shader = CotiShaderBundle.ScopeHole;
      return shader != null && shader.isSupported && ThermalRiserState.IsOnRiser( sight );
    }

    internal static void Sync( OpticSight sight )
    {
      if( sight != null && !Takes( sight.transform ) )
        sight = null;
      if( sight == _sight )
      {
        CullLeds();
        return;
      }

      Restore();
      _sight = sight;
      var lens = sight != null ? sight.LensRenderer : null;
      var mesh = lens != null ? CotiLensMeshes.Hidden( lens ) ?? lens.GetComponent<MeshFilter>()?.sharedMesh : null;
      if( mesh == null )
        return;

      Collect( sight.transform.root, firstPersonOnly: false );
      var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
      if( player != null )
        Collect( player.gameObject.transform, firstPersonOnly: true );

      Discs.Add( Disc( "CotiScopeHoleBlock", lens.transform, mesh, BlockQueue, 0f ) );
      Discs.Add( Disc( "CotiScopeHoleReset", lens.transform, mesh, ResetQueue, 1f ) );
      HideGlass( sight );
      FindLeds( sight.transform.root );
      _lens = lens;
      _lensMesh = mesh;
      CullLeds();

      if( Plugin.Config?.VerboseLogging ?? false )
        Plugin.Log.LogInfo( $"[COTI] Scope hole: {sight.name}, {Bodies.Count} renderer(s) drawn before the world, {Glass.Count} glass renderer(s) off" +
                            $"{( Glass.Count > 0 ? " (" + string.Join( ", ", Glass.ConvertAll( g => g.name ) ) + ")" : "" )}" );
    }

    /// <summary>The renderers under root drawn in the deferred pass, moved onto copies of their materials in BodyQueue.</summary>
    private static void Collect( Transform root, bool firstPersonOnly )
    {
      root.GetComponentsInChildren( false, Found );
      foreach( var renderer in Found )
      {
        // Once only: a weapon parented under the player would otherwise be moved twice and restored to the first copies.
        if( renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer
            || Bodies.Contains( renderer ) )
          continue;
        // First person draws only the arms of the player's own body; the rest casts shadows.
        if( firstPersonOnly && renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly )
          continue;

        var originals = renderer.sharedMaterials;
        var copies = new Material[originals.Length];
        var moved = false;
        for( var i = 0; i < originals.Length; i++ )
        {
          copies[i] = originals[i];
          if( originals[i] == null || originals[i].renderQueue > LastDeferredQueue )
            continue;
          copies[i] = new Material( originals[i] ) { renderQueue = BodyQueue };
          Made.Add( copies[i] );
          moved = true;
        }

        if( !moved )
          continue;
        renderer.sharedMaterials = copies;
        Bodies.Add( renderer );
        Originals.Add( originals );
      }
      Found.Clear();
    }

    /// <summary>
    /// The aimed scope's renderers that are glass and nothing else, switched off: a mod scope's dark lens (Nightforce
    /// ATACR F1, Leupold Mark 5HD), a transparent material COTI's glass tagger never named, blacked the eyepiece out. Glass is
    /// what CotiScreenSightGlass tags or anything in the transparent queues. The lens PiP-Disabler empties is left to it.
    /// </summary>
    private static void HideGlass( OpticSight sight )
    {
      var item = sight.GetComponentInParent<ScopePrefabCache>();
      if( item == null )
        return;
      item.GetComponentsInChildren( false, Found );
      foreach( var renderer in Found )
        if( renderer != null && renderer != sight.LensRenderer && !renderer.forceRenderingOff && OnlyGlass( renderer ) )
        {
          renderer.forceRenderingOff = true;
          Glass.Add( renderer );
        }
      Found.Clear();
    }

    private static void FindLeds( Transform root )
    {
      foreach( var node in root.GetComponentsInChildren<Transform>( true ) )
        if( node.name == CotiIds.RiserLedName || node.name == CotiIds.ScopeRingLedName )
          foreach( var renderer in node.GetComponentsInChildren<Renderer>( true ) )
            if( !renderer.forceRenderingOff )
              Leds.Add( renderer );
    }

    /// <summary>
    /// COTI's own power LED, hidden while it lands inside the eyepiece, where the scope's body would hide it: the lit LED
    /// draws in the forward pass, after the world and out of reach of the depth trick. The eyepiece is the lens mesh's
    /// bounds projected to the screen, its inscribed circle.
    /// </summary>
    private static void CullLeds()
    {
      var camera = CotiFrame.Main;
      if( Leds.Count == 0 || camera == null || _lens == null || _lensMesh == null )
        return;

      var bounds = _lensMesh.bounds;
      var matrix = _lens.localToWorldMatrix;
      var min = new Vector2( float.MaxValue, float.MaxValue );
      var max = new Vector2( float.MinValue, float.MinValue );
      for( var i = 0; i < 8; i++ )
      {
        var corner = bounds.center + Vector3.Scale( bounds.extents,
            new Vector3( ( i & 1 ) == 0 ? -1f : 1f, ( i & 2 ) == 0 ? -1f : 1f, ( i & 4 ) == 0 ? -1f : 1f ) );
        Vector2 point = camera.WorldToScreenPoint( matrix.MultiplyPoint3x4( corner ) );
        min = Vector2.Min( min, point );
        max = Vector2.Max( max, point );
      }

      var centre = ( min + max ) * 0.5f;
      var radius = Mathf.Min( max.x - min.x, max.y - min.y ) * 0.5f;
      foreach( var led in Leds )
      {
        if( led == null )
          continue;
        var point = camera.WorldToScreenPoint( led.bounds.center );
        led.forceRenderingOff = point.z > 0f && Vector2.Distance( point, centre ) < radius;
      }
    }

    private static bool OnlyGlass( Renderer renderer )
    {
      renderer.GetSharedMaterials( Slots );
      var glass = Slots.Count > 0;
      foreach( var material in Slots )
      {
        var tag = material != null ? material.GetTag( "RenderType", false ) : null;
        glass &= material != null && ( material.renderQueue > LastDeferredQueue || tag == CotiRenderTypeTag.SeenThrough
                                       || CotiRenderTypeTag.IsGlassTag( tag ) );
      }
      Slots.Clear();
      return glass;
    }

    private static GameObject Disc( string name, Transform lens, Mesh mesh, int queue, float far )
    {
      var disc = new GameObject( name ) { layer = lens.gameObject.layer };
      disc.transform.SetParent( lens.parent, false );
      disc.transform.localPosition = lens.localPosition;
      disc.transform.localRotation = lens.localRotation;
      disc.transform.localScale = lens.localScale;
      disc.AddComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = disc.AddComponent<MeshRenderer>();
      var material = new Material( CotiShaderBundle.ScopeHole ) { renderQueue = queue };
      material.SetFloat( FarId, far );
      Made.Add( material );
      renderer.sharedMaterial = material;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      return disc;
    }

    private static void Restore()
    {
      for( var i = 0; i < Bodies.Count; i++ )
        if( Bodies[i] != null )
          Bodies[i].sharedMaterials = Originals[i];
      Bodies.Clear();
      Originals.Clear();

      foreach( var disc in Discs )
        if( disc != null )
          Object.Destroy( disc );
      Discs.Clear();

      foreach( var renderer in Glass )
        if( renderer != null )
          renderer.forceRenderingOff = false;
      Glass.Clear();

      foreach( var led in Leds )
        if( led != null )
          led.forceRenderingOff = false;
      Leds.Clear();
      _lens = null;
      _lensMesh = null;

      foreach( var material in Made )
        if( material != null )
          Object.Destroy( material );
      Made.Clear();
    }
  }
}
