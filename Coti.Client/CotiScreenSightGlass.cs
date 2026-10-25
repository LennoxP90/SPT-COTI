using System.Collections.Generic;
using Coti.Shared;
using EFT.CameraControl;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The aimed magnified scope's own glass, kept out of COTI's thermal images while it is aimed. Under PiP-Disabler
  /// (<see cref="CotiOpticCamera.ScreenSight"/>) the riser's heat is drawn through that lens from the eye by the 1x
  /// camera, and a scope whose body mesh carries its glass (the Razor HD Gen II and its ISB copies) stood in front of it
  /// as solid glass. Without it (<see cref="CotiOpticCamera.AimedSight"/>) the magnified thermal camera sits at the game's
  /// optic camera, inside the scope, and a front glass ahead of it (the ATACR 7-35's) hid the whole view. Only this scope's
  /// own material copies change; every other scope, a bot's included, keeps its glass. The tags go back when the sight is
  /// lowered or another is aimed.
  /// </summary>
  internal static class CotiScreenSightGlass
  {
    private const string RenderType = "RenderType";

    private static readonly List<KeyValuePair<Material, string>> Hidden = new List<KeyValuePair<Material, string>>();
    private static readonly List<Renderer> Renderers = new List<Renderer>();
    private static readonly List<Material> Materials = new List<Material>();
    private static OpticSight _sight;

    internal static void Sync( OpticSight sight )
    {
      if( sight == _sight )
        return;

      Restore();
      _sight = sight;
      // The sight's item, as CotiLensMeshes finds it: the glass is on the scope's body, not under the OpticSight.
      var item = sight != null ? sight.GetComponentInParent<ScopePrefabCache>() : null;
      if( item == null )
        return;

      item.GetComponentsInChildren( true, Renderers );
      foreach( var renderer in Renderers )
      {
        renderer.GetSharedMaterials( Materials );
        foreach( var material in Materials )
        {
          var tag = material != null ? material.GetTag( RenderType, false ) : null;
          if( !CotiRenderTypeTag.IsGlassTag( tag ) )
            continue;
          Hidden.Add( new KeyValuePair<Material, string>( material, tag ) );
          material.SetOverrideTag( RenderType, CotiRenderTypeTag.SeenThrough );
        }
      }
      Renderers.Clear();
      Materials.Clear();

      if( Hidden.Count > 0 && ( Plugin.Config?.VerboseLogging ?? false ) )
        Plugin.Log.LogInfo( $"[COTI] Scope glass seen through: {Hidden.Count} material(s) on {item.name} left out of the thermal image" );
    }

    private static void Restore()
    {
      foreach( var entry in Hidden )
        if( entry.Key != null )
          entry.Key.SetOverrideTag( RenderType, entry.Value );
      Hidden.Clear();
    }
  }
}
