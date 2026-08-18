using System;
using Coti.Client.Dev;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// A second thermal camera, matched to a magnified optic instead of to the player's eye.
  ///
  /// A magnified scope disagrees with the 1x thermal by 1.3x to 7.9x, so heat inside the lens lands
  /// nowhere near what the lens shows. This renders the scene again from the optic's own position
  /// and field of view; <see cref="CotiOpticOverlayCompositor"/> puts the result into the optic's
  /// own target, so the scope's position, size and angle stay the game's problem.
  ///
  /// Opt in - the COTI is an offset sensor, so a 1x thermal is the honest default. Build details
  /// come from <see cref="CotiThermalRig"/> so a fix cannot land in one camera and miss the other.
  /// </summary>
  internal static class CotiOpticThermalCamera
  {
    private static GameObject _go;
    private static Camera _cam;
    private static ThermalVision _tv;
    private static RenderTexture _rt;

    private static int _rtWidth;
    private static int _rtHeight;

    /// <summary>
    /// BSG's own mask for a scope camera, before Configure narrows it.
    /// </summary>
    private static int _prefabCullingMask;

    /// <summary>
    /// Latches on failure so the camera stays down and logs once, rather than throwing every frame.
    /// Cleared by Teardown, which makes switching the setting off and on a genuine retry.
    /// </summary>
    private static bool _broken;

    private static bool _loggedCreated;

#if COTI_DEV
    private static CotiFrameDump.Countdown _dumps;
#endif

    internal static RenderTexture Output => _rt;

    internal static bool HasOutput => !_broken && _cam != null && _rt != null;

    /// <summary>
    /// The optic this camera is matched to, or an absent view. Published rather than re-read, so the
    /// compositor attaches to the same camera this was configured against.
    /// </summary>
    internal static CotiOpticView Optic { get; private set; }

    /// <summary>
    /// Whether a magnified image is genuinely on the optic's target this frame. A false positive
    /// costs the player a hole in the 1x overlay with nothing behind it.
    ///
    /// CotiState.Active is re-tested rather than trusted from <see cref="Optic"/>, because
    /// Plugin.Update composites in a finally block and Optic may describe the frame before.
    /// </summary>
    internal static bool Magnifying =>
        CotiState.Active && Optic.Present && HasOutput && _go != null && _go.activeSelf;

    /// <summary>
    /// Whether a ThermalVision belongs to this camera. Patches run for every instance in the game,
    /// so they have to ask.
    /// </summary>
    internal static bool Owns( ThermalVision candidate )
    {
      return candidate != null && ReferenceEquals( candidate, _tv );
    }

    internal static void Tick()
    {
      var cfg = Plugin.Config?.ThermalCamera;

      // Rides on the 1x camera's switch too, and shares its config: with the core off there is
      // nothing to magnify, and a second set of values would drift.
      if( cfg == null || !cfg.Enabled || Plugin.Config?.MagnifyWithOptic != true )
      {
        Teardown();
        return;
      }

      // Idle FIRST: MarkBroken is reachable after the object is already active, and a bare return
      // would leave it drawing a scene pass HasOutput then refuses to let anyone read.
      if( _broken )
      {
        Idle();
        return;
      }

      try
      {
        Optic = default( CotiOpticView );

        if( !CotiState.Active )
        {
          Idle();
          return;
        }

        var main = Camera.main;
        if( main == null )
          return; // not in raid yet - retry next frame

        var optic = CotiOpticCamera.Read();

        // A ratio, never a question about what kind of sight this is - red dots and irons already
        // line up, because the main camera's own field of view narrows on aiming.
        if( !CotiOpticFusion.ShouldMagnify(
                configEnabled: true, cotiActive: true, main.fieldOfView, optic.FieldOfView ) )
        {
          Idle();
          return;
        }

        if( !EnsureCamera() )
          return;

        Configure( main, optic, cfg );

        // Gate: no activation and no render until a target is proven bound.
        if( !ActivateIfReady() )
          return;

        // Only once the camera is rendering into a bound target, or the compositor would blit a
        // texture nothing has written.
        Optic = optic;

#if COTI_DEV
        DumpIfRequested( cfg, optic );
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
      _go = CotiThermalRig.Clone( prefab, "CotiOpticThermalCamera" );

      _cam = _go.GetComponent<Camera>();
      _tv = _go.GetComponent<ThermalVision>();

      if( _cam == null || _tv == null )
      {
        // Teardown BEFORE MarkBroken: Teardown clears the latch, so marking first leaves it false
        // and retries this whole load-strip-destroy cycle every frame.
        Teardown();
        MarkBroken(
            $"\"{CotiThermalRig.PrefabName}\" clone has camera={_cam != null} thermalVision={_tv != null}",
            null );
        return false;
      }

      _prefabCullingMask = _cam.cullingMask;

      CotiThermalRig.EnsureVolumetricLightRenderer( _go, _tv );

      // ThermalVision gates its own Update on _camera.enabled, so this must stay true: a disabled
      // camera still renders when driven by hand, but silently produces an ordinary lit image.
      _cam.enabled = true;
      _tv.On = true;

      Camera.onPreCull -= MatchOpticBeforeCulling;
      Camera.onPreCull += MatchOpticBeforeCulling;

      if( !_loggedCreated )
      {
        _loggedCreated = true;
        Plugin.Log.LogInfo(
            $"[COTI] Magnified thermal camera created from \"{CotiThermalRig.PrefabName}\" prefab " +
            $"(inactive); components remaining: {CotiThermalRig.DescribeComponents( _go )}" );
      }

      return true;
    }

    /// <summary>
    /// Matches the optic camera, read fresh each frame. NOT parented to it: that object is
    /// destroyed between raids and would take its children with it.
    /// </summary>
    private static void Configure( Camera main, CotiOpticView optic, CotiCameraConfig cfg )
    {
      var source = optic.Camera;

      // An INITIAL placement only, so the prewarm render has somewhere sane to stand. The pose that
      // matters is copied again in SyncToOptic, from Camera.onPreCull - see MatchOpticBeforeCulling.
      var sourceTransform = source.transform;
      _go.transform.SetPositionAndRotation( sourceTransform.position, sourceTransform.rotation );
      _go.transform.localScale = Vector3.one;

      // From MAIN, not the optic. Forward breaks the thermal image outright: ThermalVision reads
      // G-buffer data that only exists in deferred, so a warm object renders cold.
      _cam.renderingPath = main.renderingPath;

      // Intersected with BSG's scope mask. A zero prefab mask would render nothing, so it defers.
      _cam.cullingMask = _prefabCullingMask == 0
          ? main.cullingMask
          : main.cullingMask & _prefabCullingMask;

      _cam.clearFlags = main.clearFlags;
      _cam.backgroundColor = main.backgroundColor;
      _cam.allowHDR = main.allowHDR;
      _cam.useOcclusionCulling = main.useOcclusionCulling;

      // Clip planes from the OPTIC: a near plane taken from the eye would clip the barrel out of a
      // frame the scope renders.
      _cam.nearClipPlane = source.nearClipPlane;
      _cam.farClipPlane = source.farClipPlane;

      // Before the optic camera, whose AfterEverything composites this - otherwise the composite
      // takes the PREVIOUS frame's heat, which reads as lag the moment the player turns.
      _cam.depth = source.depth - 1f;

      EnsureRenderTexture( source, cfg );

      // aspect AFTER targetTexture: assigning a target recomputes aspect and would squash this.
      _cam.fieldOfView = source.fieldOfView;
      _cam.aspect = source.aspect;

      _tv.enabled = true;
      _tv.On = true;

      CotiThermalRig.SetRefreshRate( _tv, cfg.Hz );
      CotiThermalRig.ApplyHostLook( _tv, CotiState.Host );
    }

    /// <summary>
    /// Allocates the render target, reallocating only on a real size change.
    ///
    /// Aspect comes from the optic's own target, not from config: the composite is a full-target
    /// blit, so the two must describe the same frustum or the heat lands stretched.
    /// </summary>
    private static void EnsureRenderTexture( Camera source, CotiCameraConfig cfg )
    {
      var sourceTarget = source.targetTexture;

      // Capped by the optic's own 1024-square target. Rendering more texels than the destination
      // holds is waste, and raising the 1x sensor would otherwise drag this camera up with it.
      var width = Mathf.Clamp( cfg.Width, 16, 4096 );
      if( sourceTarget != null && sourceTarget.width > 0 )
        width = Mathf.Min( width, sourceTarget.width );

      var aspect = sourceTarget != null && sourceTarget.height > 0
          ? (float)sourceTarget.width / sourceTarget.height
          : 1f;

      var height = Mathf.Clamp( Mathf.RoundToInt( width / Mathf.Max( 0.01f, aspect ) ), 16, 4096 );

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
        name = "CotiOpticThermalRT",
        autoGenerateMips = false,
        useMipMap = false,
        filterMode = FilterMode.Bilinear,
      };
      _rt.Create();

      _rtWidth = width;
      _rtHeight = height;
      _cam.targetTexture = _rt;

      Plugin.Log.LogInfo( $"[COTI] Magnified thermal target {width}x{height} ({format})" );
    }

    /// <summary>
    /// Parks the camera without destroying it - rebuilding it per weapon lower costs more than
    /// leaving it asleep, and leaving it awake draws a scene pass nothing reads.
    /// </summary>
    private static void Idle()
    {
      if( _go != null && _go.activeSelf )
        _go.SetActive( false );
    }

    /// <summary>
    /// Activates only once the render target is proven bound. An unbound target renders to the
    /// backbuffer, which is the player's whole screen replaced by a thermal view.
    /// </summary>
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

        // BSG's own IE_PreWarm does this, to move first-use cost off the frame the device is
        // raised on. Strictly after the target is bound.
        try
        {
          _cam.Render();
        }
        catch( Exception ex )
        {
          MarkBroken( "prewarm render", ex );
          return false;
        }

        if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        {
          Plugin.Log.LogInfo(
              $"[COTI] Magnified thermal camera activated into {_rt.name} {_rt.width}x{_rt.height}, " +
              $"fov={_cam.fieldOfView:F2} requestedPath={_cam.renderingPath} " +
              $"actualPath={_cam.actualRenderingPath}, thermalOn={_tv.On}, tvEnabled={_tv.enabled}" );
        }
      }

      return true;
    }

#if COTI_DEV
    /// <summary>
    /// Dumps both halves, because they fail differently. <c>magnified</c> is this camera's output -
    /// neutral channel means say it is thermal, a colour cast says it rendered lit.
    /// <c>optic-target</c> is BSG's own SSAAOpticCurrent after the composite, so heat there means
    /// the buffer reached the lens texture.
    ///
    /// The optic target is read during Update, so it shows the previous frame's composite and the
    /// first dump of a batch can legitimately be empty.
    /// </summary>
    private static void DumpIfRequested( CotiCameraConfig cfg, CotiOpticView optic )
    {
      int index;
      if( !_dumps.Take( cfg.DumpFrames, out index ) )
        return;

      var mine = CotiFrameDump.Dump( _rt, "magnified", index );
      if( mine == null )
      {
        _dumps.Stop();
        return;
      }

      var target = optic.Camera == null ? null : optic.Camera.targetTexture;
      var composited = CotiFrameDump.Dump( target, "optic-target", index );

      var beforeAlpha = _cam.GetCommandBuffers( CameraEvent.BeforeForwardAlpha ).Length;
      var afterAlpha = _cam.GetCommandBuffers( CameraEvent.AfterForwardAlpha ).Length;

      Plugin.Log.LogInfo(
          $"[COTI] magnified dump -> {mine} " +
          $"fov={_cam.fieldOfView:F2} opticFov={optic.FieldOfView:F2} " +
          $"magnification={CotiOpticFusion.Magnification( Camera.main == null ? 0f : Camera.main.fieldOfView, optic.FieldOfView ):F2}x " +
          $"renderingPath={_cam.actualRenderingPath} " +
          $"camEnabled={_cam.enabled} tvEnabled={_tv.enabled} tvOn={_tv.On} " +
          $"cbBeforeAlpha={beforeAlpha} cbAfterAlpha={afterAlpha}" );

      Plugin.Log.LogInfo(
          composited == null
              ? "[COTI] optic target dump unavailable - the optic camera has no target texture"
              : $"[COTI] optic target (one frame stale) -> {composited}" );
    }
#endif

    /// <summary>
    /// Copies the optic's pose immediately before this camera culls.
    ///
    /// Doing it in Update was a frame late: the weapon animates after Update and the optic camera is
    /// posed from the weapon, so both cameras rendered from poses one frame apart. Invisible
    /// standing still, a large fraction of a 4.4 degree view while turning.
    ///
    /// onPreCull rather than LateUpdate, which would race BSG's own updater - the order of two
    /// LateUpdates is undefined without an explicit script execution order.
    /// </summary>
    private static void MatchOpticBeforeCulling( Camera rendering )
    {
      if( _broken || !ReferenceEquals( rendering, _cam ) )
        return;

      try
      {
        var optic = Optic.Camera;
        if( optic == null || _go == null )
          return;

        var t = optic.transform;
        _go.transform.SetPositionAndRotation( t.position, t.rotation );

        // Field of view too: read a frame early it shows the zoom the player has just left.
        _cam.fieldOfView = optic.fieldOfView;
      }
      catch( Exception ex )
      {
        // Latched: this runs once per camera per frame, and an unguarded throw at that rate is how
        // a previous defect leaked 72 GB.
        MarkBroken( "matching the optic transform before culling", ex );
      }
    }

    private static void MarkBroken( string what, Exception ex )
    {
      if( _broken )
        return;
      _broken = true;

      var detail = ex == null ? string.Empty : $": {ex}";
      Plugin.Log.LogError(
          $"[COTI] Magnified thermal camera disabled for this session - {what} failed{detail}" );
    }

    internal static void Teardown()
    {
      Camera.onPreCull -= MatchOpticBeforeCulling;

      if( _go != null )
        UnityEngine.Object.Destroy( _go );

      _go = null;
      _cam = null;
      _tv = null;
      _broken = false;
      _loggedCreated = false;
      Optic = default( CotiOpticView );

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
