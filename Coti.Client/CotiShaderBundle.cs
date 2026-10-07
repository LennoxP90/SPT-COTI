using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Loads the mod's shader bundle and hands out Coti/Overlay.
  /// </summary>
  internal static class CotiShaderBundle
  {
    /// <summary>
    /// Protects an asset from Resources.UnloadUnusedAssets, which the game runs mid-raid (spawning a bot is enough).
    /// On the il2cpp line a plugin field is not an engine reference, so without this the overlay material is destroyed
    /// under the compositor and the circle stops drawing.
    /// </summary>
    internal static void KeepLoaded( UnityEngine.Object asset )
    {
      if( asset != null )
        asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }

    private const string BundleFileName = "coti_shaders";
    private const string OverlayShaderName = "Coti/Overlay";
    private const string HeatOnlyShaderName = "Coti/HeatOnly";
    private const string TerrainGridShaderName = "Coti/TerrainGrid";
    private const string GlowBakeShaderName = "Coti/GlowBake";

    private static AssetBundle _bundle;
    private static Shader _overlay;
    private static Material _material;
    private static Shader _heatOnly;
    private static Shader _terrainGrid;
    private static Shader _glowBake;
    private static bool _attempted;

    private static string _loadedPath;

    /// <summary>
    /// The overlay shader, or null when the bundle is absent or does not contain it. Null stops the
    /// compositor rather than triggering a fallback, because the fallbacks are the broken
    /// behaviours this bundle replaces.
    /// </summary>
    internal static Shader Overlay
    {
      get
      {
        if( !_attempted )
          Load();
        return _overlay;
      }
    }

    /// <summary>The thermal camera's replacement shader, or null with a bundle that predates it.</summary>
    internal static Shader HeatOnly
    {
      get
      {
        if( !_attempted )
          Load();
        return _heatOnly;
      }
    }

    /// <summary>The thermal camera's terrain, drawn from each terrain's heightmap, or null with a bundle that predates it.</summary>
    internal static Shader TerrainGrid
    {
      get
      {
        if( !_attempted )
          Load();
        return _terrainGrid;
      }
    }

    /// <summary>Lays a lamp's glowing parts out in texture space to find where its glow is, or null with a bundle that predates it.</summary>
    internal static Shader GlowBake
    {
      get
      {
        if( !_attempted )
          Load();
        return _glowBake;
      }
    }

    internal static Material OverlayMaterial
    {
      get
      {
        if( !_attempted )
          Load();
        return _material;
      }
    }

    /// <summary>
    /// Loads the bundle from beside this assembly. Logs which step failed: a missing bundle, a
    /// bundle that will not open, and a bundle without the expected shader have different fixes.
    /// </summary>
    private static void Load()
    {
      _attempted = true;

      try
      {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var directory = Path.GetDirectoryName( assemblyPath );

        if( string.IsNullOrEmpty( directory ) )
        {
          Plugin.Log.LogError( "[COTI] Cannot resolve the plugin directory - shader bundle not loaded" );
          return;
        }

        var path = Path.Combine( directory, BundleFileName );

        if( !File.Exists( path ) )
        {
          Plugin.Log.LogError(
              $"[COTI] Shader bundle missing at {path}. The COTI overlay cannot render " +
              $"without it. Reinstall the mod so that {BundleFileName} sits beside this plugin." );
          return;
        }

        _loadedPath = path;

        _bundle = AssetBundle.LoadFromFile( path );

        if( _bundle == null )
        {
          Plugin.Log.LogError(
              $"[COTI] AssetBundle.LoadFromFile returned null for {path}. Most likely the " +
              "bundle was built with a different Unity version than the game's 2019.4.39f1." );
          return;
        }

        var materials = _bundle.LoadAllAssets<Material>();
        var verbose = Plugin.Config != null && Plugin.Config.VerboseLogging;
        // By shader name: the bundle carries one material per shader, in no guaranteed order.
        for( var i = 0; i < materials.Length; i++ )
        {
          var shader = materials[i] == null ? null : materials[i].shader;
          if( shader == null )
            continue;

          if( shader.name == GlowBakeShaderName )
          {
            _glowBake = shader;
            if( verbose )
              Plugin.Log.LogInfo( $"[COTI] Loaded glow bake shader '{shader.name}' (isSupported={shader.isSupported})" );
            continue;
          }

          if( shader.name == TerrainGridShaderName )
          {
            _terrainGrid = shader;
            if( verbose )
              Plugin.Log.LogInfo( $"[COTI] Loaded terrain shader '{shader.name}' (isSupported={shader.isSupported})" );
            continue;
          }

          if( shader.name == HeatOnlyShaderName )
          {
            _heatOnly = shader;
            if( verbose )
              Plugin.Log.LogInfo( $"[COTI] Loaded replacement shader '{shader.name}' (isSupported={shader.isSupported})" );
            continue;
          }

          if( _material != null )
            continue;

          _material = materials[i];
          _overlay = shader;

          if( verbose )
          {
            Plugin.Log.LogInfo(
                $"[COTI] Loaded material '{_material.name}' with shader '{_overlay.name}' " +
                $"(isSupported={_overlay.isSupported}, passes={_material.passCount})" );
          }
        }

        var shaders = _overlay != null ? new Shader[0] : _bundle.LoadAllAssets<Shader>();

        for( var i = 0; i < shaders.Length; i++ )
        {
          if( shaders[i] != null && shaders[i].name == OverlayShaderName )
          {
            _overlay = shaders[i];
            break;
          }
        }

        if( _overlay == null && shaders.Length == 1 && shaders[0] != null )
        {
          _overlay = shaders[0];
          Plugin.Log.LogWarning(
              $"[COTI] No shader named '{OverlayShaderName}' in the bundle; using the only " +
              $"one present: '{_overlay.name}'" );
        }

        if( _overlay == null )
        {
          Plugin.Log.LogError(
              $"[COTI] Bundle loaded but contains no usable shader. Shaders found: " +
              $"{shaders.Length}. Asset names: {string.Join( ", ", _bundle.GetAllAssetNames() )}" );
          return;
        }

        if( !_overlay.isSupported )
        {
          Plugin.Log.LogError(
              $"[COTI] Shader '{OverlayShaderName}' loaded but reports isSupported=false - " +
              "it will not render. Check the bundle's build target is StandaloneWindows64." );
          _overlay = null;
          return;
        }

        KeepLoaded( _material );
        KeepLoaded( _overlay );
        KeepLoaded( _heatOnly );
        KeepLoaded( _glowBake );

        Plugin.Log.LogInfo(
            $"[COTI] Loaded shader bundle from {path}; '{OverlayShaderName}' ready" );
      }
      catch( Exception ex )
      {
        Plugin.Log.LogError( $"[COTI] Shader bundle load failed: {ex}" );
        _overlay = null;
      }
    }
  }
}
