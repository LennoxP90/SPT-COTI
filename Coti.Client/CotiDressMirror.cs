using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Prevent the COTI from being shown in the viewport when using it
  /// </summary>
  public class CotiDressMirror : MonoBehaviour
  {
    private Renderer[] _coti;
    private Renderer _host;
    private ShadowCastingMode _lastLogged = (ShadowCastingMode)( -1 );

    public static void Register( GameObject itemView, Transform bone )
    {
      if( itemView == null || bone == null )
        return;

      var coti = itemView.GetComponentsInChildren<Renderer>( includeInactive: true );
      if( coti.Length == 0 )
        return;

      var host = FindNvgHostRenderer( itemView, bone );
      if( host == null )
        return;

      // Explicit null check: Unity's overloaded null defeats ?? and would silently add nothing.
      var mirror = itemView.GetComponent<CotiDressMirror>();
      if( mirror == null )
        mirror = itemView.AddComponent<CotiDressMirror>();

      mirror._coti = coti;
      mirror._host = host;

      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        Plugin.Log.LogInfo(
            $"[COTI DRESS] mirroring {coti.Length} renderer(s) against NVG host '{host.name}' " +
            $"under root '{bone.root.name}' (host mode {host.shadowCastingMode})" );
      }
    }

    /// <summary>
    /// A renderer belonging to the NVG device this COTI is clipped to.
    /// </summary>
    private static Renderer FindNvgHostRenderer( GameObject itemView, Transform bone )
    {
      for( var level = bone.parent; level != null; level = level.parent )
      {
        // The pool and the scene root are not hosts - anything found past here belongs to some
        // unrelated object that happens to share the hierarchy.
        if( level.name == "POOLS" )
          return null;

        foreach( var candidate in level.GetComponentsInChildren<Renderer>( includeInactive: true ) )
        {
          if( candidate == null )
            continue;
          if( candidate.transform.IsChildOf( itemView.transform ) )
            continue;

          return candidate;
        }
      }

      return null;
    }

    /// <summary>After the game has set the host's mode for this frame, not before.</summary>
    private void LateUpdate()
    {
      if( _host == null || _coti == null )
        return;

      var mode = _host.shadowCastingMode;

      foreach( var renderer in _coti )
      {
        if( renderer == null || renderer.shadowCastingMode == mode )
          continue;
        renderer.shadowCastingMode = mode;
      }

      if( mode != _lastLogged && Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        _lastLogged = mode;
        Plugin.Log.LogInfo( $"[COTI DRESS] NVG host '{_host.name}' -> {mode}, mirrored onto {_coti.Length} renderer(s)" );
      }
    }
  }
}
