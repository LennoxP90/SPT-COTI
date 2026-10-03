using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Composites the magnified thermal into the optic camera's own render target, which the game
  /// draws onto the lens as weapon geometry.
  ///
  /// Writing into the target leaves the scope's position, size and angle to the game. The lens
  /// cannot be located on screen: the texture it displays is published with <c>Shader.SetGlobalTexture</c>, on no material and no property block.
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

    private static CommandBuffer _commandBuffer;
    private static Camera _attachedTo;
    private static RenderTexture _builtThermal;

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
    private static float _setIntensity = float.NaN;
    private static Color? _setHotColour;
    private static Color? _setCoolColour;

    private static bool _broken;
    private static bool _loggedAttached;

    /// <summary>
    /// Whether the composite is attached and drawing. The 1x overlay gates its lens hole on this
    /// rather than on the camera: the two fail independently, and this one latches until the
    /// setting is toggled, so gating on the camera can leave a dead circle with nothing behind it.
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

      // CameraTarget on a camera rendering to a texture is that texture, so this lands in
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

      // Built from the bundle material rather than the shader: a material built from a shader whose
      // programs were stripped renders nothing while reporting isSupported=true.
      _material = new Material( shared ) { name = "CotiMagnifiedOverlay" };
      CotiShaderBundle.KeepLoaded( _material );
      ForgetMaterialValues();
      return true;
    }

    /// <summary>
    /// Pushes the magnified overlay's inputs onto its own material, writing only what changed.
    ///
    /// Same reasoning as the 1x compositor: these are native setters, and only the intensity moves
    /// frame to frame.
    /// </summary>
    private static void ApplyMaterialValues()
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
      SetFloatIfChanged( OutlineWidthId, CotiOverlayScale.OutlineWidth(
          Mathf.Max( 0.5f, image.OutlineWidth ),
          CotiOpticThermalCamera.Output == null ? 0 : CotiOpticThermalCamera.Output.height ),
          ref _setOutlineWidth );

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
      _setIntensity = float.NaN;
      _setHotColour = null;
      _setCoolColour = null;
    }

    /// <summary>
    /// Renders the magnified overlay alone, so its contribution can be told apart from the scope
    /// picture it is added to. The optic target carries both, and a blown-out target says nothing
    /// about which of the two blew out.
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

      Graphics.Blit( CotiOpticThermalCamera.Output, target, _material );
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
