using Coti.Shared;
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
  /// ThermalVision is a render-mode switch rather than an image effect: it raises a global shader
  /// value in OnPreCull and lowers it in OnPostRender, so it thermalises whichever camera it sits on
  /// for the whole render span. On Camera.main that would turn the entire screen thermal and could
  /// not be masked to a circle.
  ///
  /// A second camera rendering to a RenderTexture is EFT's own approach, used by its thermal scopes.
  /// Once thermal is a texture it is an ordinary post-process input - see
  /// <see cref="CotiOverlayCompositor"/>. The main camera is never touched here.
  /// </summary>
  internal static class CotiThermalCamera
  {
    // The far clip matches the main camera. A shorter clip would cut culling cost but could hide
    // real heat sources at range on large maps.

    private static GameObject _go;
    private static Camera _cam;
    private static readonly CotiSensorPacer Pacer = new CotiSensorPacer();

    /// <summary>Whether the replacement shader is set on this clone. Cleared with the camera in Teardown.</summary>
    private static bool _replacing;
    private static ThermalVision _tv;
    private static RenderTexture _rt;
    private static Transform _followed;

    private static int _rtWidth;
    private static int _rtHeight;

    // What Configure last wrote onto _cam and _tv. Cleared with the camera in Teardown - a fresh
    // clone carries the prefab's values, and a cache that outlived it would leave them unwritten.
    private static bool _mirrored;
    private static RenderingPath _mirroredPath;
    private static int _mirroredCullingMask;
    private static CameraClearFlags _mirroredClearFlags;
    private static Color _mirroredBackground;
    private static float _mirroredNearClip;
    private static float _mirroredFarClip;
    private static bool _mirroredHdr;
    private static bool _mirroredOcclusion;
    private static float _mirroredDepth;
    private static float _mirroredFov = float.NaN;
    private static readonly CotiCullMirror Cull = new CotiCullMirror();
    private static float _mirroredAspect = float.NaN;

    // What the cropped projection was last built from. Cleared with the camera in Teardown.
    private static float _projectedFov = float.NaN;
    private static float _projectedAspect = float.NaN;
    private static CotiCropBox _projectedBox;

    private static bool _tuned;
    private static int _tunedHz;
    private static bool _tunedPixelated;
    private static bool _tunedNoisy;
    private static bool _tunedMotionBlurred;
    private static float _tunedUnsharpRadius;
    private static float _tunedUnsharpBias;

    /// <summary>
    /// Latches on failure so the camera stays down and logs once, rather than throwing every
    /// frame, since a per-frame exception leaks memory quickly. Cleared by Teardown.
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
    /// Where <see cref="Output"/> sits on screen: the box around the COTI's circle that this camera renders, in the
    /// mask's UV. Its rows over the box's height give the full frame's, for anything sized per screen.
    /// </summary>
    internal static CotiCropBox Box { get; private set; } = CotiCropBox.Whole;

    /// <summary>The vertical field of view this camera renders with, for projecting the outline.</summary>
    internal static float FieldOfView => _cam != null ? _cam.fieldOfView : 0f;

    /// <summary>
    /// Whether a ThermalVision belongs to this camera. Patches of SetMaterialProperties must ask, since
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

      // Idle before returning, as CotiOpticThermalCamera does. MarkBroken is reachable after the
      // object has been activated, and a bare return would leave it rendering a scene pass that
      // HasOutput refuses to let anyone read.
      if( _broken )
      {
        Idle();
        return;
      }

      try
      {
        // Idled rather than destroyed: rebuilding it on every NVG toggle costs far more than
        // leaving it asleep, and leaving it awake draws an extra scene pass nothing reads.
        // While the magnified composite draws, the 1x overlay stands down (CotiOverlayCompositor.Sync), so this
        // camera's picture would go unread: about 2.5 ms of GPU per scoped frame, measured on Customs.
        if( !CotiState.Active || CotiOpticCamera.ThermalSightAimed
            || ( CotiOpticThermalCamera.Magnifying && CotiOpticOverlayCompositor.Attached ) )
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

        // At the sensor's refresh rate rather than every frame, as the real device runs; the last picture stands
        // between, and the skipped frames save the scene pass. CotiCameraConfig.Hz, 60 by default; 0 is every frame.
        _cam.enabled = Pacer.Due( Time.realtimeSinceStartupAsDouble, cfg.Hz );

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

      // Comes back inactive and stripped. Everything below configures an inactive object; it is
      // activated only once a render target is bound. See ActivateIfReady.
      _go = CotiThermalRig.Clone( prefab, "CotiThermalCamera" );

      _cam = _go.GetComponent<Camera>();
      _tv = _go.GetComponent<ThermalVision>();

      if( _cam == null || _tv == null )
      {
        // Teardown before MarkBroken: Teardown clears the latch, so marking first would leave it
        // false and retry this load-strip-destroy cycle every frame.
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

      // No prewarm render and no SetActive(true) here: before Configure assigns targetTexture, a
      // render would go to the backbuffer as a full-screen thermal frame. Activation and prewarm
      // happen in ActivateIfReady, after a target is bound.
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
    /// render-to-backbuffer, which replaces the player's whole screen with a thermal view, so the
    /// camera is never renderable in that state. False leaves it inactive.
    /// </summary>
    private static void Idle()
    {
      Pacer.Reset();
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
        // first-use cost off the frame where the player raises the device. This does the same,
        // after the target is bound.
        try
        {
          _cam.Render();
        }
        catch( Exception ex )
        {
          MarkBroken( "prewarm render", ex );
          return false;
        }

        // Every goggle toggle reaches here, so this is gated behind verbose logging to keep the
        // log readable.
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
    /// Matching is required for alignment: the thermal target is blitted over the screen 1:1, so a
    /// narrower frustum reads as a zoomed, misaligned overlay. The device is 1x, so matching is also
    /// faithful to it.
    /// </summary>
    private static void Configure( Camera main, CotiCameraConfig cfg )
    {
      Follow( main );
      MirrorSettings( main );

      // Only the circle is ever shown, so only a box around it is rendered: a sixth of the pixels for the GPNVG, and a
      // narrower view that culls everything outside it.
      var host = CotiState.Host;
      CotiCropBox box;
      if( host == null
          || !CotiSensorCrop.TryBox( host.MaskCenterX, host.MaskCenterY, host.MaskRadius, host.MaskFeather, main.aspect, out box ) )
        box = CotiCropBox.Whole;
      Box = box;

      EnsureRenderTexture( cfg, box );

      // aspect after targetTexture: assigning a target texture recomputes aspect from that
      // texture's dimensions, which would undo this and squash the image.
      if( _mirroredFov != main.fieldOfView )
      {
        _cam.fieldOfView = main.fieldOfView;
        _mirroredFov = main.fieldOfView;
      }

      if( _mirroredAspect != main.aspect )
      {
        _cam.aspect = main.aspect;
        _mirroredAspect = main.aspect;
      }

      ApplyCrop( main, box );

      CotiThermalRig.ApplyRenderMode( _cam, _tv, ref _replacing );

      ApplyTuning( cfg );

    }

    /// <summary>
    /// The main camera's perspective, built from its own field of view and aspect (so no jitter it may carry), then
    /// scaled and shifted in clip space so the box fills the target. Culling follows the projection.
    /// </summary>
    private static void ApplyCrop( Camera main, CotiCropBox box )
    {
      if( _projectedFov == main.fieldOfView && _projectedAspect == main.aspect && _projectedBox.Equals( box ) )
        return;

      float sx, sy, tx, ty;
      CotiSensorCrop.Projection( box, out sx, out sy, out tx, out ty );
      var crop = Matrix4x4.identity;
      crop.m00 = sx;
      crop.m03 = tx;
      crop.m11 = sy;
      crop.m13 = ty;
      _cam.projectionMatrix = crop * Matrix4x4.Perspective( main.fieldOfView, main.aspect, main.nearClipPlane, main.farClipPlane );

      _projectedFov = main.fieldOfView;
      _projectedAspect = main.aspect;
      _projectedBox = box;
    }

    private static void Follow( Camera main )
    {
      if( _followed == main.transform )
        return;

      _go.transform.SetParent( main.transform, worldPositionStays: false );

      // Only on re-parent: SetParent with worldPositionStays false is what makes the reset
      // necessary, and nothing else moves this object.
      _go.transform.localPosition = Vector3.zero;
      _go.transform.localRotation = Quaternion.identity;
      _go.transform.localScale = Vector3.one;

      _followed = main.transform;
    }

    /// <summary>
    /// Copies the main camera's settings onto ours, writing only what changed.
    ///
    /// Each of these is a native property, so writing all of them unconditionally pays a
    /// managed-to-native call per field per frame for values that move perhaps once a raid.
    /// </summary>
    private static void MirrorSettings( Camera main )
    {
      var first = !_mirrored;
      _mirrored = true;

      // Copies main's rendering path. EFT's ThermalVision reads G-buffer data that only exists in
      // deferred; in forward a warm object such as a fire barrel renders cold and only emissive
      // sources register.
      if( first || _mirroredPath != main.renderingPath )
      {
        _cam.renderingPath = main.renderingPath;
        _mirroredPath = main.renderingPath;
      }

      CotiDevTools.ReportCullingMasks( _prefabCullingMask, main.cullingMask );

      // Intersected with BSG's own scope mask, which omits Weapon Preview, Menu Environment and
      // three unused layers. A zero prefab mask would render nothing, so it defers to the player's.
      if( first || _mirroredCullingMask != main.cullingMask )
      {
        _cam.cullingMask = _prefabCullingMask == 0
            ? main.cullingMask
            : main.cullingMask & _prefabCullingMask;
        _mirroredCullingMask = main.cullingMask;
      }

      if( first || _mirroredClearFlags != main.clearFlags )
      {
        _cam.clearFlags = main.clearFlags;
        _mirroredClearFlags = main.clearFlags;
      }

      if( first || _mirroredBackground != main.backgroundColor )
      {
        _cam.backgroundColor = main.backgroundColor;
        _mirroredBackground = main.backgroundColor;
      }

      if( first || _mirroredNearClip != main.nearClipPlane )
      {
        _cam.nearClipPlane = main.nearClipPlane;
        _mirroredNearClip = main.nearClipPlane;
      }

      if( first || _mirroredFarClip != main.farClipPlane )
      {
        _cam.farClipPlane = main.farClipPlane;
        _mirroredFarClip = main.farClipPlane;
      }

      if( first || _mirroredHdr != main.allowHDR )
      {
        _cam.allowHDR = main.allowHDR;
        _mirroredHdr = main.allowHDR;
      }

      if( first || _mirroredOcclusion != main.useOcclusionCulling )
      {
        _cam.useOcclusionCulling = main.useOcclusionCulling;
        _mirroredOcclusion = main.useOcclusionCulling;
      }

      // The eye's per-layer cull distances, capped at the thermal's range: copying only the far clip drew to 10 km.
      Cull.Apply( _cam, main, Plugin.Config?.ThermalCamera?.RangeMetres ?? 0f );

      // Render before the main camera. Unity orders cameras by depth, and the compositor's buffer
      // runs on the main camera's AfterEverything, so a higher depth here would composite the
      // previous frame's heat, visible as lag when the player turns.
      if( first || _mirroredDepth != main.depth )
      {
        _cam.depth = main.depth - 1f;
        _mirroredDepth = main.depth;
      }
    }

    /// <summary>
    /// Refresh rate and image tuning come from config alone, so they are re-applied only when one
    /// of those values moves - nine native writes a frame otherwise.
    /// </summary>
    private static void ApplyTuning( CotiCameraConfig cfg )
    {
      var image = Plugin.Config.Image;

      if( _tunedHz == cfg.Hz
          && _tunedPixelated == image.IsPixelated
          && _tunedNoisy == image.IsNoisy
          && _tunedMotionBlurred == image.IsMotionBlurred
          && _tunedUnsharpRadius == image.UnsharpRadiusBlur
          && _tunedUnsharpBias == image.UnsharpBias
          && _tuned )
      {
        return;
      }

      CotiThermalRig.SetRefreshRate( _tv, cfg.Hz );
      CotiThermalRig.ApplyImageTuning( _tv, image );

      _tuned = true;
      _tunedHz = cfg.Hz;
      _tunedPixelated = image.IsPixelated;
      _tunedNoisy = image.IsNoisy;
      _tunedMotionBlurred = image.IsMotionBlurred;
      _tunedUnsharpRadius = image.UnsharpRadiusBlur;
      _tunedUnsharpBias = image.UnsharpBias;
    }

    /// <summary>
    /// Allocates the render target, reallocating only when the configured size changes, so
    /// width/height are live-tunable without leaking a texture per poll.
    ///
    /// These dimensions are the full-screen render. The circle receives only the fraction its
    /// radius covers (at maskRadius 0.274, about 55% of the height), so a blocky overlay calls for
    /// raising them.
    /// </summary>
    private static void EnsureRenderTexture( CotiCameraConfig cfg, CotiCropBox box )
    {
      // The full frame's texel density, over the box only.
      var width = CotiSensorCrop.Pixels( Mathf.Clamp( cfg.Width, 16, 4096 ), box.Width );
      var height = CotiSensorCrop.Pixels( Mathf.Clamp( cfg.Height, 16, 4096 ), box.Height );

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

      // The command-buffer counts reveal a disabled ThermalVision. It attaches one buffer to each
      // of BeforeForwardAlpha and AfterForwardAlpha in its Awake and fills them from OnPreCull, a
      // message only an enabled component receives. Counts of 1/1 mean the chain is wired; 0/0
      // means Awake never ran on this camera.
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
    /// Writes what the overlay shader produces, alongside the raw thermal dump. The pair is the
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
    /// on retries from scratch.
    /// </summary>
    internal static void Teardown()
    {
      if( _go != null )
      {
        UnityEngine.Object.Destroy( _go );
      }

      _go = null;
      _cam = null;
      _replacing = false;
      _tv = null;
      _followed = null;
      _broken = false;
      _loggedCreated = false;

      _mirrored = false;
      _mirroredFov = float.NaN;
      Cull.Reset();
      _mirroredAspect = float.NaN;
      _projectedFov = float.NaN;
      _projectedAspect = float.NaN;
      Box = CotiCropBox.Whole;
      _tuned = false;

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
