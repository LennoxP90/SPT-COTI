using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// What every thermal camera this mod builds has in common: which prefab to clone, what must come
  /// off it, and the one component it must never be missing.
  ///
  /// Shared rather than copied because a fix landing in one camera and not the other is the failure
  /// this codebase is least able to detect - both would still render. Stateless: each camera owns
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
    /// Components stripped off the clone. Matched by NAME, so an upstream rename degrades to "not
    /// stripped" rather than "does not compile".
    ///
    /// NOT stripped: ChromaticAberration and VolumetricLightRenderer, which ThermalVision.Awake
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

    internal static GameObject LoadPrefab()
    {
      return Resources.Load<GameObject>( PrefabName );
    }

    /// <summary>
    /// Clones the prefab into an INACTIVE, stripped, untagged object. Activation is the caller's
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
    /// Copies the ThermalVision look from the host's config, shared so the magnified image cannot
    /// drift from the 1x one it sits inside.
    /// </summary>
    internal static void ApplyHostLook( ThermalVision thermal, CotiNvgHostConfig host )
    {
      if( host == null )
        return;

      thermal.IsPixelated = host.IsPixelated;
      thermal.IsNoisy = host.IsNoisy;
      thermal.IsMotionBlurred = host.IsMotionBlurred;

      // Dropout is BSG's artefact for a failing scope, not a characteristic of this device.
      thermal.IsGlitch = false;
      thermal.UnsharpRadiusBlur = host.UnsharpRadiusBlur;
      thermal.UnsharpBias = host.UnsharpBias;
    }

    /// <summary>
    /// The sensor's refresh, via ThermalVision's own frame hold - it captures at this rate and
    /// re-blits the held copy in between.
    ///
    /// NOT a render cap: skipping renders means disabling the camera, and ThermalVision gates its
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
