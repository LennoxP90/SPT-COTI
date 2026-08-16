using System;
using System.IO;
using System.Reflection;
using Coti.Client.Dev;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// The COTI's own off-screen thermal camera.
  ///
  /// ThermalVisionItemClass is a render-mode switch, not an image effect: it raises a GLOBAL shader value in
  /// OnPreCull and lowers it in OnPostRender, so it thermalises whichever camera it sits on, for the
  /// whole render span. On Camera.main that span is the player's view, so the effect cannot be
  /// masked to a circle - the first implementation turned the entire screen thermal.
  ///
  /// A second camera rendering to a RenderTexture is EFT's own answer, used by its thermal scopes.
  /// Once thermal is a texture it is an ordinary post-process input - see
  /// <see cref="CotiOverlayCompositor"/>. The main camera is never touched here.
  /// </summary>
  internal static class CotiThermalCamera
  {
    /// <summary>
    /// BSG's optic-camera prefab. Instantiated rather than hand-built: ThermalVisionItemClass carries
    /// serialized material and ramp-texture references, so a bare AddComponent yields a component
    /// with null guts that cannot render.
    /// </summary>
    private const string PrefabName = "BaseOpticCamera";

    /// <summary>
    /// Components stripped off the clone. Matched by NAME, so an upstream rename degrades to "not
    /// stripped" rather than "does not compile".
    /// </summary>
    private static readonly string[] StripComponentNames =
    {
      "OpticComponentUpdater",
      "OpticRetrice",
      "NightVision",

      // SSAA resolves through its own render targets, a plausible route for this camera's
      // output to reach the BACKBUFFER instead of our texture - which is what once put a
      // full-screen thermal view on the player's monitor.
      "SSAA",
      "PostProcessLayer",
      "PostProcessVolume",

      // Scope-lens cosmetics, visible in the first working thermal dump as a bright radial
      // halo and rounded distortion. NOT stripped: ChromaticAberration and
      // VolumetricLightRenderer, which ThermalVisionItemClass.Awake dereferences without a null check.
      "Fisheye",
      "CC_FastVignette",
      "UltimateBloom",
      "BloomOptimized",
      "Tonemapping",
      "Undithering",

      // Overrides cullingMask for scope rendering, which fights the value Configure copies from
      // the main camera.
      "OpticCullingMask",
    };

    /// <summary>
    /// ThermalVisionItemClass.OnPreCull dereferences this without a null guard, so a clone missing it
    /// throws once per frame. Private and cached during Awake, hence reflection.
    /// </summary>
    private static readonly FieldInfo VolumetricLightRendererField =
        AccessTools.Field( typeof( ThermalVisionItemClass ), "_volumetricLightRenderer" );

    private static GameObject _go;
    private static Camera _cam;
    private static ThermalVisionItemClass _tv;
    private static RenderTexture _rt;
    private static Transform _followed;

    private static int _rtWidth;
    private static int _rtHeight;

    /// <summary>
    /// Latches on failure so the camera stays down and logs once, rather than throwing every
    /// frame - a per-frame NRE elsewhere in this game leaked 72GB. Cleared by Teardown.
    /// </summary>
    private static bool _broken;

    private static bool _loggedCreated;

    /// <summary>BSG's own mask for a scope camera, before Configure replaces it.</summary>
    private static int _prefabCullingMask;

#if COTI_DEV
    // Frame-dump state; see DumpIfRequested. _lastDumpRequest tracks the config value so a live
    // edit to dumpFrames triggers a fresh batch rather than dumping forever.
    private static int _dumpsRemaining;
    private static int _lastDumpRequest;
    private static int _dumpIndex;
#endif

    internal static RenderTexture Output => _rt;

    /// <summary>
    /// Whether a ThermalVisionItemClass is our camera's own. Patches of SetMaterialProperties must ask, since
    /// it runs for every instance in the game.
    /// </summary>
    internal static bool Owns( ThermalVisionItemClass candidate )
    {
      return candidate != null && ReferenceEquals( candidate, _tv );
    }

    internal static bool HasOutput => !_broken && _cam != null && _rt != null;

    internal static bool ModeEnabled
    {
      get
      {
        var cfg = Plugin.Config?.ThermalCamera;
        return cfg != null && cfg.Enabled;
      }
    }

    internal static void Tick()
    {
      var cfg = Plugin.Config?.ThermalCamera;

      if( cfg == null || !cfg.Enabled )
      {
        Teardown();
        return;
      }

      if( _broken )
        return;

      try
      {
        // Idled, not destroyed: rebuilding it on every NVG toggle costs far more than leaving it
        // asleep, but leaving it awake draws a whole extra scene pass nothing reads.
        if( !CotiState.Active )
        {
          Idle();
          return;
        }

        var main = Camera.main;
        if( main == null )
          return; // not in raid yet - retry next frame

        if( !EnsureCamera() )
          return;

        Configure( main, cfg );

        // Gate: no activation and no render until a target is proven bound.
        if( !ActivateIfReady() )
          return;

#if COTI_DEV
        DumpIfRequested( cfg );
#endif
      }
      catch( Exception ex )
      {
        MarkBroken( "per-frame update", ex );
      }
    }

    private static bool EnsureCamera()
    {
      if( _go != null && _cam != null && _tv != null )
        return true;

      var prefab = Resources.Load<GameObject>( PrefabName );
      if( prefab == null )
      {
        MarkBroken( $"Resources.Load<GameObject>(\"{PrefabName}\") returned null", null );
        return false;
      }

      _go = UnityEngine.Object.Instantiate( prefab );

      // DEACTIVATE FIRST, before anything else. BSG does exactly this at
      // OpticCameraManager.Init():174 and the reason is now known the hard way: a live camera
      // with no targetTexture renders to the BACKBUFFER, which put a full-screen thermal image
      // on the player's monitor - recognisable because it had no night-vision tube mask (this
      // clone has NightVision stripped) and refreshed at the camera's Hz rather than the game's
      // framerate. Everything below configures an inactive object; it is only activated once a
      // render target is proven bound. See ActivateIfReady.
      _go.SetActive( false );

      _go.name = "CotiThermalCamera";

      // BSG tags its optic camera "OpticCamera" and the clone inherits that tag. PiP-Disabler
      // finds and disables cameras by that tag to kill picture-in-picture rendering, which
      // would silently switch our camera off on any install running it.
      _go.tag = "Untagged";

      StripScopeComponents( _go );

      _cam = _go.GetComponent<Camera>();
      _tv = _go.GetComponent<ThermalVisionItemClass>();

      if( _cam == null || _tv == null )
      {
        // Teardown BEFORE MarkBroken: Teardown clears the latch, so marking first leaves it false
        // and retries this whole load-strip-destroy cycle every frame.
        Teardown();
        MarkBroken( $"\"{PrefabName}\" clone has camera={_cam != null} thermalVision={_tv != null}", null );
        return false;
      }

      _prefabCullingMask = _cam.cullingMask;

      EnsureVolumetricLightRenderer();

      // ThermalVisionItemClass gates its own Update on _camera.enabled, so this must stay true: a disabled
      // camera still renders when driven by hand, but silently produces an ordinary lit image.
      _cam.enabled = true;
      _tv.On = true;

      // NO prewarm render here, and no SetActive(true). Both were here before and the prewarm
      // was the clearest instance of the backbuffer defect: it ran before Configure had ever
      // assigned targetTexture, so it rendered a full-screen thermal frame by construction.
      // Activation and prewarm now happen in ActivateIfReady, after a target is bound.
      if( !_loggedCreated )
      {
        _loggedCreated = true;
        Plugin.Log.LogInfo(
            $"[COTI] Thermal camera created from \"{PrefabName}\" prefab (inactive); " +
            $"components remaining: {DescribeComponents( _go )}" );
      }

      return true;
    }

    private static string DescribeComponents( GameObject go )
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
    /// Activates the camera only once its render target is proven bound. An unbound target means
    /// render-to-backbuffer, i.e. the player's whole screen replaced by a thermal view - so the
    /// camera must never be renderable in that state. False leaves it inactive and harmless.
    /// </summary>
    private static void Idle()
    {
      if( _go != null && _go.activeSelf )
        _go.SetActive( false );
    }

    private static bool ActivateIfReady()
    {
      if( _rt == null || _cam.targetTexture != _rt )
      {
        MarkBroken(
            $"render target not bound (rt={( _rt == null ? "null" : _rt.name )}, " +
            $"camera.targetTexture={( _cam.targetTexture == null ? "null" : _cam.targetTexture.name )}) - " +
            "refusing to activate, since a camera with no target renders to the screen",
            null );
        return false;
      }

      if( !_go.activeSelf )
      {
        _go.SetActive( true );

        // BSG's own IE_PreWarm renders one frame immediately then deactivates, to move the
        // first-use cost off the frame where the player raises the device. Same idea, but
        // strictly after the target is bound.
        try
        {
          _cam.Render();
        }
        catch( Exception ex )
        {
          MarkBroken( "prewarm render", ex );
          return false;
        }

        Plugin.Log.LogInfo(
            $"[COTI] Thermal camera activated, rendering into {_rt.name} " +
            $"{_rt.width}x{_rt.height}, requestedPath={_cam.renderingPath} " +
            $"actualPath={_cam.actualRenderingPath}, thermalOn={_tv.On}, " +
            $"tvEnabled={_tv.enabled}" );
      }

      return true;
    }

    private static void StripScopeComponents( GameObject go )
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

          UnityEngine.Object.DestroyImmediate( component );
          break;
        }
      }
    }

    /// <summary>
    /// The sensor's refresh, via ThermalVisionItemClass's own frame hold: it captures the target at this rate
    /// and re-blits the held copy in between, which is what a low-refresh core looks like.
    ///
    /// Not a render cap. Skipping renders means disabling the camera, and ThermalVisionItemClass gates its
    /// Update on camera.enabled - a disabled camera driven by manual Render() calls produces an
    /// ordinary lit image with no thermal at all.
    /// </summary>
    private static void SetRefreshRate( CotiCameraConfig cfg )
    {
      var stuck = _tv.StuckFpsUtilities;
      if( stuck == null )
        return;

      _tv.IsFpsStuck = cfg.Hz > 0;
      stuck.MinFramerate = cfg.Hz;
      stuck.MaxFramerate = cfg.Hz;
    }

    /// <summary>See <see cref="VolumetricLightRendererField"/>: prevents an unguarded NRE inside
    /// BSG's own OnPreCull on every rendered frame.</summary>
    private static void EnsureVolumetricLightRenderer()
    {
      if( VolumetricLightRendererField == null )
        return;

      if( VolumetricLightRendererField.GetValue( _tv ) != null )
        return;

      var renderer = _go.GetComponent<VolumetricLightRenderer>()
                     ?? _go.AddComponent<VolumetricLightRenderer>();

      VolumetricLightRendererField.SetValue( _tv, renderer );

      Plugin.Log.LogWarning(
          "[COTI] Thermal camera prefab had no VolumetricLightRenderer - added one, since " +
          "ThermalVisionItemClass.OnPreCull dereferences it without a null check" );
    }

    /// <summary>
    /// Locks the thermal camera to the player's eye and matches the main camera's projection.
    ///
    /// Matching is load-bearing for alignment: the thermal target is blitted over the screen 1:1, so
    /// a narrower frustum reads as a zoomed, misaligned overlay. The device is 1x, so matching is
    /// also the faithful choice.
    /// </summary>
    private static void Configure( Camera main, CotiCameraConfig cfg )
    {
      if( _followed != main.transform )
      {
        _go.transform.SetParent( main.transform, worldPositionStays: false );
        _followed = main.transform;
      }

      _go.transform.localPosition = Vector3.zero;
      _go.transform.localRotation = Quaternion.identity;
      _go.transform.localScale = Vector3.one;

      _cam.renderingPath = main.renderingPath;

      CotiDevTools.ReportCullingMasks( _prefabCullingMask, main.cullingMask );

      // Intersected with BSG's own scope mask, which omits Weapon Preview, Menu Environment and
      // three unused layers. A zero prefab mask would render nothing, so it defers to the player's.
      _cam.cullingMask = _prefabCullingMask == 0
          ? main.cullingMask
          : main.cullingMask & _prefabCullingMask;
      _cam.clearFlags = main.clearFlags;
      _cam.backgroundColor = main.backgroundColor;
      _cam.nearClipPlane = main.nearClipPlane;
      _cam.farClipPlane = main.farClipPlane;
      _cam.allowHDR = main.allowHDR;
      _cam.useOcclusionCulling = main.useOcclusionCulling;

      // Render BEFORE the main camera. Unity orders cameras by depth, and the compositor's
      // buffer runs on the main camera's
      // AfterEverything - so a higher depth here would composite the PREVIOUS frame's heat.
      // Irrelevant while parked, visible as lag the moment the player turns.
      _cam.depth = main.depth - 1f;

      EnsureRenderTexture( cfg );

      // aspect AFTER targetTexture: assigning a target texture recomputes aspect from that
      // texture's dimensions, which would undo this and squash the image.
      _cam.fieldOfView = main.fieldOfView;
      _cam.aspect = main.aspect;

      _tv.enabled = true;
      _tv.On = true;

      SetRefreshRate( cfg );

      var host = CotiState.Host;
      if( host != null )
      {
        _tv.IsPixelated = host.IsPixelated;
        _tv.IsNoisy = host.IsNoisy;
        _tv.IsMotionBlurred = host.IsMotionBlurred;

        // Dropout is BSG's artefact for a failing scope, not a characteristic of this device.
        _tv.IsGlitch = false;
        _tv.UnsharpRadiusBlur = host.UnsharpRadiusBlur;
        _tv.UnsharpBias = host.UnsharpBias;
      }
    }

    /// <summary>
    /// Allocates the render target, reallocating only when the configured size actually changes -
    /// so width/height are live-tunable without leaking a texture per poll.
    ///
    /// Note these dimensions are the FULL-SCREEN render, not the circle: the circle receives
    /// only the fraction of them its radius covers (at maskRadius 0.274 that is about 55% of the
    /// height), so if the overlay looks too blocky the fix is to raise these.
    /// </summary>
    private static void EnsureRenderTexture( CotiCameraConfig cfg )
    {
      var width = Mathf.Clamp( cfg.Width, 16, 4096 );
      var height = Mathf.Clamp( cfg.Height, 16, 4096 );

      if( _rt != null && _rtWidth == width && _rtHeight == height )
      {
        if( _cam.targetTexture != _rt )
          _cam.targetTexture = _rt;
        return;
      }

      ReleaseRenderTexture();

      var format = _cam.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;

      _rt = new RenderTexture( width, height, 24, format, RenderTextureReadWrite.Default )
      {
        name = "CotiThermalRT",
        autoGenerateMips = false,
        useMipMap = false,
        filterMode = FilterMode.Bilinear,
      };
      _rt.Create();

      _rtWidth = width;
      _rtHeight = height;
      _cam.targetTexture = _rt;

      Plugin.Log.LogInfo( $"[COTI] Thermal camera target {width}x{height} ({format})" );
    }

#if COTI_DEV
    /// <summary>
    /// Writes the thermal camera's own render texture to a PNG and logs per-channel statistics.
    /// </summary>
    private static void DumpIfRequested( CotiCameraConfig cfg )
    {
      if( cfg.DumpFrames != _lastDumpRequest )
      {
        _dumpsRemaining = cfg.DumpFrames;
        _lastDumpRequest = cfg.DumpFrames;
        _dumpIndex = 0;
      }

      if( _dumpsRemaining <= 0 )
        return;
      _dumpsRemaining--;

      var readback = (Texture2D)null;
      var previous = RenderTexture.active;

      try
      {
        readback = new Texture2D( _rt.width, _rt.height, TextureFormat.RGBA32, false );

        RenderTexture.active = _rt;
        readback.ReadPixels( new Rect( 0f, 0f, _rt.width, _rt.height ), 0, 0 );
        readback.Apply( false, false );
        RenderTexture.active = previous;

        var pixels = readback.GetPixels32();
        double sumR = 0, sumG = 0, sumB = 0;
        int maxR = 0, maxG = 0, maxB = 0, nonBlack = 0;

        for( var i = 0; i < pixels.Length; i++ )
        {
          var p = pixels[i];
          sumR += p.r;
          sumG += p.g;
          sumB += p.b;
          if( p.r > maxR )
            maxR = p.r;
          if( p.g > maxG )
            maxG = p.g;
          if( p.b > maxB )
            maxB = p.b;
          if( p.r > 8 || p.g > 8 || p.b > 8 )
            nonBlack++;
        }

        var count = pixels.Length;
        var coverage = 100.0 * nonBlack / count;

        var directory = Path.Combine( BepInEx.Paths.GameRootPath, "coti-dumps" );
        Directory.CreateDirectory( directory );
        var path = Path.Combine( directory, $"thermal-{_dumpIndex:d3}.png" );
        _dumpIndex++;

        File.WriteAllBytes( path, readback.EncodeToPNG() );

        // The command-buffer counts are the diagnostic that would have found the disabled
        // component immediately. ThermalVisionItemClass attaches one buffer to each of
        // BeforeForwardAlpha and AfterForwardAlpha in its Awake, and fills them from
        // OnPreCull - a message only an ENABLED component receives. Counts of 1/1 mean the
        // chain is wired; 0/0 means Awake never ran on this camera.
        var beforeAlpha = _cam.GetCommandBuffers( CameraEvent.BeforeForwardAlpha ).Length;
        var afterAlpha = _cam.GetCommandBuffers( CameraEvent.AfterForwardAlpha ).Length;

        DumpOverlay( directory, _dumpIndex - 1 );

        Plugin.Log.LogInfo(
            $"[COTI] dump -> {path} ({_rt.width}x{_rt.height}) " +
            $"mean R={sumR / count:F1} G={sumG / count:F1} B={sumB / count:F1} " +
            $"max R={maxR} G={maxG} B={maxB} nonBlack={coverage:F1}% " +
            $"renderingPath={_cam.actualRenderingPath} " +
            $"camEnabled={_cam.enabled} tvEnabled={_tv.enabled} tvOn={_tv.On} " +
            $"cbBeforeAlpha={beforeAlpha} cbAfterAlpha={afterAlpha}" );
      }
      catch( Exception ex )
      {
        RenderTexture.active = previous;
        _dumpsRemaining = 0;
        Plugin.Log.LogError( $"[COTI] Thermal frame dump failed: {ex}" );
      }
      finally
      {
        if( readback != null )
          UnityEngine.Object.Destroy( readback );
      }
    }
#endif

#if COTI_DEV
    /// <summary>
    /// Writes what the OVERLAY SHADER produces, alongside the raw thermal dump. The pair is the
    /// diagnostic: raw thermal with content but overlay all black means the shader is the problem,
    /// and both having content means the shader works and the composite is not reaching the screen.
    /// </summary>
    private static void DumpOverlay( string directory, int index )
    {
      RenderTexture rendered = null;
      Texture2D readback = null;
      var previous = RenderTexture.active;

      try
      {
        rendered = CotiOverlayCompositor.RenderOverlayForDiagnostics( _rt.width, _rt.height );
        if( rendered == null )
        {
          Plugin.Log.LogWarning( "[COTI] overlay dump skipped - no material or no thermal output" );
          return;
        }

        readback = new Texture2D( rendered.width, rendered.height, TextureFormat.RGBA32, false );
        RenderTexture.active = rendered;
        readback.ReadPixels( new Rect( 0f, 0f, rendered.width, rendered.height ), 0, 0 );
        readback.Apply( false, false );
        RenderTexture.active = previous;

        var pixels = readback.GetPixels32();
        double sum = 0;
        int max = 0, nonBlack = 0;

        for( var i = 0; i < pixels.Length; i++ )
        {
          var value = Mathf.Max( pixels[i].r, Mathf.Max( pixels[i].g, pixels[i].b ) );
          sum += value;
          if( value > max )
            max = value;
          if( value > 8 )
            nonBlack++;
        }

        var path = Path.Combine( directory, $"overlay-{index:d3}.png" );
        File.WriteAllBytes( path, readback.EncodeToPNG() );

        Plugin.Log.LogInfo(
            $"[COTI] overlay -> {path} mean={sum / pixels.Length:F1} max={max} " +
            $"nonBlack={100.0 * nonBlack / pixels.Length:F1}% " +
            $"(threshold={CotiState.Host?.HeatThreshold:F2} " +
            $"intensity={CotiState.Host?.OverlayIntensity:F2} " +
            $"outlineMix={CotiState.Host?.OutlineMix:F2})" );
      }
      catch( Exception ex )
      {
        RenderTexture.active = previous;
        Plugin.Log.LogError( $"[COTI] Overlay dump failed: {ex.Message}" );
      }
      finally
      {
        if( readback != null )
          UnityEngine.Object.Destroy( readback );
        if( rendered != null )
        {
          rendered.Release();
          UnityEngine.Object.Destroy( rendered );
        }
      }
    }
#endif



    private static void MarkBroken( string what, Exception ex )
    {
      if( _broken )
        return;
      _broken = true;

      var detail = ex == null ? string.Empty : $": {ex}";
      Plugin.Log.LogError(
          $"[COTI] Thermal camera disabled for this session - {what} failed{detail}" );
    }

    /// <summary>
    /// Destroys the camera and releases its target. Called when the feature is switched off, and
    /// from Plugin.OnDestroy. Also clears <see cref="_broken"/>, so toggling the config off and
    /// on is a genuine retry.
    /// </summary>
    internal static void Teardown()
    {
      if( _go != null )
      {
        UnityEngine.Object.Destroy( _go );
      }

      _go = null;
      _cam = null;
      _tv = null;
      _followed = null;
      _broken = false;
      _loggedCreated = false;

      ReleaseRenderTexture();
    }

    private static void ReleaseRenderTexture()
    {
      if( _rt == null )
        return;

      _rt.Release();
      UnityEngine.Object.Destroy( _rt );
      _rt = null;
      _rtWidth = 0;
      _rtHeight = 0;
    }
  }
}
