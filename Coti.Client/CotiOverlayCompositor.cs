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
      var host = CotiState.Host;

      _material.SetTexture( MainTexId, CotiThermalCamera.Output );
      _material.SetTexture( MaskTexId, CotiState.Mask );
      ApplyPhosphorTint();
      _material.SetFloat( ThresholdId, Mathf.Clamp01( host.HeatThreshold ) );
      _material.SetFloat( OutlineMixId, Mathf.Clamp01( host.OutlineMix ) );
      _material.SetFloat( OutlineWidthId, Mathf.Max( 0.5f, host.OutlineWidth ) );
      _material.SetFloat( IntensityId, Mathf.Max( 0f, host.OverlayIntensity ) * PhosphorFade );
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

      _material.SetColor( HotColourId, Color.Lerp( hue, Color.white, 0.7f ) );
      _material.SetColor( CoolColourId, hue );

      if( !_loggedTint )
      {
        _loggedTint = true;
        Plugin.Log.LogInfo(
            $"[COTI] Heat tinted from the tube's phosphor: " +
            $"({hue.r:F2}, {hue.g:F2}, {hue.b:F2})" );
      }
    }

    internal static float PhosphorFade { get; private set; } = 1f;

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

      var current = nightVision.CurrentColor;
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
