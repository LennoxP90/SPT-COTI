using Coti.Shared;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  internal static class CotiOverlayCompositor
  {
    private const CameraEvent InjectionPoint = CameraEvent.AfterEverything;

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

    /// <summary>
    /// The message's brightness. 1 renders the letters at exactly HotColour, the tube's own
    /// near-white phosphor tint.
    /// </summary>
    private const float TextIntensity = 1f;

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;
    private static Material _material;

    private static Texture _setMainTex;
    private static Texture _setMaskTex;
    private static float _setThreshold = float.NaN;
    private static float _setOutlineMix = float.NaN;
    private static float _setOutlineWidth = float.NaN;
    private static float _setOutlineWorldWidth = float.NaN;
    private static float _setPixelsPerMetre = float.NaN;
    private static float _setOutlineMinTexels = float.NaN;
    private static float _setIntensity = float.NaN;
    private static Color? _setHotColour;
    private static Color? _setCoolColour;
    private static Texture _builtSource;

    private static bool _broken;
    private static bool _loggedAttached;

    // Phosphor tint state. The NightVision component is cached per camera rather than fetched
    // every frame; the camera only changes between raids.
    private static bool _loggedTint;
    private static Camera _nightVisionCamera;
    private static BSG.CameraEffects.NightVision _nightVision;

    /// <summary>
    /// The tint last written to the material, published so the magnified path can match it.
    ///
    /// Initialised to the shader's own defaults and only reassigned where the material is written,
    /// so it describes what the material holds even when ApplyPhosphorTint declines to touch it.
    /// </summary>
    internal static Color HotColour { get; private set; } = new Color( 1.0f, 0.95f, 0.85f, 1f );

    /// <inheritdoc cref="HotColour"/>
    internal static Color CoolColour { get; private set; } = new Color( 0.9f, 0.45f, 0.15f, 1f );

    internal static void Sync()
    {
      try
      {
        // Switching the overlay off is the retry: nothing else clears the latch.
        if( !( Plugin.Config?.Enabled ?? true ) )
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

        var camera = Camera.main;

        // While the magnified composite is drawing, the 1x overlay stands down entirely. Cutting a
        // hole for the lens can only approximate the tilted disc, and its rim reads as a second
        // circle inside the scope.
        //
        // Both conditions, because they fail independently: standing down for a composite that is
        // not running would leave no thermal at all.
        if( CotiOpticThermalCamera.Magnifying && CotiOpticOverlayCompositor.Attached )
        {
          Detach();
          return;
        }

        // Attached only while there is something to draw.
        var source = CurrentSource();
        var wanted = source != null
                     && CotiState.Host != null
                     && CotiState.Mask != null
                     && CotiShaderBundle.OverlayMaterial != null;

        if( !wanted || camera == null )
        {
          Detach();
          return;
        }

        if( _attachedTo != camera )
          Detach();

        EnsureBuffer( camera, source );

        ApplyMaterialValues( source );
      }
      catch( Exception ex )
      {
        Detach();
        _broken = true;
        Plugin.Log.LogError(
            $"[COTI] Overlay composite disabled - switch the overlay off and on to retry: {ex}" );
      }
    }

    /// <summary>
    /// The thermal while it runs, the device's own message while it boots or shuts down. Both go
    /// through the same material, so the message glows in the phosphor tint inside the circle.
    /// </summary>
    private static Texture CurrentSource()
    {
      if( !CotiThermalCamera.ModeEnabled )
        return null;

      switch( CotiState.Showing )
      {
        case CotiShowing.Thermal:
          return CotiState.Active && CotiThermalCamera.HasOutput ? CotiThermalCamera.Output : null;
        case CotiShowing.Message:
          return CotiDisplayText.Output;
        default:
          return null;
      }
    }

    private static void EnsureBuffer( Camera camera, Texture thermal )
    {
      if( _commandBuffer != null
          && _attachedTo == camera
          && ReferenceEquals( _builtSource, thermal ) )
      {
        return;
      }

      Detach();

      // The material from the bundle, not one constructed from the shader. A material built from
      // a shader whose compiled programs were stripped at build time renders nothing while
      // reporting isSupported=true.
      _material = CotiShaderBundle.OverlayMaterial;
      ForgetMaterialValues();

      _commandBuffer = new CommandBuffer { name = "COTI overlay" };

      // The entire composite. The shader blends additively, so the destination is only written
      // to and needs no temporary target or frame copy.
      _commandBuffer.Blit( thermal, BuiltinRenderTextureType.CameraTarget, _material );

      camera.AddCommandBuffer( InjectionPoint, _commandBuffer );

      _attachedTo = camera;
      _builtSource = thermal;

      if( !_loggedAttached )
      {
        _loggedAttached = true;
        Plugin.Log.LogInfo(
            $"[COTI] Overlay attached at {InjectionPoint} using additive blit " +
            $"(thermal {thermal.width}x{thermal.height}, no frame read-back)" );
      }
    }

    /// <summary>
    /// Pushes the overlay's inputs onto the material, writing only what changed.
    ///
    /// A message pins threshold, outline and intensity, so a player's outline or high-threshold
    /// preset cannot hollow out or hide the letters. The change-gated setters put the player's
    /// values back on the first thermal frame.
    /// </summary>
    private static void ApplyMaterialValues( Texture source )
    {
      var image = Plugin.Config.Image;

      SetTextureIfChanged( MainTexId, source, ref _setMainTex );
      SetTextureIfChanged( MaskTexId, CotiState.Mask, ref _setMaskTex );

      ApplyPhosphorTint();

      if( CotiState.Showing == CotiShowing.Message )
      {
        SetFloatIfChanged( ThresholdId, 0f, ref _setThreshold );
        SetFloatIfChanged( OutlineMixId, 0f, ref _setOutlineMix );
        SetFloatIfChanged( IntensityId, TextIntensity * PhosphorFade, ref _setIntensity );
        return;
      }

      SetFloatIfChanged( ThresholdId, Mathf.Clamp01( image.HeatThreshold ), ref _setThreshold );
      SetFloatIfChanged( OutlineMixId, Mathf.Clamp01( image.OutlineMix ), ref _setOutlineMix );
      SetOutlineRange( image, CotiThermalCamera.Output == null ? 0 : CotiThermalCamera.Output.height,
          _attachedTo != null ? _attachedTo.pixelHeight : Screen.height, 1f );

      // Perspective outline, only with the heat-only thermal: it is what puts each surface's distance in alpha.
      var thickness = CotiShaderBundle.HeatOnly != null ? Mathf.Max( 0f, image.OutlineThicknessCm ) / 100f : 0f;
      SetFloatIfChanged( OutlineWorldWidthId, thickness, ref _setOutlineWorldWidth );
      SetFloatIfChanged( PixelsPerMetreId, CotiOverlayScale.PixelsPerMetre(
          CotiThermalCamera.Output == null ? 0 : CotiThermalCamera.Output.height, CotiThermalCamera.FieldOfView ), ref _setPixelsPerMetre );
      SetFloatIfChanged( IntensityId,
          Mathf.Max( 0f, image.OverlayIntensity ) * PhosphorFade * CotiPowerToggle.Frame.GainBoost,
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
    /// Drops what this compositor believes the material already holds. Called wherever _material is
    /// reassigned or the buffer detached, since the values are the material's and not ours.
    /// </summary>
    private static void ForgetMaterialValues()
    {
      _setMainTex = null;
      _setMaskTex = null;
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
    /// Tints the heat to the tube's phosphor colour, read at runtime so it follows the player's
    /// own settings rather than a per-host value that would go stale.
    ///
    /// Hot trends toward white: it is brightness that reads as heat, and a green blob on a green
    /// image would not.
    ///
    /// The phosphor comes from whichever mod owns the tube - see CotiTubeBridge. NightVision.Color
    /// is only the fallback, since Borkel 3.0 does not write it.
    /// </summary>
    private static void ApplyPhosphorTint()
    {
      var nightVision = ResolveNightVision();
      if( nightVision == null )
      {
        PhosphorFade = 1f;
        return;
      }

      PhosphorFade = ComputeFade( nightVision );

      Color phosphor;
      bool fromBridge = CotiTubeBridge.TryPhosphor( nightVision, out phosphor );
      if( !fromBridge )
        phosphor = nightVision.Color;

      // Leave the shader's own defaults in place rather than tint the heat to nothing, which an
      // unwritten NightVision.Color would do under Borkel 3.0.
      float hueR, hueG, hueB;
      if( !CotiPhosphorTint.TryHue( phosphor.r, phosphor.g, phosphor.b, out hueR, out hueG, out hueB ) )
        return;

      float hotR, hotG, hotB;
      CotiPhosphorTint.Hot( hueR, hueG, hueB, out hotR, out hotG, out hotB );

      HotColour = new Color( hotR, hotG, hotB, 1f );
      CoolColour = new Color( hueR, hueG, hueB, 1f );

      SetColorIfChanged( HotColourId, HotColour, ref _setHotColour );
      SetColorIfChanged( CoolColourId, CoolColour, ref _setCoolColour );

      if( !_loggedTint )
      {
        _loggedTint = true;

        // "NightVision.Color" with Borkel 3.0 installed means the bridge failed to bind.
        Plugin.Log.LogInfo(
            $"[COTI] Heat tinted from the tube's phosphor: " +
            $"({hueR:F2}, {hueG:F2}, {hueB:F2}) " +
            $"via {( fromBridge ? "the tube's own renderer" : "NightVision.Color" )}" );
      }
    }

    internal static float PhosphorFade { get; private set; } = 1f;

    /// <summary>
    /// The main camera's NightVision, for the timing trace. Exposed rather than resolved a second
    /// time so the trace reports the same component the tint and fade were computed from.
    /// </summary>
    internal static BSG.CameraEffects.NightVision Tube
    {
      get { return ResolveNightVision(); }
    }

    /// <summary>
    /// Rides vanilla's switch flash - a ~100 ms dip in CurrentColor as the tube lights, not a fade
    /// across the whole switch. A replacement renderer does not draw that flash, so following it
    /// there would blink the overlay out with nothing on screen to explain it.
    /// </summary>
    private static float ComputeFade( BSG.CameraEffects.NightVision nightVision )
    {
      if( CotiTubeBridge.Present )
        return 1f;

      var full = nightVision.Color;
      var current = EftCompat.NightVisionCurrentColor( nightVision );

      return CotiPhosphorTint.Fade(
          current.r + current.g + current.b,
          full.r + full.g + full.b );
    }

    /// <summary>
    /// The main camera's NightVision component, cached per camera. GetComponent every frame would
    /// be wasteful, and the camera only changes between raids.
    /// </summary>
    private static BSG.CameraEffects.NightVision ResolveNightVision()
    {
      var camera = Camera.main;
      if( camera == null )
        return null;

      if( !ReferenceEquals( _nightVisionCamera, camera ) )
      {
        _nightVisionCamera = camera;
        _nightVision = camera.GetComponent<BSG.CameraEffects.NightVision>();
        _loggedTint = false;
      }

      return _nightVision;
    }

    /// <summary>
    /// What this material is actually set to. While a magnified
    /// optic is up this compositor is detached, so ApplyMaterialValues does not run and these hold
    /// whatever was last set before the scope came up.
    /// </summary>
    internal static string DescribeMaterial()
    {
      if( _material == null )
        return "(no material)";

      return $"threshold={_material.GetFloat( ThresholdId ):F2} " +
             $"intensity={_material.GetFloat( IntensityId ):F2} " +
             $"outlineMix={_material.GetFloat( OutlineMixId ):F2} " +
             $"outlineWidth={_material.GetFloat( OutlineWidthId ):F2} " +
             $"attached={( _commandBuffer != null && _attachedTo != null )}";
    }

    internal static RenderTexture RenderOverlayForDiagnostics( int width, int height )
    {
      if( _material == null || CotiThermalCamera.Output == null )
        return null;

      var target = new RenderTexture( width, height, 0, RenderTextureFormat.ARGB32 )
      {
        name = "CotiOverlayDiagnostic",
      };
      target.Create();

      var previous = RenderTexture.active;
      RenderTexture.active = target;
      GL.Clear( false, true, Color.black );
      RenderTexture.active = previous;

      Graphics.Blit( CotiThermalCamera.Output, target, _material );
      return target;
    }

    internal static void Detach()
    {
      if( _commandBuffer != null && _attachedTo != null )
      {
        try
        {
          _attachedTo.RemoveCommandBuffer( InjectionPoint, _commandBuffer );
        }
        catch( Exception ex )
        {
          // A destroyed camera can throw here; the buffer is being dropped regardless.
          Plugin.Log.LogWarning( $"[COTI] Removing overlay command buffer failed: {ex.Message}" );
        }
      }

      _commandBuffer?.Release();
      _commandBuffer = null;
      _attachedTo = null;
      _builtSource = null;
    }
  }
}
