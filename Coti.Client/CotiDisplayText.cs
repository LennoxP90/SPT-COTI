using System;
using System.IO;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The device display's boot and shutdown messages, as a screen-aspect texture the overlay
  /// compositor blends through the circle mask like the thermal. Redrawn only when the message,
  /// the circle or the screen size changes.
  /// </summary>
  internal static class CotiDisplayText
  {
    private const string TextureFolder = "textures";

    private static Texture2D _initializing;
    private static Texture2D _powerOff;
    private static RenderTexture _target;

    private static bool _drawn;
    private static CotiDisplayMessage _drawnMessage;
    private static float _drawnCenterX;
    private static float _drawnCenterY;
    private static float _drawnRadius;

    internal static RenderTexture Output =>
        CotiState.Showing == CotiShowing.Message ? _target : null;

    internal static void Load( string pluginDirectory )
    {
      var folder = Path.Combine( pluginDirectory, TextureFolder );
      _initializing = LoadPng( Path.Combine( folder, "coti_text_initializing.png" ) );
      _powerOff = LoadPng( Path.Combine( folder, "coti_text_power_off.png" ) );
    }

    internal static void Tick()
    {
      var host = CotiState.Host;
      if( CotiState.Showing != CotiShowing.Message || host == null )
        return;

      var width = Mathf.Max( 16, Screen.width );
      var height = Mathf.Max( 16, Screen.height );

      if( EnsureTarget( width, height ) || !IsCurrent( CotiState.Message, host ) )
        Draw( CotiState.Message, host );
    }

    internal static void Teardown()
    {
      if( _target != null )
      {
        _target.Release();
        UnityEngine.Object.Destroy( _target );
        _target = null;
      }

      _drawn = false;
    }

    private static bool IsCurrent( CotiDisplayMessage message, CotiNvgHostConfig host )
    {
      return _drawn
             && _drawnMessage == message
             && _drawnCenterX == host.MaskCenterX
             && _drawnCenterY == host.MaskCenterY
             && _drawnRadius == host.MaskRadius;
    }

    /// <summary>
    /// True when the target was (re)allocated, which always needs a redraw.
    /// </summary>
    private static bool EnsureTarget( int width, int height )
    {
      if( _target != null && _target.width == width && _target.height == height )
        return false;

      Teardown();

      _target = new RenderTexture( width, height, 0, RenderTextureFormat.ARGB32 )
      {
        name = "CotiDisplayText",
        useMipMap = false,
        autoGenerateMips = false,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };
      _target.Create();
      return true;
    }

    private static void Draw( CotiDisplayMessage message, CotiNvgHostConfig host )
    {
      Clear();
      Remember( message, host );

      var image = ImageFor( message );
#if COTI_DEV
      var state = image != null ? image.width + "x" + image.height : ReferenceEquals( image, null ) ? "never loaded" : "destroyed";
      Plugin.Log.LogInfo( $"[COTI] message {message}: image {state}, " +
                          $"target {_target.width}x{_target.height}, circle ({host.MaskCenterX:F3},{host.MaskCenterY:F3}) r={host.MaskRadius:F3}" );
#endif
      if( image == null )
        return; // Blank, or a missing file: the lit, empty display

      float scaleX, scaleY, offsetX, offsetY;
      if( !CotiDisplayLayout.TryBlitTransform(
              host.MaskCenterX, host.MaskCenterY, host.MaskRadius,
              (float)_target.width / _target.height, (float)image.width / image.height,
              out scaleX, out scaleY, out offsetX, out offsetY ) )
        return;

      Graphics.Blit( image, _target, new Vector2( scaleX, scaleY ), new Vector2( offsetX, offsetY ) );
    }

    private static void Clear()
    {
      var previous = RenderTexture.active;
      RenderTexture.active = _target;
      GL.Clear( false, true, Color.black );
      RenderTexture.active = previous;
    }

    private static void Remember( CotiDisplayMessage message, CotiNvgHostConfig host )
    {
      _drawn = true;
      _drawnMessage = message;
      _drawnCenterX = host.MaskCenterX;
      _drawnCenterY = host.MaskCenterY;
      _drawnRadius = host.MaskRadius;
    }

    private static Texture2D ImageFor( CotiDisplayMessage message )
    {
      switch( message )
      {
        case CotiDisplayMessage.Initializing: return _initializing;
        case CotiDisplayMessage.PowerOff: return _powerOff;
        default: return null;
      }
    }

    /// <summary>
    /// The image is drawn several times smaller than it is stored, and plain bilinear sampling
    /// that far down breaks thin strokes apart. A mip chain with trilinear filtering keeps the
    /// letters whole at any screen size.
    /// </summary>
    private static Texture2D WithMipChain( Texture2D decoded, string name )
    {
      // Loaded at the menu and used in raid. The raid's scene load unloads every asset nothing in the
      // engine references, and on the il2cpp line a plugin field is not an engine reference.
      var texture = new Texture2D( decoded.width, decoded.height, TextureFormat.RGB24, true )
      {
        name = name,
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Trilinear,
        hideFlags = HideFlags.DontUnloadUnusedAsset,
      };

      texture.SetPixels32( decoded.GetPixels32() );
      texture.Apply( true, true );
      return texture;
    }

    /// <summary>
    /// Null on any failure, logged once here: the sequence then runs without that message.
    /// </summary>
    private static Texture2D LoadPng( string path )
    {
      try
      {
        if( !File.Exists( path ) )
        {
          Plugin.Log.LogWarning( $"[COTI] {path} is missing - the power sequence will show no text for it" );
          return null;
        }

        var decoded = new Texture2D( 2, 2, TextureFormat.RGB24, false );
        if( !decoded.LoadImage( File.ReadAllBytes( path ) ) )
        {
          UnityEngine.Object.Destroy( decoded );
          Plugin.Log.LogWarning( $"[COTI] {path} is not a readable image - the power sequence will show no text for it" );
          return null;
        }

        var texture = WithMipChain( decoded, Path.GetFileNameWithoutExtension( path ) );
        UnityEngine.Object.Destroy( decoded );
        return texture;
      }
      catch( Exception ex )
      {
        Plugin.Log.LogWarning( $"[COTI] Could not load {path}: {ex.Message}" );
        return null;
      }
    }
  }
}
