using System;
using System.IO;
using Coti.Client.Dev;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// The COTI's own off-screen thermal camera.
  ///
  /// ThermalVision is a render-mode switch, not an image effect: it raises a GLOBAL shader value in
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
    // A 500m far clip on this camera was tried as a way to cut culling and submit cost, on the
    // theory that a 640x480 sensor at 1x resolves nothing past a few hundred metres. The only
    // test raid was Factory, where nothing is beyond 500m, so no effect - good or bad - could
    // actually be measured, and shipping it unmeasured risks silently hiding real heat sources
    // past that range. Only bring it back if a large map such as Woods or Lighthouse shows it
    // actually helps.

    private static GameObject _go;
    private static Camera _cam;
    private static ThermalVision _tv;
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

    /// <summary>
    /// BSG's own mask for a scope camera, before Configure replaces it.
    /// </summary>
    private static int _prefabCullingMask;

#if COTI_DEV
    // Frame-dump state; see DumpIfRequested. The countdown tracks the config value so a live edit to
    // Dump Frames triggers a fresh batch rather than dumping forever.
    private static CotiFrameDump.Countdown _dumps;
#endif

    internal static RenderTexture Output => _rt;

    /// <summary>
    /// Whether a ThermalVision is our camera's own. Patches of SetMaterialProperties must ask, since
    /// it runs for every instance in the game.
    /// </summary>
    internal static bool Owns( ThermalVision candidate )
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

      // Idle FIRST - see the same guard on CotiOpticThermalCamera. MarkBroken is reachable after
      // the object has been activated, and a bare return leaves it rendering a scene pass that
      // HasOutput then refuses to let anyone read.
      if( _broken )
      {
        Idle();
        return;
      }

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

      var prefab = CotiThermalRig.LoadPrefab();
      if( prefab == null )
      {
        MarkBroken( $"Resources.Load<GameObject>(\"{CotiThermalRig.PrefabName}\") returned null", null );
        return false;
      }

      // Comes back INACTIVE and stripped. Everything below configures a dead object; it is only
      // activated once a render target is proven bound. See ActivateIfReady.
      _go = CotiThermalRig.Clone( prefab, "CotiThermalCamera" );

      _cam = _go.GetComponent<Camera>();
      _tv = _go.GetComponent<ThermalVision>();

      if( _cam == null || _tv == null )
      {
        // Teardown BEFORE MarkBroken: Teardown clears the latch, so marking first leaves it false
        // and retries this whole load-strip-destroy cycle every frame.
        Teardown();
        MarkBroken( $"\"{CotiThermalRig.PrefabName}\" clone has camera={_cam != null} thermalVision={_tv != null}", null );
        return false;
      }

      _prefabCullingMask = _cam.cullingMask;

      CotiThermalRig.EnsureVolumetricLightRenderer( _go, _tv );

      // ThermalVision gates its own Update on _camera.enabled, so this must stay true: a disabled
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
            $"[COTI] Thermal camera created from \"{CotiThermalRig.PrefabName}\" prefab (inactive); " +
            $"components remaining: {CotiThermalRig.DescribeComponents( _go )}" );
      }

      return true;
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

        // Every goggle toggle reaches here, so this is gated. Ungated it filled a normal play
        // session's log with render-target diagnostics nobody had asked for, which is how a log
        // stops being worth reading when something does go wrong.
        if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        {
          Plugin.Log.LogInfo(
              $"[COTI] Thermal camera activated, rendering into {_rt.name} " +
              $"{_rt.width}x{_rt.height}, requestedPath={_cam.renderingPath} " +
              $"actualPath={_cam.actualRenderingPath}, " +
              $"thermalOn={_tv.On}, tvEnabled={_tv.enabled}" );
        }
      }

      return true;
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

      // Tried RenderingPath.Forward here once, on the theory that a heat map thrown away after
      // post-processing didn't need deferred's G-buffer and lighting passes. Measured in raid: it
      // does apply (requestedPath=Forward actualPath=Forward), but it broke the thermal image.
      // EFT's ThermalVision derives its output from rendering rather than real temperature, and it
      // reads G-buffer data that only exists in deferred - in forward a warm object like a fire
      // barrel renders cold, and only emissive sources such as the flames still register. Reverted
      // to copying main's path.
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

      CotiThermalRig.SetRefreshRate( _tv, cfg.Hz );

      CotiThermalRig.ApplyImageTuning( _tv, Plugin.Config.Image );
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
    /// Readback, statistics and encoding come from <see cref="CotiFrameDump"/>, so this dump and the
    /// magnified camera's report in exactly the same format and can be read side by side.
    /// </summary>
    private static void DumpIfRequested( CotiCameraConfig cfg )
    {
      int index;
      if( !_dumps.Take( cfg.DumpFrames, out index ) )
        return;

      var summary = CotiFrameDump.Dump( _rt, "thermal", index );
      if( summary == null )
      {
        _dumps.Stop();
        return;
      }

      // The command-buffer counts are the diagnostic that would have found a disabled ThermalVision
      // immediately. It attaches one buffer to each of BeforeForwardAlpha and AfterForwardAlpha in
      // its Awake and fills them from OnPreCull, a message only an ENABLED component receives.
      // Counts of 1/1 mean the chain is wired; 0/0 means Awake never ran on this camera.
      var beforeAlpha = _cam.GetCommandBuffers( CameraEvent.BeforeForwardAlpha ).Length;
      var afterAlpha = _cam.GetCommandBuffers( CameraEvent.AfterForwardAlpha ).Length;

      DumpOverlay( CotiFrameDump.Directory, index );

      Plugin.Log.LogInfo(
          $"[COTI] dump -> {summary} " +
          $"renderingPath={_cam.actualRenderingPath} " +
          $"camEnabled={_cam.enabled} tvEnabled={_tv.enabled} tvOn={_tv.On} " +
          $"cbBeforeAlpha={beforeAlpha} cbAfterAlpha={afterAlpha}" );
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
            $"material[{CotiOverlayCompositor.DescribeMaterial()}]" );
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
