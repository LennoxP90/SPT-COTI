using System;
using System.IO;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The device display's boot and shutdown messages, as a screen-aspect texture the overlay
  /// compositor blends through the open circles like the thermal: one copy per open circle, placed
  /// by CotiDisplayLayout where the device file's text block says. Redrawn only when the message, the
  /// mode, the open circles, their text placement or the screen size changes.
  /// </summary>
  internal static class CotiDisplayText
  {
    private const string TextureFolder = "textures";

    private static Texture2D _initializing;
    private static Texture2D _powerOff;
    private static Texture2D _outline;
    private static Texture2D _full;
    private static RenderTexture _target;

    private static bool _drawn;
    private static CotiDisplayMessage _drawnMessage;
    private static CotiThermalMode _drawnMode;
    private static readonly CotiCircle[] _drawnCircles = new CotiCircle[CotiState.MaxTubes];
    private static readonly string[] _drawnLabels = new string[CotiState.MaxTubes];
    private static readonly CotiTextPlacement[] _drawnTexts = new CotiTextPlacement[CotiState.MaxTubes];
    private static int _drawnCount;

    internal static RenderTexture Output =>
        CotiState.Showing == CotiShowing.Message ? _target : null;

    internal static void Load( string pluginDirectory )
    {
      var folder = Path.Combine( pluginDirectory, TextureFolder );
      _initializing = LoadPng( Path.Combine( folder, "coti_text_initializing.png" ) );
      _powerOff = LoadPng( Path.Combine( folder, "coti_text_power_off.png" ) );
      _outline = LoadPng( Path.Combine( folder, "coti_text_mode_outline.png" ) );
      _full = LoadPng( Path.Combine( folder, "coti_text_mode_full.png" ) );
    }

    internal static void Tick()
    {
      if( CotiState.Showing != CotiShowing.Message || CotiState.OpenCount == 0 )
        return;

      var width = Mathf.Max( 16, Screen.width );
      var height = Mathf.Max( 16, Screen.height );

      if( EnsureTarget( width, height ) || !IsCurrent( CotiState.Message ) )
        Draw( CotiState.Message );
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

    /// <summary>
    /// The redraw key: the message, the mode, and each open circle's label, text placement and geometry.
    /// The placement follows the device file, the geometry the device and the screen.
    /// </summary>
    private static bool IsCurrent( CotiDisplayMessage message )
    {
      if( !_drawn || _drawnMessage != message || _drawnMode != CurrentMode || _drawnCount != CotiState.OpenCount )
        return false;

      for( var i = 0; i < _drawnCount; i++ )
      {
        var drawn = _drawnCircles[i];
        var open = CotiState.OpenCircles[i];
        if( _drawnLabels[i] != CotiState.OpenLabels[i]
            || !_drawnTexts[i].Equals( CotiState.OpenTexts[i] )
            || drawn.U != open.U
            || drawn.V != open.V
            || drawn.Radius != open.Radius )
          return false;
      }

      return true;
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

    private static void Draw( CotiDisplayMessage message )
    {
      Remember( message );

      var image = ImageFor( message );
#if COTI_DEV
      var state = image != null ? image.width + "x" + image.height : ReferenceEquals( image, null ) ? "never loaded" : "destroyed";
      Plugin.Log.LogInfo( $"[COTI] message {message}: image {state}, " +
                          $"target {_target.width}x{_target.height}, {CotiState.OpenCount} open circle(s)" );
#endif

      var previous = RenderTexture.active;
      RenderTexture.active = _target;
      GL.Clear( false, true, Color.black );

      // Blank, or a missing file: the lit, empty display
      if( image != null )
        DrawCopies( image );

      RenderTexture.active = previous;
    }

    /// <summary>
    /// Each copy into its own rectangle, with the target active. Graphics.Blit writes every texel of
    /// the target, so a second copy drawn that way would erase the first.
    /// </summary>
    private static void DrawCopies( Texture2D image )
    {
      var width = _target.width;
      var height = _target.height;
      var aspect = (float)width / height;

      GL.PushMatrix();
      // Top-left origin, the convention Graphics.DrawTexture's rectangles use.
      GL.LoadPixelMatrix( 0f, width, height, 0f );

      for( var i = 0; i < CotiState.OpenCount; i++ )
      {
        CotiTextRect rect;
        if( !CotiDisplayLayout.TryRect( CotiState.OpenCircles[i], CotiState.OpenTexts[i], aspect,
                image.width, image.height, out rect ) )
          continue;

        Graphics.DrawTexture(
            new Rect( rect.X * width, ( 1f - rect.Y - rect.Height ) * height, rect.Width * width, rect.Height * height ),
            image );
      }

      GL.PopMatrix();
    }

    private static void Remember( CotiDisplayMessage message )
    {
      _drawn = true;
      _drawnMessage = message;
      _drawnMode = CurrentMode;
      _drawnCount = CotiState.OpenCount;
      for( var i = 0; i < _drawnCount; i++ )
      {
        _drawnCircles[i] = CotiState.OpenCircles[i];
        _drawnLabels[i] = CotiState.OpenLabels[i];
        _drawnTexts[i] = CotiState.OpenTexts[i];
      }
    }

    private static CotiThermalMode CurrentMode => Plugin.Config?.Image?.Mode ?? CotiThermalMode.Outline;

    private static Texture2D ImageFor( CotiDisplayMessage message )
    {
      switch( message )
      {
        case CotiDisplayMessage.Initializing: return _initializing;
        case CotiDisplayMessage.PowerOff: return _powerOff;
        case CotiDisplayMessage.Mode: return CurrentMode == CotiThermalMode.Full ? _full : _outline;
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
