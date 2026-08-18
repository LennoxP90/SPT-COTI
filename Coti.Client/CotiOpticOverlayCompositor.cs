using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Composites the magnified thermal into the optic camera's own render target, which the game
  /// draws onto the lens as weapon geometry.
  ///
  /// Writing into the target means the scope's position, size and angle stay the game's problem.
  /// Every alternative had to locate the lens on screen, which cannot be done: the texture it
  /// displays is published with <c>Shader.SetGlobalTexture</c>, on no material and no property block.
  /// </summary>
  internal static class CotiOpticOverlayCompositor
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
    private static readonly int CircleGlowId = Shader.PropertyToID( "_CircleGlow" );
    private static readonly int LensCircleId = Shader.PropertyToID( "_LensCircle" );

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;
    private static RenderTexture _builtThermal;

    /// <summary>
    /// Our own copy, not the shared material. A command buffer reads the material's properties at
    /// render time, so two buffers sharing one would each draw with whatever the other set last.
    /// </summary>
    private static Material _material;

    private static bool _broken;
    private static bool _loggedAttached;

    /// <summary>
    /// Whether the composite is genuinely attached and drawing. The 1x overlay gates its lens hole
    /// on this rather than on the camera: the two fail independently, and this one latches until the
    /// setting is toggled, so gating on the camera left a dead circle with nothing behind it.
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

        // The camera publishes what it configured against. Re-reading here could attach the buffer
        // to a camera nothing rendered for.
        var optic = CotiOpticThermalCamera.Optic;

        var wanted = CotiOpticThermalCamera.Magnifying
                     && CotiState.Active
                     && CotiState.Host != null
                     && CotiShaderBundle.OverlayMaterial != null;

        if( !wanted || optic.Camera == null )
        {
          Detach();
          return;
        }

        EnsureBuffer( optic.Camera );
        ApplyMaterialValues();
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

    private static void EnsureBuffer( Camera opticCamera )
    {
      var thermal = CotiOpticThermalCamera.Output;

      if( _commandBuffer != null
          && _attachedTo == opticCamera
          && ReferenceEquals( _builtThermal, thermal ) )
      {
        return;
      }

      Detach();

      if( !EnsureMaterial() )
        return;

      _commandBuffer = new CommandBuffer { name = "COTI magnified overlay" };

      // CameraTarget on a camera rendering to a texture IS that texture, so this lands in
      // SSAAOpticCurrent without naming it. Additive blend, so the destination is only written to.
      _commandBuffer.Blit( thermal, BuiltinRenderTextureType.CameraTarget, _material );

      opticCamera.AddCommandBuffer( InjectionPoint, _commandBuffer );

      _attachedTo = opticCamera;
      _builtThermal = thermal;

      if( !_loggedAttached )
      {
        _loggedAttached = true;
        Plugin.Log.LogInfo(
            $"[COTI] Magnified overlay attached to {opticCamera.name} at {InjectionPoint} " +
            $"(thermal {thermal.width}x{thermal.height} into " +
            $"{( opticCamera.targetTexture == null ? "SCREEN" : opticCamera.targetTexture.name )})" );
      }
    }

    private static bool EnsureMaterial()
    {
      if( _material != null )
        return true;

      var shared = CotiShaderBundle.OverlayMaterial;
      if( shared == null )
        return false;

      // From the bundle material, never from the shader: a material built from a shader whose
      // programs were stripped renders nothing while reporting isSupported=true.
      _material = new Material( shared ) { name = "CotiMagnifiedOverlay" };
      return true;
    }

    private static void ApplyMaterialValues()
    {
      var host = CotiState.Host;

      _material.SetTexture( MainTexId, CotiOpticThermalCamera.Output );

      // No circle mask: inside the lens the whole picture is the sensor's view, and the lens is
      // already a circle the game draws. White passes the shader's multiply through.
      _material.SetTexture( MaskTexId, Texture2D.whiteTexture );

      // Same reason: the disc marks where the sensor points inside the tube image, and with the
      // mask open it would just lift the whole scope picture by a constant.
      _material.SetFloat( CircleGlowId, 0f );

      // No lens exclusion either - that only means anything on the main camera. Zero radius is the
      // shader's own "no lens" value.
      _material.SetVector( LensCircleId, Vector4.zero );

      _material.SetFloat( ThresholdId, Mathf.Clamp01( host.HeatThreshold ) );
      _material.SetFloat( OutlineMixId, Mathf.Clamp01( host.OutlineMix ) );
      _material.SetFloat( OutlineWidthId, CotiOverlayScale.OutlineWidth(
          Mathf.Max( 0.5f, host.OutlineWidth ),
          CotiOpticThermalCamera.Output == null ? 0 : CotiOpticThermalCamera.Output.height ) );

      // Phosphor and switching fade from the 1x path: the magnified image sits inside the same
      // tube, so a different tint would read as two instruments.
      _material.SetColor( HotColourId, CotiOverlayCompositor.HotColour );
      _material.SetColor( CoolColourId, CotiOverlayCompositor.CoolColour );
      _material.SetFloat(
          IntensityId, Mathf.Max( 0f, host.OverlayIntensity ) * CotiOverlayCompositor.PhosphorFade );
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
          // The optic camera is destroyed between raids and throws here. The buffer is dropped
          // regardless.
          Plugin.Log.LogWarning(
              $"[COTI] Removing magnified overlay command buffer failed: {ex.Message}" );
        }
      }

      _commandBuffer?.Release();
      _commandBuffer = null;
      _attachedTo = null;
      _builtThermal = null;
    }

    /// <summary>
    /// Drops the material as well as the buffer, for plugin shutdown. Detach runs on every weapon
    /// lower, where rebuilding a material would be waste.
    /// </summary>
    internal static void Teardown()
    {
      Detach();

      if( _material != null )
      {
        UnityEngine.Object.Destroy( _material );
        _material = null;
      }

      _loggedAttached = false;
      _broken = false;
    }
  }
}
