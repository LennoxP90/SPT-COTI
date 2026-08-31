using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Reads the tube's phosphor from whatever is actually drawing it. Borkel's Realistic NVGs 3.0
  /// renders from a component of its own and writes NightVision.Color no more, so that field is
  /// the fallback rather than the source.
  ///
  /// Bound by SHAPE, never by version, which is why there is no BepInDependency: Borkel 2.x and
  /// vanilla have no such component and do still use NightVision.Color, so an absent renderer is
  /// the signal to fall back rather than an error. A version pin would refuse to load for them.
  /// </summary>
  internal static class CotiTubeBridge
  {
    /// <summary>
    /// Matched on the field name rather than the declaring type, so a namespace move survives.
    /// </summary>
    private const string PhosphorFieldMarker = "phosphor";

    private const string LitPropertyName = "NightVisionEnabled";

    private static GameObject _resolvedFor;
    private static Component _renderer;
    private static FieldInfo _phosphorField;
    private static PropertyInfo _litProperty;
    private static bool _loggedBinding;

    /// <summary>
    /// Whether a third-party renderer owns the tube image. Either value below can be missing while
    /// this is true, so ask for them rather than inferring them from it.
    /// </summary>
    internal static bool Present { get; private set; }

    /// <summary>
    /// The phosphor colour the tube is actually being drawn with, or false when nothing here can
    /// answer - in which case the caller falls back to NightVision.Color.
    /// </summary>
    internal static bool TryPhosphor( BSG.CameraEffects.NightVision tube, out Color phosphor )
    {
      phosphor = default( Color );

      Resolve( tube );
      if( _renderer == null || _phosphorField == null )
        return false;

      try
      {
        phosphor = (Color)_phosphorField.GetValue( _renderer );
        return true;
      }
      catch( Exception ex )
      {
        Forget( $"reading {_phosphorField.Name} failed: {ex.Message}" );
        return false;
      }
    }

    /// <summary>
    /// Whether the renderer considers the tube lit this frame. Tracks NightVision.On in practice;
    /// kept as an independent reading so the trace can show the two diverging if they ever do.
    /// </summary>
    internal static bool TryLit( BSG.CameraEffects.NightVision tube, out bool lit )
    {
      lit = false;

      Resolve( tube );
      if( _renderer == null || _litProperty == null )
        return false;

      try
      {
        // Both halves - the renderer only draws when the property and the Behaviour agree.
        var behaviour = _renderer as Behaviour;
        lit = (bool)_litProperty.GetValue( _renderer ) && ( behaviour == null || behaviour.enabled );
        return true;
      }
      catch( Exception ex )
      {
        Forget( $"reading {_litProperty.Name} failed: {ex.Message}" );
        return false;
      }
    }

    /// <summary>
    /// Finds the renderer on the tube's GameObject, once each - it only changes between raids.
    /// </summary>
    private static void Resolve( BSG.CameraEffects.NightVision tube )
    {
      var host = tube == null ? null : tube.gameObject;

      if( ReferenceEquals( host, _resolvedFor ) )
        return;

      _resolvedFor = host;
      _renderer = null;
      _phosphorField = null;
      _litProperty = null;
      _loggedBinding = false;
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
        // A missing script leaves a null slot in the array rather than dropping it.
        if( component == null )
          continue;

        var type = component.GetType();

        FieldInfo phosphor = null;
        foreach( var field in type.GetFields( AccessTools.all ) )
        {
          if( field.FieldType != typeof( Color ) )
            continue;
          if( field.Name.IndexOf( PhosphorFieldMarker, StringComparison.OrdinalIgnoreCase ) < 0 )
            continue;

          phosphor = field;
          break;
        }

        if( phosphor == null )
          continue;

        _renderer = component;
        _phosphorField = phosphor;
        _litProperty = type.GetProperty( LitPropertyName, AccessTools.all );
        Present = true;

        if( !_loggedBinding )
        {
          _loggedBinding = true;

          // Names both members, so a rename shows up as a missing half, not a wrong colour.
          Plugin.Log.LogInfo(
              $"[COTI] Tube owned by {type.FullName} - phosphor from {phosphor.Name}, " +
              $"lit state {( _litProperty == null ? "UNAVAILABLE" : _litProperty.Name )}" );
        }

        return;
      }
    }

    /// <summary>
    /// Drops the binding after a failure, so the fallback takes over instead of throwing per frame.
    /// </summary>
    private static void Forget( string reason )
    {
      Plugin.Log.LogWarning( $"[COTI] Tube bridge disabled - {reason}" );

      _renderer = null;
      _phosphorField = null;
      _litProperty = null;
      Present = false;
    }
  }
}
