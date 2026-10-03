using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Draws the magnified thermal through the scope's own lens, on top of the finished frame.
  ///
  /// The game shows the scope camera's picture on the lens through the global <c>_CamTex</c>. Heat written into that
  /// picture went through the main camera's effects with the rest of the scene, and UltimateBloom haloed it. So once
  /// the main camera has finished, the heat is composited alone into a texture of its own, <c>_CamTex</c> points at it
  /// while the lens is drawn again into a scratch target with its reticle blanked, <c>_CamTex</c> is put back, and the
  /// scratch target is added onto the frame. The lens's own shader maps the heat, so it lands exactly where the
  /// picture does, and the game's picture is never touched.
  ///
  /// Recorded at the main camera's pre-cull, when its matrices are final for the frame.
  /// </summary>
  internal static class CotiOpticOverlayCompositor
  {
    private const CameraEvent InjectionPoint = CameraEvent.AfterEverything;
    private const int CompositePass = 0;
    private const int AddPass = 1;
    private const int LensDepthPass = 2;
    private const int AddVisiblePass = 3;

    /// <summary>
    /// The pass of CW FX/OpticSight that draws <c>_CamTex</c> through the lens's eye-box mask: found by drawing each of
    /// its five unnamed passes with the heat texture flat grey (Debug > Lens Probe 20 to 24), where only pass 2 drew.
    /// </summary>
    private const int LensPass = 2;

    private static readonly int MainTexId = Shader.PropertyToID( "_MainTex" );
    private static readonly int MaskTexId = Shader.PropertyToID( "_MaskTex" );
    private static readonly int IntensityId = Shader.PropertyToID( "_Intensity" );
    private static readonly int ThresholdId = Shader.PropertyToID( "_Threshold" );
    private static readonly int OutlineMixId = Shader.PropertyToID( "_OutlineMix" );
    private static readonly int OutlineWidthId = Shader.PropertyToID( "_OutlineWidth" );
    private static readonly int OutlineWorldWidthId = Shader.PropertyToID( "_OutlineWorldWidth" );
    private static readonly int PixelsPerMetreId = Shader.PropertyToID( "_PixelsPerMetre" );
    private static readonly int OutlineMinTexelsId = Shader.PropertyToID( "_OutlineMinTexels" );
    private static readonly int HotColourId = Shader.PropertyToID( "_HotColour" );
    private static readonly int CoolColourId = Shader.PropertyToID( "_CoolColour" );
    private static readonly int CircleGlowId = Shader.PropertyToID( "_CircleGlow" );

    /// <summary>The global the scope's lens samples its picture from (OpticCameraManager._camTexId).</summary>
    private static readonly int CamTexId = Shader.PropertyToID( "_CamTex" );
    private static readonly int MarkTexId = Shader.PropertyToID( "_MarkTex" );
    private static readonly int ScratchId = Shader.PropertyToID( "_CotiLensScratch" );

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;

    /// <summary>The magnified heat alone, composited on black: what <c>_CamTex</c> points at while the lens redraws.</summary>
    private static RenderTexture _heat;

    /// <summary>A copy of the lens's material with the reticle blanked, so the redraw does not add a second reticle.</summary>
    private static Material _lensMaterial;
    private static Material _lensSource;

    /// <summary>
    /// A private copy of the shared material. A command buffer reads the material's properties at
    /// render time, so two buffers sharing one would each draw with whatever the other set last.
    /// </summary>
    private static Material _material;

    private static Texture _setMainTex;
    private static Texture _setMaskTex;
    private static float _setCircleGlow = float.NaN;
    private static float _setThreshold = float.NaN;
    private static float _setOutlineMix = float.NaN;
    private static float _setOutlineWidth = float.NaN;
    private static float _setOutlineWorldWidth = float.NaN;
    private static float _setPixelsPerMetre = float.NaN;
    private static float _setOutlineMinTexels = float.NaN;
    private static float _setIntensity = float.NaN;
    private static Color? _setHotColour;
    private static Color? _setCoolColour;

    private static bool _broken;
    private static bool _loggedAttached;

    /// <summary>
    /// Whether the composite is attached and drawing. The 1x overlay stands down on this rather than
    /// on the camera: the two fail independently, and this one latches until the setting is
    /// toggled, so gating on the camera could leave no thermal at all.
    /// </summary>
    internal static bool Attached => _commandBuffer != null && _attachedTo != null && !_broken;

    internal static void Sync()
    {
      try
      {
        // Switching the setting off is the retry: nothing else clears the latch.
        if( Plugin.Config?.MagnifyWithOptic != true )
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

        var main = Camera.main;
        var wanted = CotiOpticThermalCamera.Magnifying
                     && CotiState.Active
                     && CotiState.Host != null
                     && CotiShaderBundle.OverlayMaterial != null
                     && CotiOpticThermalCamera.Optic.Lens != null;

        if( !wanted || main == null )
        {
          Detach();
          return;
        }

        EnsureBuffer( main );
        if( Attached )
          ApplyMaterialValues( main );
      }
      catch( Exception ex )
      {
        Detach();
        _broken = true;
        Plugin.Log.LogError(
            "[COTI] Magnified composite disabled - switch Magnify With Optic off and on to retry: "
            + ex );
      }
    }

    private static void EnsureBuffer( Camera main )
    {
      if( _commandBuffer != null && _attachedTo == main )
        return;

      Detach();

      if( !EnsureMaterial() )
        return;

      _commandBuffer = new CommandBuffer { name = "COTI magnified overlay" };
      main.AddCommandBuffer( InjectionPoint, _commandBuffer );
      _attachedTo = main;

      Camera.onPreCull -= RecordBeforeCulling;
      Camera.onPreCull += RecordBeforeCulling;

      if( !_loggedAttached )
      {
        _loggedAttached = true;
        Plugin.Log.LogInfo( $"[COTI] Magnified overlay attached to {main.name} at {InjectionPoint}, drawn through the scope's lens" );
      }
    }

    private static void RecordBeforeCulling( Camera rendering )
    {
      // ==, not ReferenceEquals: on il2cpp each callback hands over a fresh wrapper for the native camera, so reference
      // identity never matches there; Unity's == compares the native objects.
      if( _broken || _commandBuffer == null || rendering != _attachedTo )
        return;

      try
      {
        Record( rendering );
      }
      catch( Exception ex )
      {
        // Empty, never half-recorded: a buffer that set _CamTex without putting it back would show the heat alone in
        // place of the scope's picture.
        _commandBuffer.Clear();
        _broken = true;
        Plugin.Log.LogError(
            "[COTI] Magnified composite disabled - switch Magnify With Optic off and on to retry: " + ex );
      }
    }

    private static void Record( Camera main )
    {
      var buffer = _commandBuffer;
      buffer.Clear();

      var thermal = CotiOpticThermalCamera.Output;
      var optic = CotiOpticThermalCamera.Optic;
      var lens = optic.Lens;
      var picture = optic.Camera != null ? optic.Camera.targetTexture : null;
      if( thermal == null || lens == null || !lens.enabled || picture == null
          || !EnsureLensMaterial( lens.sharedMaterial ) || !EnsureHeat( thermal ) )
      {
#if COTI_DEV
        LogSkipOnce( thermal, lens, picture );
#endif
        return;
      }

#if COTI_DEV
      var probe = Plugin.Config?.ThermalCamera?.LensProbe ?? 0;
      LogRecordOnce( lens, picture, probe );
#endif

      buffer.SetRenderTarget( _heat );
#if COTI_DEV
      if( probe >= 20 )
        buffer.ClearRenderTarget( false, true, new Color( 0.5f, 0.5f, 0.5f, 1f ) );
      else
      {
        buffer.ClearRenderTarget( false, true, Color.clear );
        buffer.Blit( thermal, _heat, _material, CompositePass );
      }
#else
      buffer.ClearRenderTarget( false, true, Color.clear );
      buffer.Blit( thermal, _heat, _material, CompositePass );
#endif

      buffer.GetTemporaryRT( ScratchId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf );
      buffer.SetRenderTarget( ScratchId );
      buffer.ClearRenderTarget( false, true, Color.clear );
      buffer.SetGlobalTexture( CamTexId, _heat );
      buffer.SetViewProjectionMatrices( main.worldToCameraMatrix, main.projectionMatrix );
#if COTI_DEV
      if( probe == 3 )
        buffer.SetInvertCulling( true );
      if( probe == 2 )
        buffer.DrawRenderer( lens, _material, 0, AddPass );
      else
        buffer.DrawRenderer( lens, _lensMaterial, 0, probe >= 20 ? probe - 20 : probe >= 10 ? probe - 10 : LensPass );
#else
      buffer.DrawRenderer( lens, _lensMaterial, 0, LensPass );
#endif
      // The lens's own distance into the scratch target's alpha, so the add below can hide what the scope's housing
      // or a hand covers: this redraw has no depth buffer of its own.
      buffer.DrawRenderer( lens, _material, 0, LensDepthPass );
#if COTI_DEV
      if( probe == 3 )
        buffer.SetInvertCulling( false );
#endif
      buffer.SetGlobalTexture( CamTexId, picture );

#if COTI_DEV
      if( probe == 1 )
        buffer.Blit( _heat, BuiltinRenderTextureType.CameraTarget, _material, AddPass );
      else
#endif
      buffer.Blit( ScratchId, BuiltinRenderTextureType.CameraTarget, _material, AddVisiblePass );
      buffer.ReleaseTemporaryRT( ScratchId );
    }

#if COTI_DEV
    private static bool _loggedSkip;
    private static int _loggedProbe = -1;

    private static void LogSkipOnce( RenderTexture thermal, Renderer lens, Texture picture )
    {
      if( _loggedSkip )
        return;
      _loggedSkip = true;
      Plugin.Log.LogInfo( $"[COTI] lens redraw skipped: thermal={( thermal == null ? "null" : thermal.name )} " +
                          $"lens={( lens == null ? "null" : lens.name + ( lens.enabled ? "" : " (disabled)" ) )} " +
                          $"picture={( picture == null ? "null" : picture.name )} " +
                          $"lensMaterial={( lens == null || lens.sharedMaterial == null ? "null" : lens.sharedMaterial.name )}" );
    }

    private static void LogRecordOnce( Renderer lens, Texture picture, int probe )
    {
      if( _loggedProbe == probe )
        return;
      _loggedProbe = probe;
      var shader = _lensMaterial.shader;
      Plugin.Log.LogInfo( $"[COTI] lens redraw probe {probe}: lens {lens.name} layer {lens.gameObject.layer}, " +
                          $"material {_lensMaterial.name} shader {( shader == null ? "null" : shader.name )} " +
                          $"passes {_lensMaterial.passCount} [{PassNames( _lensMaterial )}], picture {picture.name}, " +
                          $"heat {_heat.width}x{_heat.height}" );
    }

    private static string PassNames( Material material )
    {
      var names = new string[material.passCount];
      for( var i = 0; i < names.Length; i++ )
        names[i] = i + ":" + material.shader.FindPassTagValue( i, new ShaderTagId( "LightMode" ) ).name;
      return string.Join( " ", names );
    }
#endif

    private static bool EnsureMaterial()
    {
      if( _material != null )
        return true;

      var shared = CotiShaderBundle.OverlayMaterial;
      if( shared == null )
        return false;

      // Built from the bundle material rather than the shader: a material built from a shader whose
      // programs were stripped renders nothing while reporting isSupported=true.
      _material = new Material( shared ) { name = "CotiMagnifiedOverlay" };
      CotiShaderBundle.KeepLoaded( _material );

      // The magnified heat is rendered full-frame; the copy may carry the 1x path's crop box from the shared material.
      _material.SetVector( Shader.PropertyToID( "_ThermalRect" ), new Vector4( 0f, 0f, 1f, 1f ) );

      // The lens samples the heat past its edges and clamps; a black frame keeps that from streaking an outline that
      // touches the edge out across the screen.
      _material.SetFloat( Shader.PropertyToID( "_BlackFrame" ), 1f );
      ForgetMaterialValues();
      return true;
    }

    private static bool EnsureLensMaterial( Material source )
    {
      if( source == null )
        return false;
      if( _lensMaterial != null && ReferenceEquals( _lensSource, source ) )
        return true;

      DestroyLensMaterial();
      _lensMaterial = new Material( source ) { name = "CotiLensRedraw" };
      _lensMaterial.SetTexture( MarkTexId, Texture2D.blackTexture );
      CotiShaderBundle.KeepLoaded( _lensMaterial );
      _lensSource = source;
      return true;
    }

    private static bool EnsureHeat( RenderTexture thermal )
    {
      if( _heat != null && _heat.width == thermal.width && _heat.height == thermal.height )
        return true;

      DestroyHeat();
      _heat = new RenderTexture( thermal.width, thermal.height, 0, RenderTextureFormat.ARGBHalf )
      {
        name = "CotiMagnifiedHeat",
        useMipMap = false,
        autoGenerateMips = false,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.DontUnloadUnusedAsset,
      };
      _heat.Create();
      return true;
    }

    /// <summary>
    /// Pushes the magnified overlay's inputs onto its own material, writing only what changed.
    ///
    /// Same reasoning as the 1x compositor: these are native setters, and only the intensity moves
    /// frame to frame.
    /// </summary>
    private static void ApplyMaterialValues( Camera main )
    {
      var image = Plugin.Config.Image;

      SetTextureIfChanged( MainTexId, CotiOpticThermalCamera.Output, ref _setMainTex );

      // No circle mask: inside the lens the whole picture is the sensor's view, and the lens is
      // already a circle the game draws. White passes the shader's multiply through.
      SetTextureIfChanged( MaskTexId, Texture2D.whiteTexture, ref _setMaskTex );

      // Same reason: the disc marks where the sensor points inside the tube image, and with the
      // mask open it would just lift the whole scope picture by a constant.
      SetFloatIfChanged( CircleGlowId, 0f, ref _setCircleGlow );

      SetFloatIfChanged( ThresholdId, Mathf.Clamp01( image.HeatThreshold ), ref _setThreshold );
      SetFloatIfChanged( OutlineMixId, Mathf.Clamp01( image.OutlineMix ), ref _setOutlineMix );

      // The magnified picture fills the lens, so a screen pixel is the lens's height in it, and the zoom lets a near
      // line thicken with the object.
      SetOutlineRange( image, CotiOpticThermalCamera.Output == null ? 0 : CotiOpticThermalCamera.Output.height,
          LensScreenRows( main ),
          CotiOpticFusion.Magnification( main.fieldOfView, CotiOpticThermalCamera.Optic.FieldOfView ) );

      // Perspective outline, only with the heat-only thermal: it is what puts each surface's distance in alpha.
      var thickness = CotiShaderBundle.HeatOnly != null ? Mathf.Max( 0f, image.OutlineThicknessCm ) / 100f : 0f;
      SetFloatIfChanged( OutlineWorldWidthId, thickness, ref _setOutlineWorldWidth );
      SetFloatIfChanged( PixelsPerMetreId, CotiOverlayScale.PixelsPerMetre(
          CotiOpticThermalCamera.Output == null ? 0 : CotiOpticThermalCamera.Output.height, CotiOpticThermalCamera.FieldOfView ), ref _setPixelsPerMetre );

      // Phosphor and switching fade from the 1x path: the magnified image sits inside the same
      // tube, so a different tint would read as two instruments.
      SetColorIfChanged( HotColourId, CotiOverlayCompositor.HotColour, ref _setHotColour );
      SetColorIfChanged( CoolColourId, CotiOverlayCompositor.CoolColour, ref _setCoolColour );

      // Scaled down - see CotiImageConfig.MagnifiedIntensityScale. Without the circle glow beneath
      // it, the 1x value drives contours past full scale and they clip to a solid mass.
      SetFloatIfChanged( IntensityId,
          Mathf.Max( 0f, image.OverlayIntensity )
              * Mathf.Clamp( image.MagnifiedIntensityScale, 0.05f, 1f )
              * CotiOverlayCompositor.PhosphorFade,
          ref _setIntensity );
    }

    /// <summary>
    /// The outline's floor and cap for a target of <paramref name="rows"/> shown <paramref name="screenRows"/> pixels
    /// tall at <paramref name="zoom"/>; see <see cref="CotiOverlayScale.OutlineRange"/>.
    /// </summary>
    private static void SetOutlineRange( CotiImageConfig image, int rows, float screenRows, float zoom )
    {
      float min, max;
      CotiOverlayScale.OutlineRange( Mathf.Max( 0.5f, image.OutlineWidth ), screenRows, zoom,
          CotiOverlayScale.TexelsPerPixel( rows, screenRows ), out min, out max );
      SetFloatIfChanged( OutlineMinTexelsId, min, ref _setOutlineMinTexels );
      SetFloatIfChanged( OutlineWidthId, max, ref _setOutlineWidth );
    }

    /// <summary>
    /// The lens's height on screen in pixels: its bounds' corners through the main camera. 0 without a lens.
    /// </summary>
    private static float LensScreenRows( Camera main )
    {
      var lens = CotiOpticThermalCamera.Optic.Lens;
      if( lens == null || main == null )
        return 0f;

      var bounds = lens.bounds;
      float low = float.MaxValue, high = float.MinValue;
      for( var i = 0; i < 8; i++ )
      {
        var corner = bounds.center + Vector3.Scale( bounds.extents,
            new Vector3( ( i & 1 ) == 0 ? -1f : 1f, ( i & 2 ) == 0 ? -1f : 1f, ( i & 4 ) == 0 ? -1f : 1f ) );
        var y = main.WorldToScreenPoint( corner ).y;
        low = Mathf.Min( low, y );
        high = Mathf.Max( high, y );
      }

      return high - low;
    }

    private static void SetFloatIfChanged( int id, float value, ref float last )
    {
      if( last == value )
        return;

      _material.SetFloat( id, value );
      last = value;
    }

    private static void SetColorIfChanged( int id, Color value, ref Color? last )
    {
      if( last.HasValue && last.Value == value )
        return;

      _material.SetColor( id, value );
      last = value;
    }

    private static void SetTextureIfChanged( int id, Texture value, ref Texture last )
    {
      if( ReferenceEquals( last, value ) )
        return;

      _material.SetTexture( id, value );
      last = value;
    }

    /// <summary>
    /// Drops what this compositor believes its material already holds. Called wherever the material
    /// is built, since a new material holds none of the cached values.
    /// </summary>
    private static void ForgetMaterialValues()
    {
      _setMainTex = null;
      _setMaskTex = null;
      _setCircleGlow = float.NaN;
      _setThreshold = float.NaN;
      _setOutlineMix = float.NaN;
      _setOutlineWidth = float.NaN;
      _setOutlineWorldWidth = float.NaN;
      _setPixelsPerMetre = float.NaN;
      _setOutlineMinTexels = float.NaN;
      _setIntensity = float.NaN;
      _setHotColour = null;
      _setCoolColour = null;
    }

    /// <summary>
    /// Renders the magnified overlay alone, so its contribution can be told apart from the scope
    /// picture it is added to.
    /// </summary>
    internal static RenderTexture RenderOverlayForDiagnostics( int width, int height )
    {
      if( _material == null || CotiOpticThermalCamera.Output == null )
        return null;

      var target = new RenderTexture( width, height, 0, RenderTextureFormat.ARGB32 )
      {
        name = "CotiMagnifiedOverlayDiagnostic",
      };
      target.Create();

      var previous = RenderTexture.active;
      RenderTexture.active = target;
      GL.Clear( false, true, Color.black );
      RenderTexture.active = previous;

      Graphics.Blit( CotiOpticThermalCamera.Output, target, _material, CompositePass );
      return target;
    }

    /// <summary>
    /// What the magnified material is set to, read back off the material rather than off config,
    /// so a value that fails to reach this path shows up as a difference.
    /// </summary>
    internal static string DescribeMaterial()
    {
      if( _material == null )
        return "(no material)";

      return $"threshold={_material.GetFloat( ThresholdId ):F2} " +
             $"intensity={_material.GetFloat( IntensityId ):F2} " +
             $"outlineMix={_material.GetFloat( OutlineMixId ):F2} " +
             $"outlineWidth={_material.GetFloat( OutlineWidthId ):F2} " +
             $"circleGlow={_material.GetFloat( CircleGlowId ):F3}";
    }

    internal static void Detach()
    {
      Camera.onPreCull -= RecordBeforeCulling;

      if( _commandBuffer != null && _attachedTo != null )
      {
        try
        {
          _attachedTo.RemoveCommandBuffer( InjectionPoint, _commandBuffer );
        }
        catch( Exception ex )
        {
          // The camera is destroyed between raids and throws here. The buffer is dropped regardless.
          Plugin.Log.LogWarning(
              $"[COTI] Removing magnified overlay command buffer failed: {ex.Message}" );
        }
      }

      _commandBuffer?.Release();
      _commandBuffer = null;
      _attachedTo = null;
    }

    /// <summary>
    /// Drops the materials and the heat texture as well as the buffer, for plugin shutdown. Detach runs on every
    /// weapon lower, where rebuilding them would be waste.
    /// </summary>
    internal static void Teardown()
    {
      Detach();

      if( _material != null )
      {
        UnityEngine.Object.Destroy( _material );
        _material = null;
      }

      DestroyLensMaterial();
      DestroyHeat();
      _loggedAttached = false;
      _broken = false;
    }

    private static void DestroyLensMaterial()
    {
      if( _lensMaterial != null )
        UnityEngine.Object.Destroy( _lensMaterial );
      _lensMaterial = null;
      _lensSource = null;
    }

    private static void DestroyHeat()
    {
      if( _heat != null )
      {
        _heat.Release();
        UnityEngine.Object.Destroy( _heat );
      }
      _heat = null;
    }
  }
}
