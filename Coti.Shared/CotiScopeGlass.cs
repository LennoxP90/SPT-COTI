using System;
using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// Which sights are magnified scopes, and which of their materials are glass the thermal draws. A magnified scope's
  /// rear glass sits on an object named backLens (backLens (1), backLens_000) and its front glass in a material named
  /// glass; a hybrid's built-in red dot names its parts collimator. Only materials the scope's own bundle holds qualify:
  /// a dependency's material can be shared with a holo (the EOTech HHS-1 draws the EXPS3's glass).
  /// </summary>
  public static class CotiScopeGlass
  {
    private const string RearObject = "backLens";
    private const string Glass = "glass";
    private const string Collimator = "collimator";

    /// <summary>The largest zoom across every sight mode; 1 when there is none.</summary>
    public static float MaxZoom( float[][] zooms )
    {
      var max = 1f;
      if( zooms == null )
        return max;
      foreach( var mode in zooms )
        if( mode != null )
          foreach( var zoom in mode )
            if( zoom > max )
              max = zoom;
      return max;
    }

    /// <summary>
    /// A sight filed as a scope (some, like the Nightforce NXS 2.5-10x, declare zooms of 1 and magnify through the scope
    /// camera) or one zooming above the gate, never a special (thermal, night-vision, camera) scope.
    /// </summary>
    public static bool IsMagnified( float maxZoom, bool isScope, bool isSpecialScope, float minimum )
    {
      return !isSpecialScope && ( isScope || maxZoom > minimum );
    }

    /// <summary>Whether a material of a magnified scope draws as glass: the scope's own rear or front glass.</summary>
    public static bool IsScopeGlass( string materialName, string objectName, bool ownAsset )
    {
      if( !ownAsset )
        return false;
      var material = materialName ?? "";
      var obj = objectName ?? "";
      if( Has( material, Collimator ) || Has( obj, Collimator ) )
        return false;
      return obj.StartsWith( RearObject, StringComparison.OrdinalIgnoreCase ) || Has( material, Glass );
    }

    /// <summary>
    /// How scope glass draws: as world glass does, except that its own toggle can keep it Plain while world glass reflects.
    /// </summary>
    public static CotiGlassMode Mode( CotiGlassMode world, bool scopeReflections )
    {
      return world == CotiGlassMode.Reflections && scopeReflections ? CotiGlassMode.Reflections : CotiGlassMode.Plain;
    }

    /// <summary>
    /// Every bundle the given one depends on, directly or through another dependency, without the bundle itself: where a
    /// material shared with another item comes from. A dependency loop ends.
    /// </summary>
    public static HashSet<string> DependencyClosure( string key, Func<string, IEnumerable<string>> dependencies )
    {
      var closure = new HashSet<string>( StringComparer.Ordinal );
      var open = new Stack<string>();
      open.Push( key );
      while( open.Count > 0 )
      {
        var next = dependencies( open.Pop() );
        if( next == null )
          continue;
        foreach( var dependency in next )
          if( dependency != key && closure.Add( dependency ) )
            open.Push( dependency );
      }
      return closure;
    }

    private static bool Has( string text, string part )
    {
      return text.IndexOf( part, StringComparison.OrdinalIgnoreCase ) >= 0;
    }
  }
}
