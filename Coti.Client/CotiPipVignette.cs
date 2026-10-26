using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// PiP-Disabler's lens vignette, carried into the riser's heat window. The game's own scopes darken their picture's edge
  /// in the lens shader, and COTI's magnified path draws its heat through that shader, so the heat darkens with the
  /// picture. Under PiP-Disabler the lens is emptied and PiP-Disabler draws its own vignette over the zoomed screen
  /// before COTI composites, so without this the heat showed bright inside its dark ring. Its live per-scope radius,
  /// softness and opacity and its lens box are read each frame, and the same falloff is multiplied into the window mask
  /// (Coti/ScopeVignette), centred on the eyepiece.
  /// </summary>
  internal static class CotiPipVignette
  {
    private delegate bool LensBounds( Camera camera, out Vector2 centre, out Vector2 size );

    private static readonly int InnerId = Shader.PropertyToID( "_Inner" );
    private static readonly int OuterId = Shader.PropertyToID( "_Outer" );
    private static readonly int OpacityId = Shader.PropertyToID( "_Opacity" );
    private static readonly int HalfId = Shader.PropertyToID( "_Half" );
    private static readonly int CentreId = Shader.PropertyToID( "_Centre" );

    private static bool _looked;
    private static ConfigEntry<bool> _enabled;
    private static Func<float> _radius, _softness, _opacity;
    private static LensBounds _bounds;
    private static Material _material;
    private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

    /// <summary>Multiplies the vignette into the window just drawn through this lens, when PiP-Disabler draws one.</summary>
    internal static void Draw( CommandBuffer buffer, Camera main, Renderer lens )
    {
      if( !Plugin.PipDisabler || lens == null || !Ready() || !_enabled.Value )
        return;

      var opacity = _opacity();
      if( opacity <= 0f || !_bounds( main, out _, out var size ) )
        return;

      var mesh = CotiLensMeshes.Hidden( lens ) ?? lens.GetComponent<MeshFilter>()?.sharedMesh;
      if( mesh == null )
        return;

      var inner = Mathf.Clamp01( _radius() );
      Block.Clear();
      Block.SetFloat( InnerId, inner );
      Block.SetFloat( OuterId, Mathf.Lerp( inner, 1f, Mathf.Clamp01( _softness() ) ) );
      Block.SetFloat( OpacityId, opacity );
      Block.SetVector( HalfId, size * 0.5f );
      Block.SetVector( CentreId, lens.localToWorldMatrix.MultiplyPoint3x4( mesh.bounds.center ) );
      buffer.DrawMesh( mesh, lens.localToWorldMatrix, _material, 0, 0, Block );
    }

    private static bool Ready()
    {
      if( !_looked )
      {
        _looked = true;
        try
        {
          var settings = AccessTools.TypeByName( "PiPDisabler.Settings" );
          var perScope = AccessTools.TypeByName( "PiPDisabler.PerScopeMeshSurgerySettings" );
          var reticle = AccessTools.TypeByName( "PiPDisabler.ReticleRenderer" );
          _enabled = AccessTools.Field( settings, "VignetteEnabled" )?.GetValue( null ) as ConfigEntry<bool>;
          _radius = Getter( perScope, "GetVignetteRadius" );
          _softness = Getter( perScope, "GetVignetteSoftness" );
          _opacity = Getter( perScope, "GetVignetteOpacity" );
          var bounds = AccessTools.Method( reticle, "TryGetLensMaskClipBounds" );
          _bounds = bounds != null ? (LensBounds)Delegate.CreateDelegate( typeof( LensBounds ), bounds ) : null;
          var shader = CotiShaderBundle.ScopeVignette;
          _material = shader != null && shader.isSupported ? new Material( shader ) { name = "CotiScopeVignette" } : null;
          CotiShaderBundle.KeepLoaded( _material );
        }
        catch( Exception ex )
        {
          Plugin.Log.LogWarning( $"[COTI] PiP-Disabler's vignette not found where 2.1.0 has it; the riser's heat ignores it: {ex.Message}" );
        }

        if( Plugin.Config?.VerboseLogging ?? false )
          Plugin.Log.LogInfo( $"[COTI] PiP-Disabler vignette in the riser's heat: {( Usable ? "on" : "unavailable" )}" );
      }

      return Usable;
    }

    private static bool Usable => _enabled != null && _radius != null && _softness != null && _opacity != null && _bounds != null && _material != null;

    private static Func<float> Getter( Type type, string name )
    {
      var method = type != null ? AccessTools.Method( type, name ) : null;
      return method != null ? (Func<float>)Delegate.CreateDelegate( typeof( Func<float> ), method ) : null;
    }
  }
}
