using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// What every thermal camera this mod builds has in common: which prefab to clone, what must come
  /// off it, and the one component it must never be missing.
  ///
  /// Shared rather than copied because a fix landing in one camera and not the other is hard to
  /// detect, since both would still render. Stateless: each camera owns
  /// its own clone, target and failure latch.
  /// </summary>
  internal static class CotiThermalRig
  {
    /// <summary>
    /// BSG's optic-camera prefab. Cloned rather than hand-built: ThermalVision carries serialized
    /// material and ramp-texture references, so AddComponent yields null guts.
    /// </summary>
    internal const string PrefabName = "BaseOpticCamera";

    /// <summary>
    /// Components stripped off the clone. Matched by name, so an upstream rename degrades to "not
    /// stripped" rather than "does not compile".
    ///
    /// Kept: ChromaticAberration and VolumetricLightRenderer, which ThermalVision.Awake
    /// dereferences without a null check.
    /// </summary>
    private static readonly string[] StripComponentNames =
    {
      "OpticComponentUpdater",
      "OpticRetrice",
      "NightVision",

      // SSAA resolves through its own targets, a route for this camera's output to reach the
      // backbuffer instead of ours.
      "SSAA",
      "PostProcessLayer",
      "PostProcessVolume",

      // Scope-lens cosmetics: a bright radial halo and rounded distortion on a heat image.
      "Fisheye",
      "CC_FastVignette",
      "UltimateBloom",
      "BloomOptimized",
      "Tonemapping",
      "Undithering",

      // Overrides cullingMask, fighting the value each camera copies from its own source.
      "OpticCullingMask",

      // Atmospheric scattering for the visible-light sky.
      "TOD_Scattering",
      "MBOIT_Scattering",

      // Lit-scene upkeep with nothing on a thermal target to manage.
      "AreaLightManager",
      "StreamingController",
      "CameraLodBiasController",
    };

    /// <summary>
    /// Draws the camera with Coti/HeatOnly, so only what is warmer than the air reaches the thermal image and everything
    /// else draws black. See that shader for the rule, and CotiThermalWorld for what it is fed. Forward, because the replacement has no deferred pass,
    /// and with ThermalVision off, since its passes post-process a G-buffer this camera no longer fills.
    ///
    /// Run every frame after the camera copies its settings from the one it follows, which writes the rendering
    /// path and clear flags back. With a bundle that predates the shader, ThermalVision renders as before.
    /// </summary>
    internal static void ApplyRenderMode( Camera camera, ThermalVision thermal, ref bool replacing )
    {
      var heatOnly = CotiShaderBundle.HeatOnly;
      if( heatOnly == null )
      {
        thermal.enabled = true;
        thermal.On = true;
        return;
      }

      thermal.enabled = false;

      if( !replacing )
      {
        camera.SetReplacementShader( heatOnly, "RenderType" );

        // ThermalVision's buffers and the volumetric pass it kept switched off: in a forward camera with no G-buffer
        // either can draw over the finished frame, and this camera needs neither.
        camera.RemoveAllCommandBuffers();
        var volumetric = camera.GetComponent<VolumetricLightRenderer>();
        if( volumetric != null )
          volumetric.enabled = false;

        // Kept on the clone for ThermalVision.Awake (see StripComponentNames), but it resamples the finished image,
        // alpha included, and alpha is each surface's distance: at a scope's zoom it pulled a body 25 m away down to
        // 8-16 m, so the perspective outline drew it several times too thick.
        if( camera.GetComponent( "ChromaticAberration" ) is Behaviour aberration )
          aberration.enabled = false;

        replacing = true;
      }

      // After the camera's own buffers are cleared on its first frame, so the terrain buffer it adds survives.
      CotiThermalWorld.Update( camera );

      if( camera.renderingPath != RenderingPath.Forward )
        camera.renderingPath = RenderingPath.Forward;
      if( camera.clearFlags != CameraClearFlags.SolidColor )
        camera.clearFlags = CameraClearFlags.SolidColor;
      if( camera.backgroundColor != Color.black )
        camera.backgroundColor = Color.black;
    }

    internal static GameObject LoadPrefab()
    {
      return Resources.Load<GameObject>( PrefabName );
    }

    /// <summary>
    /// Clones the prefab into an inactive, stripped, untagged object. Activation is the caller's
    /// job and must wait until a render target is proven bound.
    ///
    /// Deactivated first, before anything else, as BSG does in OpticCameraManager.Init: a live
    /// camera with no targetTexture renders to the backbuffer, which is the player's whole screen.
    /// </summary>
    internal static GameObject Clone( GameObject prefab, string name )
    {
      var go = Object.Instantiate( prefab );
      go.SetActive( false );
      go.name = name;

      // The clone inherits BSG's "OpticCamera" tag, and PiP-Disabler disables cameras by it.
      go.tag = "Untagged";

      Strip( go );
      return go;
    }

    private static void Strip( GameObject go )
    {
      var components = go.GetComponents<Component>();
      for( var i = 0; i < components.Length; i++ )
      {
        var component = components[i];
        if( component == null )
          continue;

        var name = component.GetType().Name;
        for( var j = 0; j < StripComponentNames.Length; j++ )
        {
          if( name != StripComponentNames[j] )
            continue;

          // DestroyImmediate: Destroy is deferred to end of frame, so a "stripped" component would
          // still be alive while the camera is configured and prewarm-rendered.
          Object.DestroyImmediate( component );
          break;
        }
      }
    }

    /// <summary>
    /// Prevents an unguarded NRE inside BSG's own OnPreCull on every rendered frame. The field is
    /// named differently on each build, so it comes from <see cref="EftCompat"/> by type.
    /// </summary>
    internal static void EnsureVolumetricLightRenderer( GameObject go, ThermalVision thermal )
    {
      var field = EftCompat.VolumetricLightRendererField();

      if( field.GetValue( thermal ) != null )
        return;

      var renderer = go.GetComponent<VolumetricLightRenderer>()
                     ?? go.AddComponent<VolumetricLightRenderer>();

      field.SetValue( thermal, renderer );

      Plugin.Log.LogWarning(
          $"[COTI] {go.name} had no VolumetricLightRenderer - added one, since " +
          "ThermalVision.OnPreCull dereferences it without a null check" );
    }

    internal static string DescribeComponents( GameObject go )
    {
      var components = go.GetComponents<Component>();
      var names = new string[components.Length];
      for( var i = 0; i < components.Length; i++ )
      {
        names[i] = components[i] == null ? "<null>" : components[i].GetType().Name;
      }
      return string.Join( ", ", names );
    }

    /// <summary>
    /// Copies the shared image tuning - see <see cref="CotiImageConfig"/> - onto a ThermalVision
    /// instance. Called for both cameras, so the magnified image cannot drift from the 1x one it
    /// sits inside.
    /// </summary>
    internal static void ApplyImageTuning( ThermalVision thermal, CotiImageConfig image )
    {
      if( image == null )
        return;

      thermal.IsPixelated = image.IsPixelated;
      thermal.IsNoisy = image.IsNoisy;
      thermal.IsMotionBlurred = image.IsMotionBlurred;

      // Dropout is BSG's artefact for a failing scope, not a characteristic of this device.
      thermal.IsGlitch = false;
      thermal.UnsharpRadiusBlur = image.UnsharpRadiusBlur;
      thermal.UnsharpBias = image.UnsharpBias;
    }

    /// <summary>
    /// The sensor's refresh, via ThermalVision's own frame hold - it captures at this rate and
    /// re-blits the held copy in between.
    ///
    /// Not a render cap: skipping renders means disabling the camera, and ThermalVision gates its
    /// Update on camera.enabled, so a disabled camera driven by hand produces an ordinary lit image.
    /// </summary>
    internal static void SetRefreshRate( ThermalVision thermal, int hz )
    {
      var stuck = thermal.StuckFpsUtilities;
      if( stuck == null )
        return;

      thermal.IsFpsStuck = hz > 0;
      stuck.MinFramerate = hz;
      stuck.MaxFramerate = hz;
    }
  }
}
