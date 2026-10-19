using UnityEngine;

namespace Coti.Client
{
  public static class CotiShaderRebind
  {
    private const string ItemShader = "p0/Reflective/Bumped Specular SMap";

    private static Shader _gameShader;

    public static void Apply( GameObject itemView )
    {
      if( itemView == null )
        return;

      var swapped = 0;

      foreach( var renderer in itemView.GetComponentsInChildren<Renderer>( includeInactive: true ) )
      {
        // Tested against sharedMaterials: reading materials instantiates a copy per renderer, and
        // this runs on every dress pass over pooled views.
        if( !NeedsRebind( renderer ) )
          continue;

        foreach( var material in renderer.materials )
        {
          if( material == null || material.shader == null )
            continue;
          if( material.shader.name != ItemShader )
            continue;

          var gameShader = GameShader( material.shader );
          if( gameShader == null || material.shader == gameShader )
            continue;

          material.shader = gameShader;
          swapped++;
        }
      }

      if( swapped > 0 && Plugin.Config != null && Plugin.Config.VerboseLogging )
      {
        Plugin.Log.LogInfo( $"[COTI SHADER] rebound {swapped} material(s) onto the game's shader" );
      }
    }

    /// <summary>
    /// Rebinds the thermal riser's own mesh and the LED's mode_ children (the LED node itself has no renderer;
    /// <see cref="Apply"/> walks its inactive children too) wherever they sit under <paramref name="view"/>. Only
    /// those nodes are handed to <see cref="Apply"/>, never the view, for the reason given on
    /// GameShader: the view also holds the gun's and the sight's renderers, already on the game's shader.
    /// </summary>
    public static void ApplyToRisers( GameObject view )
    {
      if( view == null )
        return;

      foreach( var node in view.GetComponentsInChildren<Transform>( includeInactive: true ) )
      {
        if( node.name == Coti.Shared.CotiIds.RiserMeshName || node.name == Coti.Shared.CotiIds.RiserLedName )
          Apply( node.gameObject );
      }
    }

    private static bool NeedsRebind( Renderer renderer )
    {
      foreach( var material in renderer.sharedMaterials )
      {
        if( material?.shader != null && material.shader.name == ItemShader )
          return true;
      }

      return false;
    }

    /// <summary>
    /// Every loaded shader of that name, minus ours, leaves the game's. Shader.Find is not used
    /// because it returns whichever registered first, which may be the copy being replaced. Not
    /// cached on failure, since the game's may not be loaded yet.
    ///
    /// This only holds while <paramref name="ours"/> is the bundle's copy, so callers pass the
    /// device and never a whole item view. Sweeping a view reaches the host's renderers, and being
    /// handed the game's shader as "ours" would cache the wrong copy.
    /// </summary>
    private static Shader GameShader( Shader ours )
    {
      if( _gameShader != null )
        return _gameShader;

      foreach( var shader in Resources.FindObjectsOfTypeAll<Shader>() )
      {
        if( shader == null || shader == ours || shader.name != ItemShader )
          continue;

        _gameShader = shader;

        if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        {
          Plugin.Log.LogInfo( $"[COTI SHADER] game shader {shader.GetInstanceID()}, ours {ours.GetInstanceID()}" );
        }

        break;
      }

      return _gameShader;
    }
  }
}
