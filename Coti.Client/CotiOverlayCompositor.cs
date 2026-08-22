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
    private static readonly int HotColourId = Shader.PropertyToID( "_HotColour" );
    private static readonly int CoolColourId = Shader.PropertyToID( "_CoolColour" );
    private static readonly int LensCircleId = Shader.PropertyToID( "_LensCircle" );

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;
    private static Material _material;
    private static RenderTexture _builtThermal;

    private static bool _broken;
    private static bool _loggedAttached;

    // Phosphor tint state. The NightVision component is cached per camera rather than fetched
    // every frame; the camera only changes between raids.
    private static bool _loggedTint;
    private static Camera _nightVisionCamera;
    private static BSG.CameraEffects.NightVision _nightVision;

    private static bool _loggedLensExclusion;

    /// <summary>
    /// The lens ellipse last handed to the shader, as (centreU, centreV, radiusU, radiusV), or zero
    /// when none was. Published so the F9 probe can report what was ACTUALLY applied rather than
    /// recomputing it - a recomputation would answer a slightly different question.
    /// </summary>
    internal static Vector4 LensExclusion { get; private set; }

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

        // Attached only while there is genuinely something to draw.
        var wanted = CotiThermalCamera.ModeEnabled
                     && CotiState.Active
                     && CotiState.Host != null
                     && CotiState.Mask != null
                     && CotiThermalCamera.HasOutput
                     && CotiShaderBundle.OverlayMaterial != null;

        if( !wanted || camera == null )
        {
          Detach();
          return;
        }

        if( _attachedTo != camera )
          Detach();

        EnsureBuffer( camera );
        ApplyMaterialValues();
      }
      catch( Exception ex )
      {
        Detach();
        _broken = true;
        Plugin.Log.LogError(
            $"[COTI] Overlay composite disabled - switch the overlay off and on to retry: {ex}" );
      }
    }

    private static void EnsureBuffer( Camera camera )
    {
      var thermal = CotiThermalCamera.Output;

      if( _commandBuffer != null
          && _attachedTo == camera
          && ReferenceEquals( _builtThermal, thermal ) )
      {
        return;
      }

      Detach();

      // The material FROM THE BUNDLE, not one constructed from the shader. Constructing one
      // from a shader whose compiled programs were stripped at build time yields a material that
      // renders nothing while reporting isSupported=true - measured as overlay mean=0 max=0
      // against a threshold its input provably cleared.
      _material = CotiShaderBundle.OverlayMaterial;

      _commandBuffer = new CommandBuffer { name = "COTI overlay" };

      // The entire composite. No temporary targets, no frame copy, no ping-pong: the shader's
      // additive blend means the destination is only ever written to.
      _commandBuffer.Blit( thermal, BuiltinRenderTextureType.CameraTarget, _material );

      camera.AddCommandBuffer( InjectionPoint, _commandBuffer );

      _attachedTo = camera;
      _builtThermal = thermal;

      if( !_loggedAttached )
      {
        _loggedAttached = true;
        Plugin.Log.LogInfo(
            $"[COTI] Overlay attached at {InjectionPoint} using additive blit " +
            $"(thermal {thermal.width}x{thermal.height}, no frame read-back)" );
      }
    }

    private static void ApplyMaterialValues()
    {
      var image = Plugin.Config.Image;

      _material.SetTexture( MainTexId, CotiThermalCamera.Output );
      _material.SetTexture( MaskTexId, CotiState.Mask );
      LensExclusion = ResolveLensExclusion();
      _material.SetVector( LensCircleId, LensExclusion );
      ApplyPhosphorTint();
      _material.SetFloat( ThresholdId, Mathf.Clamp01( image.HeatThreshold ) );
      _material.SetFloat( OutlineMixId, Mathf.Clamp01( image.OutlineMix ) );
      _material.SetFloat( OutlineWidthId, CotiOverlayScale.OutlineWidth(
          Mathf.Max( 0.5f, image.OutlineWidth ),
          CotiThermalCamera.Output == null ? 0 : CotiThermalCamera.Output.height ) );
      _material.SetFloat( IntensityId, Mathf.Max( 0f, image.OverlayIntensity ) * PhosphorFade );
    }

    /// <summary>
    /// Tints the heat to the tube's phosphor colour, read off NightVision.Color at runtime so it
    /// follows the player's own settings rather than a per-host value that would go stale.
    ///
    /// Hot trends toward white: it is brightness that reads as heat, and a green blob on a green
    /// image would not.
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

      var phosphor = nightVision.Color;

      // A black or unset colour would tint the heat to nothing. Leave the shader's own defaults
      // in place instead - visible warm-white beats invisible.
      if( phosphor.r + phosphor.g + phosphor.b <= 0.01f )
        return;

      // Normalise: RNVG's colours vary in brightness, and an already-dim phosphor would
      // otherwise make the heat dimmer still. Only the HUE should come from the tube.
      var peak = Mathf.Max( phosphor.r, Mathf.Max( phosphor.g, phosphor.b ) );
      var hue = new Color( phosphor.r / peak, phosphor.g / peak, phosphor.b / peak, 1f );

      HotColour = Color.Lerp( hue, Color.white, 0.7f );
      CoolColour = hue;

      _material.SetColor( HotColourId, HotColour );
      _material.SetColor( CoolColourId, CoolColour );

      if( !_loggedTint )
      {
        _loggedTint = true;
        Plugin.Log.LogInfo(
            $"[COTI] Heat tinted from the tube's phosphor: " +
            $"({hue.r:F2}, {hue.g:F2}, {hue.b:F2})" );
      }
    }

    internal static float PhosphorFade { get; private set; } = 1f;

    /// <summary>
    /// The ellipse this overlay must NOT draw into, or zero for no exclusion.
    ///
    /// The 1x overlay paints straight over a magnified lens, so the magnified thermal underneath
    /// cannot be seen until it stops. Zero whenever the magnified path is not rendering, or the hole
    /// would just be a blind spot.
    /// </summary>
    private static Vector4 ResolveLensExclusion()
    {
      // Both, because they fail independently: a hole cut for a composite that is not running is a
      // dead circle. Attached describes the previous frame - Plugin.Update syncs this compositor
      // first - which costs one frame of 1x heat over the lens when magnification engages.
      if( !CotiOpticThermalCamera.Magnifying || !CotiOpticOverlayCompositor.Attached )
        return Vector4.zero;

      var lens = CotiOpticThermalCamera.Optic.Lens;
      var camera = Camera.main;
      if( lens == null || camera == null )
        return Vector4.zero;

      float minX, minY, maxX, maxY;
      if( !TryProjectBounds( lens.bounds, camera, out minX, out minY, out maxX, out maxY ) )
        return Vector4.zero;

      float centreU, centreV, radiusU, radiusV;
      if( !CotiOpticFusion.TryLensEllipse(
              minX, minY, maxX, maxY,
              camera.pixelWidth, camera.pixelHeight,
              Mathf.Max( 0f, Plugin.Config?.MagnifiedLensCover ?? 1f ),
              out centreU, out centreV, out radiusU, out radiusV ) )
      {
        return Vector4.zero;
      }

      if( !_loggedLensExclusion && ( Plugin.Config?.VerboseLogging ?? false ) )
      {
        _loggedLensExclusion = true;

        // A centred hole is expected: the mask is the COTI's own display, not the host's viewport.
        Plugin.Log.LogInfo(
            $"[COTI] 1x overlay began excluding the lens - FIRST frame only, mid-ADS and NOT the " +
            $"settled position: ({centreU:F3}, {centreV:F3}) radius ({radiusU:F3}, {radiusV:F3}) " +
            $"of a {camera.pixelWidth}x{camera.pixelHeight} view. F9 reports the steady state." );
      }

      return new Vector4( centreU, centreV, radiusU, radiusV );
    }

    /// <summary>
    /// Projects a world bounding box to the camera's own pixel space.
    ///
    /// camera.pixelWidth/Height, never Screen.width/height: EFT renders at a scaled resolution, and
    /// mixing the two spaces once produced a second copy of the mask scaled by that factor.
    /// </summary>
    private static bool TryProjectBounds(
        Bounds bounds, Camera camera,
        out float minX, out float minY, out float maxX, out float maxY )
    {
      minX = float.MaxValue;
      minY = float.MaxValue;
      maxX = float.MinValue;
      maxY = float.MinValue;

      for( var corner = 0; corner < 8; corner++ )
      {
        var point = new Vector3(
            ( corner & 1 ) == 0 ? bounds.min.x : bounds.max.x,
            ( corner & 2 ) == 0 ? bounds.min.y : bounds.max.y,
            ( corner & 4 ) == 0 ? bounds.min.z : bounds.max.z );

        var screen = camera.WorldToScreenPoint( point );

        // A corner behind the eye comes back with the wrong sign, so one bad corner poisons the
        // box. Rejected rather than clamped: a plausible wrong answer deletes overlay.
        if( screen.z <= 0f )
          return false;

        if( screen.x < minX ) minX = screen.x;
        if( screen.y < minY ) minY = screen.y;
        if( screen.x > maxX ) maxX = screen.x;
        if( screen.y > maxY ) maxY = screen.y;
      }

      return true;
    }

    internal static bool TubeSwitching
    {
      get
      {
        var nightVision = ResolveNightVision();
        return nightVision != null && nightVision.InProcessSwitching;
      }
    }

    private static float ComputeFade( BSG.CameraEffects.NightVision nightVision )
    {
      var full = nightVision.Color;
      var denominator = full.r + full.g + full.b;

      // A black configured colour makes the ratio meaningless; treat the tube as fully on rather
      // than dividing by zero and hiding the overlay forever.
      if( denominator <= 0.001f )
        return 1f;

      var current = EftCompat.NightVisionCurrentColor( nightVision );
      var ratio = ( current.r + current.g + current.b ) / denominator;

      // CurrentColor goes NEGATIVE past the midpoint of the flash, since the formula is
      // 1 - 2 * value with value running beyond 0.5 - so clamping is required, not cosmetic.
      return Mathf.Clamp01( ratio );
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
      _builtThermal = null;
    }
  }
}
