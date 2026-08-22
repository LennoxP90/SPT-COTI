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
    private const string BundleFileName = "coti_shaders";
    private const string OverlayShaderName = "Coti/Overlay";

    private static AssetBundle _bundle;
    private static Shader _overlay;
    private static Material _material;
    private static bool _attempted;

    private static string _loadedPath;

    /// <summary>
    /// The overlay shader, or null when the bundle is absent or does not contain it. Null is a
    /// hard stop for the compositor rather than a reason to fall back: the fallbacks are exactly
    /// the broken behaviours this bundle exists to replace.
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
    /// Loads the bundle from beside this assembly. Logs precisely which step failed - a missing
    /// bundle, a bundle that will not open, or a bundle without the expected shader are three
    /// different problems with three different fixes, and "the effect does nothing" is not a
    /// usable diagnosis for any of them.
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
        for( var i = 0; i < materials.Length; i++ )
        {
          if( materials[i] == null || materials[i].shader == null )
            continue;

          _material = materials[i];
          _overlay = materials[i].shader;

          Plugin.Log.LogInfo(
              $"[COTI] Loaded material '{_material.name}' with shader '{_overlay.name}' " +
              $"(isSupported={_overlay.isSupported}, passes={_material.passCount})" );
          break;
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
