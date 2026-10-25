using System;
using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// The thermal riser's 1x heat, drawn only inside the lens of the red dot on the riser, so it follows the sight the
  /// way the dot does. During the riser's boot, shutdown and mode change the same lens shows its message instead, or,
  /// while a magnifier on the riser is aimed, the magnified lens does: a flat overlay drawn after the scope's picture,
  /// never through the zoomed camera, so the letters keep their size and hold still on the lens.
  ///
  /// No second camera and no new shader. The lens mesh of every red dot near the eye is drawn white into a screen-sized
  /// mask, and the 1x overlay's own pass composites the 1x thermal through that mask instead of through the tube
  /// circles. A device of its own: the 1x thermal runs for it with no goggles, and it draws white-hot rather than in a
  /// tube's phosphor.
  ///
  /// Not covered: nothing hides the heat or the text where a hand covers the lens.
  /// </summary>
  internal static class CotiCollimatorCompositor
  {
    private const CameraEvent InjectionPoint = CameraEvent.AfterEverything;
    private const int CompositePass = 0;
    private const int AddPass = 1;

    /// <summary>How near the eye a red dot must be to count as the player's own: bots' sights are metres away.</summary>
    private const float OwnSightRange = 0.6f;

    private static readonly int MainTexId = Shader.PropertyToID( "_MainTex" );
    private static readonly int MaskTexId = Shader.PropertyToID( "_MaskTex" );
    private static readonly int CircleCountId = Shader.PropertyToID( "_CircleCount" );
    private static readonly int CircleGlowId = Shader.PropertyToID( "_CircleGlow" );
    private static readonly int ThermalRectId = Shader.PropertyToID( "_ThermalRect" );
    private static readonly int HotColourId = Shader.PropertyToID( "_HotColour" );
    private static readonly int CoolColourId = Shader.PropertyToID( "_CoolColour" );
    private static readonly int ThresholdId = Shader.PropertyToID( "_Threshold" );
    private static readonly int OutlineMixId = Shader.PropertyToID( "_OutlineMix" );
    private static readonly int HeatBrightnessId = Shader.PropertyToID( "_HeatBrightness" );
    private static readonly int IntensityId = Shader.PropertyToID( "_Intensity" );
    private static readonly int OutlineWorldWidthId = Shader.PropertyToID( "_OutlineWorldWidth" );
    private static readonly int CutTexId = Shader.PropertyToID( "_CutTex" );

    private static readonly List<CollimatorSight> Sights = new List<CollimatorSight>();
    private static readonly List<Renderer> Lenses = new List<Renderer>();

    /// <summary>Each lens's window (<see cref="WindowOf"/>), worked out once per lens.</summary>
    private static readonly Dictionary<Renderer, Renderer> Windows = new Dictionary<Renderer, Renderer>();

    /// <summary>
    /// The glass that stands for a lens's window: the lens itself, or, when a sibling mode's glass on the same sight is
    /// over four times larger across, that one. TGC's Nomad scales its default mode's glass to 2.5% (half a millimetre),
    /// so the window, the boot text and the heat all landed in 2 pixels. The modes share one window.
    /// </summary>
    internal static Renderer WindowOf( Renderer lens )
    {
      if( lens == null )
        return null;
      if( Windows.TryGetValue( lens, out var window ) && window != null )
        return window;

      window = lens;
      // Each mode's glass sits under its own mode_00x node, so search the sight's item from its root (its
      // ScopePrefabCache, as CotiScopeCut finds it); never above it, where another sight's glass could be.
      var item = lens.GetComponentInParent<ScopePrefabCache>();
      var modes = item != null ? item.GetComponentsInChildren<CollimatorSight>( true ) : null;

      if( modes != null )
      {
        var size = Across( lens );
        foreach( var other in modes )
        {
          var glass = other.CollimatorMeshRenderer;
          var across = Across( glass );
          if( glass != null && across > size * 4f )
          {
            window = glass;
            size = across;
          }
        }
      }

      Windows[lens] = window;
      return window;
    }

    // The mesh's own size through its world scale, read from the mesh: a renderer on an inactive mode reports empty
    // bounds, which is how the first try measured every sibling of TGC's Nomad as 0.
    private static float Across( Renderer glass )
    {
      var mesh = glass != null ? glass.GetComponent<MeshFilter>()?.sharedMesh : null;
      return mesh == null ? 0f : Vector3.Scale( mesh.bounds.size, glass.transform.lossyScale ).magnitude;
    }

    /// <summary>
    /// Draws a lens's window (<see cref="WindowOf"/>) into a mask: the lens itself, or, when it is shrunk, its own mesh
    /// at a full-size sibling's scale (<see cref="WindowMatrix"/>).
    /// </summary>
    internal static void DrawWindow( CommandBuffer buffer, Renderer lens, Material material, int pass )
    {
      var hidden = CotiLensMeshes.Hidden( lens );
      if( hidden != null )
      {
        buffer.DrawMesh( hidden, lens.localToWorldMatrix, material, 0, pass );
        return;
      }

      var window = WindowOf( lens );
      var mesh = window == lens ? null : lens.GetComponent<MeshFilter>()?.sharedMesh;
      if( mesh == null )
      {
        buffer.DrawRenderer( lens, material, 0, pass );
        return;
      }

      buffer.DrawMesh( mesh, WindowMatrix( lens, window ), material, 0, pass );
    }

    /// <summary>
    /// The dot's own glass where the dot is, at a full-size sibling's scale: the Nomad's other modes sit about 5 mm
    /// higher, so drawing them in place put the window off the dot. The modes' meshes are the same square.
    /// </summary>
    internal static Matrix4x4 WindowMatrix( Renderer lens, Renderer window )
    {
      var t = lens.transform;
      var local = Matrix4x4.TRS( t.localPosition, t.localRotation, window.transform.localScale );
      return t.parent != null ? t.parent.localToWorldMatrix * local : local;
    }

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;
    private static Material _material;
    private static Material _maskMaterial;
    private static RenderTexture _mask;
    private static RenderTexture _text;
    private static Texture _source;
    private static bool _subscribed;
    private static bool _broken;
    private static bool _loggedAttached;

    /// <summary>This frame's message, drawn at pre-cull with the lens where the camera sees it, and the lens it goes on.</summary>
    private static CotiDisplayMessage _message;
    private static CotiRiserTextLens _textLens;
    private static CotiDisplayMessage _loggedMessage;
    private static CotiRiserTextLens _loggedLens;

    /// <summary>The em the text is drawn at, held against small changes of the glass's box (CotiSightText.Hold).</summary>
    private static float _heldEm;
    private static CotiRiserTextLens _heldLens;

    /// <summary>The Debug setting forces it on for any red dot; otherwise a powered thermal riser turns it on.</summary>
    internal static bool Enabled => ThermalRiserState.Active || RiserMessage != CotiDisplayMessage.None;

    /// <summary>The riser's boot or shutdown message, shown in the sight window in place of the heat.</summary>
    private static CotiDisplayMessage RiserMessage =>
        ThermalRiserState.Present ? ThermalRiserState.Frame.Message : CotiDisplayMessage.None;

    /// <summary>
    /// The riser draws in its red dot window this frame, heat or text: the goggle COTI's 1x image is cut there
    /// (CotiScopeCut), so the two heat images never add up in the window. Never while a magnifier on the riser is aimed:
    /// the riser's heat and text are then on the magnified lens alone. Decided from this frame's state, not the attach,
    /// since the 1x overlay asks before this compositor syncs.
    /// </summary>
    internal static bool DrawsInWindow => !_broken && Enabled && !CotiOpticCamera.OpticOnRiser;

    /// <summary>The red dot windows the riser draws in, near the eye; refilled on every call.</summary>
    internal static List<Renderer> WindowLenses( Vector3 eye )
    {
      CollectOwnLenses( eye );
      return Lenses;
    }

    internal static void Sync()
    {
      try
      {
        if( !Enabled )
        {
          _broken = false;
          Detach();
          return;
        }

        if( _broken )
        {
          Detach();
          return;
        }

        Subscribe();

        var main = CotiFrame.Main;
        var message = RiserMessage;
        _message = message;
        _textLens = CotiDeviceKeys.RiserTextLens( CotiOpticCamera.OpticOnRiser );
        if( message == CotiDisplayMessage.None )
          _loggedMessage = CotiDisplayMessage.None;
        // Through a magnifier on the riser its heat is the magnified view's alone: the red dot window behind the
        // eyepiece would otherwise get the 1x heat as well, drawn over the lens with no depth test.
        _source = message != CotiDisplayMessage.None ? EnsureText( main )
            : !CotiOpticCamera.OpticOnRiser && CotiThermalCamera.ModeEnabled && CotiThermalCamera.HasOutput
                ? CotiThermalCamera.Output : null;

        if( main == null || _source == null || CotiShaderBundle.OverlayMaterial == null || !EnsureMaterials() )
        {
          Detach();
          return;
        }

        EnsureMask( main );
        ApplyMaterialValues( message != CotiDisplayMessage.None );
        Attach( main );
      }
      catch( Exception ex )
      {
        Detach();
        _broken = true;
        Plugin.Log.LogError( "[COTI] Thermal riser view disabled - power the riser off and on to retry: " + ex );
      }
    }

    /// <summary>
    /// The game announces every red dot as it enables and disables. Ones already enabled when the toggle comes on are
    /// picked up by a single search.
    /// </summary>
    private static void Subscribe()
    {
      if( _subscribed )
        return;

      CollimatorSight.OnCollimatorEnabled += sight => Sights.Add( sight );
      CollimatorSight.OnCollimatorDisabled += sight => Sights.Remove( sight );
      Sights.AddRange( UnityEngine.Object.FindObjectsOfType<CollimatorSight>() );
      _subscribed = true;
    }

    private static bool EnsureMaterials()
    {
      if( _material != null && _maskMaterial != null )
        return true;

      // From the bundle material, never the bare shader: see CotiOverlayCompositor.EnsureBuffer.
      _material = new Material( CotiShaderBundle.OverlayMaterial ) { name = "CotiRedDotOverlay" };
      // The goggle COTI's cut is set on the shared material and must not reach this copy: it would cut the window itself.
      _material.SetTexture( CutTexId, Texture2D.blackTexture );
      CotiShaderBundle.KeepLoaded( _material );

      // The copy pass with a white source paints the lens white into the mask.
      _maskMaterial = new Material( CotiShaderBundle.OverlayMaterial ) { name = "CotiRedDotMask" };
      _maskMaterial.SetTexture( MainTexId, Texture2D.whiteTexture );
      _maskMaterial.SetTexture( CutTexId, Texture2D.blackTexture );
      CotiShaderBundle.KeepLoaded( _maskMaterial );
      return true;
    }

    private static void EnsureMask( Camera main )
    {
      if( _mask != null && _mask.width == main.pixelWidth && _mask.height == main.pixelHeight )
        return;

      if( _mask != null )
        _mask.Release();

      _mask = new RenderTexture( main.pixelWidth, main.pixelHeight, 0, RenderTextureFormat.R8 )
      {
        name = "CotiRedDotMask",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };
      _mask.Create();
    }

    /// <summary>
    /// The player's image settings in the device's own white-hot colours, with the lens mask in place of the tube
    /// circles. Set every frame: a handful of values on one material, not worth the change tracking the other
    /// compositors do.
    /// </summary>
    private static void ApplyMaterialValues( bool text )
    {
      var image = Plugin.Config.Image;
      var box = text ? CotiCropBox.Whole : CotiThermalCamera.Box;

      _material.SetFloat( CircleCountId, 0f );
      _material.SetFloat( CircleGlowId, 0f );
      _material.SetTexture( MaskTexId, _mask );
      _material.SetTexture( MainTexId, _source );
      _material.SetVector( ThermalRectId, new Vector4( box.X, box.Y, box.Width, box.Height ) );
      _material.SetColor( HotColourId, Color.white );
      _material.SetColor( CoolColourId, new Color( 0.55f, 0.55f, 0.55f, 1f ) );
      // The message is drawn screen-sized and shown as drawn, the way CotiOverlayCompositor shows the goggle's.
      _material.SetFloat( ThresholdId, text ? 0f : Mathf.Clamp01( image.HeatThreshold ) );
      _material.SetFloat( OutlineMixId, text ? 0f : Mathf.Clamp01( image.OutlineMix ) );
      _material.SetFloat( HeatBrightnessId, text ? 1f : Mathf.Clamp( image.HeatBrightness, 0.25f, 2f ) );
      _material.SetFloat( IntensityId, text ? 1f : Mathf.Max( 0f, image.OverlayIntensity ) );
      // The perspective outline needs the 1x compositor's scale; the screen-space outline is enough here.
      _material.SetFloat( OutlineWorldWidthId, 0f );
    }

    private static void Attach( Camera main )
    {
      if( _attachedTo == main )
        return;

      Detach();

      if( _commandBuffer == null )
        _commandBuffer = new CommandBuffer { name = "COTI red dot overlay" };
      _commandBuffer.Clear();

      main.AddCommandBuffer( InjectionPoint, _commandBuffer );
      _attachedTo = main;
      Camera.onPreRender += RecordBeforeRendering;

      if( !_loggedAttached )
      {
        _loggedAttached = true;
        Plugin.Log.LogInfo( $"[COTI] Red dot overlay attached to {main.name} at {InjectionPoint}" );
      }
    }

    private static void RecordBeforeRendering( Camera rendering )
    {
      // ==, not ReferenceEquals: see CotiOpticOverlayCompositor.RecordBeforeCulling.
      if( _broken || _commandBuffer == null || rendering != _attachedTo )
        return;

      try
      {
        Record( rendering );
      }
      catch( Exception ex )
      {
        _commandBuffer.Clear();
        _broken = true;
        Plugin.Log.LogError( "[COTI] Thermal riser view disabled - power the riser off and on to retry: " + ex );
      }
    }

    /// <summary>
    /// Recorded at pre-render, when the camera's matrices are final for the frame, so the mask lands on the lens as drawn.
    /// Not at pre-cull: PiP-Disabler moves the eye toward the scope's axis in its own pre-cull, for the scopes it gives a
    /// visual recoil compensation, and a window recorded before that sat 2 to 3 mrad low (230 px at 0.6 degrees).
    /// The message is drawn here too, from the same matrices, so the text holds still on the lens while the weapon
    /// sways; drawn from Update it would trail the lens by a frame.
    /// </summary>
    private static void Record( Camera main )
    {
      var buffer = _commandBuffer;
      buffer.Clear();

      CollectMessageLenses( main.transform.position );
      if( Lenses.Count == 0 || _source == null || _mask == null )
        return;

      if( _message != CotiDisplayMessage.None )
        DrawMessage( main, _message );

      buffer.SetRenderTarget( _mask );
      buffer.ClearRenderTarget( false, true, Color.clear );
      buffer.SetViewProjectionMatrices( main.worldToCameraMatrix, main.projectionMatrix );
      foreach( var lens in Lenses )
        DrawWindow( buffer, lens, _maskMaterial, AddPass );

      buffer.Blit( _source, BuiltinRenderTextureType.CameraTarget, _material, CompositePass );
    }

    /// <summary>The screen-sized target the message is drawn into, one texel per screen pixel.</summary>
    private static Texture EnsureText( Camera main )
    {
      if( main == null )
        return null;

      if( _text == null || _text.width != main.pixelWidth || _text.height != main.pixelHeight )
      {
        if( _text != null )
          _text.Release();
        _text = new RenderTexture( main.pixelWidth, main.pixelHeight, 0, RenderTextureFormat.ARGB32 ) { name = "CotiRedDotText" };
        _text.Create();
      }

      return _text;
    }

    /// <summary>
    /// The message drawn into the text target, centred on the lenses' screen bounds so it sits in the glass, its size
    /// from that box alone (CotiSightText.EmPixels): the same share of the glass in the red dot window and on the
    /// magnified lens, whatever the zoom or the message. Redrawn every frame it shows, a second or two per press.
    /// </summary>
    private static void DrawMessage( Camera main, CotiDisplayMessage message )
    {
      var bounds = new Rect();
      var first = true;
      foreach( var lens in Lenses )
      {
        // The mesh's own box through its transform, not the renderer's world box: that one is axis-aligned, so it is
        // loose around a tilted eyepiece and its centre drifts off the glass.
        var window = WindowOf( lens );
        var hidden = CotiLensMeshes.Hidden( lens );
        var local = hidden != null ? hidden.bounds
            : window == lens ? lens.localBounds : lens.GetComponent<MeshFilter>().sharedMesh.bounds;
        var toWorld = hidden != null || window == lens ? lens.localToWorldMatrix : WindowMatrix( lens, window );
        for( var corner = 0; corner < 8; corner++ )
        {
          var point = new Vector3( ( corner & 1 ) == 0 ? local.min.x : local.max.x, ( corner & 2 ) == 0 ? local.min.y : local.max.y,
              ( corner & 4 ) == 0 ? local.min.z : local.max.z );
          var screen = main.WorldToScreenPoint( toWorld.MultiplyPoint3x4( point ) );
          // Screen points have a bottom-left origin; the drawing rectangle a top-left one.
          var pixel = new Vector2( screen.x, main.pixelHeight - screen.y );
          if( first )
            bounds = new Rect( pixel, Vector2.zero );
          else
            bounds = Rect.MinMaxRect( Mathf.Min( bounds.xMin, pixel.x ), Mathf.Min( bounds.yMin, pixel.y ),
                Mathf.Max( bounds.xMax, pixel.x ), Mathf.Max( bounds.yMax, pixel.y ) );
          first = false;
        }
      }

      // A switch between the red dot window and the magnified lens takes the new glass's size at once.
      if( _textLens != _heldLens )
        _heldEm = 0f;
      _heldLens = _textLens;
      _heldEm = CotiSightText.Hold( _heldEm, CotiSightText.EmPixels( bounds.width, bounds.height ) );

      CotiDisplayText.DrawInGlass( _text, message, bounds, _heldEm );
      LogMessageOnce( message, bounds );
    }

    /// <summary>
    /// One verbose line per message and lens: the glass's size on screen and the em drawn.
    /// </summary>
    private static void LogMessageOnce( CotiDisplayMessage message, Rect bounds )
    {
      if( message == _loggedMessage && _textLens == _loggedLens )
        return;

      _loggedMessage = message;
      _loggedLens = _textLens;
      if( !( Plugin.Config?.VerboseLogging ?? false ) )
        return;

      Plugin.Log.LogInfo( $"[COTI] Riser text {message} on the {_textLens}: lens {bounds.width:F0}x{bounds.height:F0} px, " +
                          $"em {_heldEm:F1} px" );
    }

    /// <summary>
    /// The lens this frame draws on: the aimed magnifier's lens while a message shows there (CotiDeviceKeys.RiserTextLens),
    /// else the red dot windows on the riser. Only one, so the text is never in both views.
    /// </summary>
    private static void CollectMessageLenses( Vector3 eye )
    {
      if( _message == CotiDisplayMessage.None || _textLens == CotiRiserTextLens.RedDotWindow )
      {
        CollectOwnLenses( eye );
        return;
      }

      Lenses.Clear();
      var sight = CotiOpticCamera.AimedSight;
      var lens = sight != null ? sight.LensRenderer : null;
      if( lens != null && lens.enabled && lens.gameObject.activeInHierarchy )
        Lenses.Add( lens );
    }

    private static void CollectOwnLenses( Vector3 eye )
    {
      Lenses.Clear();
      var rangeSquared = OwnSightRange * OwnSightRange;

      for( var i = Sights.Count - 1; i >= 0; i-- )
      {
        var sight = Sights[i];
        if( sight == null )
        {
          Sights.RemoveAt( i );
          continue;
        }

        var lens = sight.CollimatorMeshRenderer;
        if( lens != null && lens.enabled && lens.gameObject.activeInHierarchy
            && ( lens.transform.position - eye ).sqrMagnitude <= rangeSquared
            && ThermalRiserState.IsOnRiser( sight.transform ) )
          Lenses.Add( lens );
      }

      // A scope with no optic camera (PiP-Disabler) shows the zoomed main camera, which the 1x thermal already matches,
      // so its lens is one more window. Its renderer may be hidden by that mod; the mask draws it all the same.
      var screen = CotiOpticCamera.ScreenSight;
      var glass = screen != null ? screen.LensRenderer : null;
      if( glass != null && ThermalRiserState.IsOnRiser( screen.transform ) )
        Lenses.Add( glass );
    }

    internal static void Detach()
    {
      Camera.onPreRender -= RecordBeforeRendering;

      if( _attachedTo != null )
      {
        try
        {
          _attachedTo.RemoveCommandBuffer( InjectionPoint, _commandBuffer );
        }
        catch( Exception ex )
        {
          Plugin.Log.LogWarning( $"[COTI] Removing red dot command buffer failed: {ex.Message}" );
        }
      }
      _attachedTo = null;
    }
  }
}
