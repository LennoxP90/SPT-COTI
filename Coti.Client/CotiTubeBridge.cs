using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Reads the tube's phosphor from whatever is actually drawing it. Borkel's Realistic NVGs 3.x renders from a component
  /// of its own and does not write NightVision.Color, so that field is the fallback rather than the source.
  ///
  /// Bound by shape rather than version, so there is no BepInDependency: vanilla and Borkel 2.x have no such component,
  /// and an absent renderer is the signal to fall back rather than an error.
  /// </summary>
  internal static class CotiTubeBridge
  {
    /// <summary>Matched on the field name rather than the declaring type, so a namespace move survives.</summary>
    private const string PhosphorFieldMarker = "phosphor";

    private static GameObject _resolvedFor;
    private static Component _renderer;

    /// <summary>
    /// The renderer's phosphor field as a compiled getter: read every frame, so no reflection and no boxed Color. Kept
    /// across raids for the same renderer type, so it is generated once per session.
    /// </summary>
    private static AccessTools.FieldRef<object, Color> _phosphor;
    private static Type _phosphorType;

    /// <summary>Whether a third-party renderer owns the tube image.</summary>
    internal static bool Present { get; private set; }

    /// <summary>
    /// The phosphor colour the tube is being drawn with, or false when nothing here can answer, in which case the caller
    /// falls back to NightVision.Color.
    /// </summary>
    internal static bool TryPhosphor( BSG.CameraEffects.NightVision tube, out Color phosphor )
    {
      phosphor = default( Color );

      Resolve( tube );
      if( _renderer == null )
        return false;

      try
      {
        phosphor = _phosphor( _renderer );
        return true;
      }
      catch( Exception ex )
      {
        Forget( $"reading the phosphor failed: {ex.Message}" );
        return false;
      }
    }

    /// <summary>Finds the renderer on the tube's GameObject, once per GameObject: it only changes between raids.</summary>
    private static void Resolve( BSG.CameraEffects.NightVision tube )
    {
      var host = tube == null ? null : tube.gameObject;
      if( ReferenceEquals( host, _resolvedFor ) )
        return;

      _resolvedFor = host;
      _renderer = null;
      Present = false;

      if( host == null )
        return;

      try
      {
        Bind( host );
      }
      catch( Exception ex )
      {
        Forget( $"binding failed: {ex.Message}" );
      }
    }

    private static void Bind( GameObject host )
    {
      foreach( var component in host.GetComponents<Component>() )
      {
        // A missing script leaves a null slot in the array.
        if( component == null )
          continue;

        var type = component.GetType();
        var field = PhosphorField( type );
        if( field == null )
          continue;

        if( type != _phosphorType )
        {
          _phosphor = AccessTools.FieldRefAccess<object, Color>( field );
          _phosphorType = type;
          Plugin.Log.LogInfo( $"[COTI] Tube owned by {type.FullName} - phosphor from {field.Name}" );
        }

        _renderer = component;
        Present = true;
        return;
      }
    }

    /// <summary>The first instance Color field whose name contains "phosphor".</summary>
    private static FieldInfo PhosphorField( Type type )
    {
      foreach( var field in type.GetFields( AccessTools.all ) )
      {
        if( !field.IsStatic
            && field.FieldType == typeof( Color )
            && field.Name.IndexOf( PhosphorFieldMarker, StringComparison.OrdinalIgnoreCase ) >= 0 )
          return field;
      }
      return null;
    }

    /// <summary>Drops the binding after a failure, so the fallback takes over instead of throwing every frame.</summary>
    private static void Forget( string reason )
    {
      Plugin.Log.LogWarning( $"[COTI] Tube bridge disabled - {reason}" );

      _renderer = null;
      _phosphor = null;
      _phosphorType = null;
      Present = false;
    }
  }
}
