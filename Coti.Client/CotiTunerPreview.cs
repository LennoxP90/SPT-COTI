using System;
using System.Collections.Generic;
using System.Text;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// A second camera on the item view CotiPoseTuner already holds, rendered into a RenderTexture
  /// the panel draws. Reusing the game's own item view means the real bundle in real pool state,
  /// at whatever distance and FOV EFT's inspect window will not allow.
  ///
  /// The orbit/zoom/framing arithmetic is in CotiOrbitMath, where a test can reach it; this is
  /// the Unity half.
  ///
  /// The camera and target are allocated when the panel first opens, reused (never reallocated
  /// on resize), and released when it closes. The GameObject is deactivated rather than
  /// destroyed while the host root is missing, so a transient gap is not a teardown.
  /// </summary>
  internal static class CotiTunerPreview
  {
    // Fixed size, stretched to fit whatever rect the panel draws it into, so a window drag cannot
    // churn a render target. Square, because the viewport is square and a 1.5:1 texture stretched
    // into it distorted the model - an aspect of 1 also keeps FramingDistance exact.
    private const int TextureWidth = 512;
    private const int TextureHeight = 512;

    private const float FieldOfViewDegrees = 35f;
    private const float DegreesPerPixel = 0.35f;
    private const float MetresPerScrollUnit = 0.02f;

    private const float DefaultDistanceMetres = 0.25f;
    private const float DefaultPitchDegrees = 12f;
    private const float DefaultYawDegrees = 200f;

    private static GameObject _go;
    private static Camera _cam;
    private static RenderTexture _rt;
    private static bool _broken;
    private static bool _loggedCreated;

    private static float _yawDegrees = DefaultYawDegrees;
    private static float _pitchDegrees = DefaultPitchDegrees;
    private static float _distanceMetres = DefaultDistanceMetres;

    // The host the view and mask were last resolved for - only "has it changed", not which id.
    private static string _trackedHostId;

    // Resolved once per host plus throttled retries while provisional: a renderer walk allocates.
    private static int _cullingMask;
    private static Light _light;
    private static bool _loggedRenderState;

    // Whether the last resolve is trustworthy. While false it retries rather than freezing at
    // whatever the mask happened to be when the host changed.
    private static bool _maskProvisional;
    private static int _maskRetryCount;
    private static float _nextMaskRetryTime;

    // Bounded retries while the COTI's item view is still being parented asynchronously.
    private const float ProvisionalRetryIntervalSeconds = 0.25f;
    private const int MaxProvisionalRetries = 8;

    /// <summary>
    /// The rendered frame, or null whenever there is nothing worth drawing - the panel falls back
    /// to a placeholder label in every one of those cases rather than drawing a stale or blank
    /// texture.
    /// </summary>
    internal static RenderTexture Texture => _broken ? null : _rt;

    /// <summary>
    /// Whether the live item view exists for this frame - the panel uses this to tell "no camera
    /// yet" apart from "model not in scene right now", the same distinction
    /// CotiPoseTuner.FlipUnavailableReason draws for the same underlying reason.
    /// </summary>
    internal static bool HasLiveModel => CotiPoseTuner.LiveHostRoot != null;

    /// <summary>
    /// Camera lifecycle and per-frame transform update. Called from Plugin.Update, unconditionally
    /// - like CotiPoseTuner.Tick, it does nothing at all once IsOpen is false, so this is not a
    /// per-frame cost for a player who never opens the panel.
    /// </summary>
    internal static void Tick()
    {
      if( !CotiPoseTuner.IsOpen )
      {
        Teardown();
        return;
      }

      var root = CotiPoseTuner.LiveHostRoot;
      if( root == null )
      {
        Idle();
        return;
      }

      if( _broken )
        return;

      try
      {
        EnsureCamera();
        EnsureRenderTexture();

        if( CotiPoseTuner.OpenHostId != _trackedHostId )
          OnHostChanged( root );
        else if( ShouldRetryMask() )
          ResolveAndApplyMask( root );

        PositionCamera( root );

        if( !_go.activeSelf )
          _go.SetActive( true );
      }
      catch( Exception ex )
      {
        MarkBroken( "per-frame update", ex );
      }
    }

    // ---- Panel-facing controls -----------------------------------------------------------------

    /// <summary>
    /// One drag step, in the same screen-pixel units GUI's own Event.delta reports. All of the
    /// clamping and wrapping is CotiOrbitMath's - this only threads the current state through it.
    /// </summary>
    internal static void Orbit( float dragDeltaX, float dragDeltaY )
    {
      CotiOrbitMath.ApplyDrag(
          _yawDegrees, _pitchDegrees, dragDeltaX, dragDeltaY, DegreesPerPixel,
          out _yawDegrees, out _pitchDegrees );
    }

    /// <summary>One scroll tick, in Input.mouseScrollDelta units.</summary>
    internal static void Zoom( float scrollDelta )
    {
      _distanceMetres = CotiOrbitMath.ApplyZoom( _distanceMetres, scrollDelta, MetresPerScrollUnit );
    }

    internal static void ResetView()
    {
      _yawDegrees = DefaultYawDegrees;
      _pitchDegrees = DefaultPitchDegrees;
      _distanceMetres = DefaultDistanceMetres;
    }

    /// <summary>
    /// Frames the live host's measured bounds, so "fit it in view" does not depend on which way the
    /// model happens to be facing.
    /// </summary>
    internal static void FrameCurrent()
    {
      var bounds = CotiPoseTuner.LiveHostBounds;
      if( bounds == null )
        return;

      _distanceMetres = CotiOrbitMath.FramingDistance( bounds.Value.size.magnitude, FieldOfViewDegrees );
    }

    // ---- Camera and render target lifecycle ------------------------------------------------------

    /// <summary>
    /// What the camera and its target actually hold at render time. Exists because the viewport
    /// draws white where this code sets dark grey, and two plausible explanations were both wrong.
    /// </summary>
    private static void LogRenderStateOnce()
    {
      if( _loggedRenderState || _cam == null )
        return;

      _loggedRenderState = true;

      var rt = _cam.targetTexture;
      Plugin.Log.LogInfo(
          "[COTI TUNE] preview render state: " +
          $"clear={_cam.clearFlags} bg={_cam.backgroundColor} " +
          $"target={( rt == null ? "(null)" : $"{rt.width}x{rt.height} {rt.format} srgb={rt.sRGB} depth={rt.depth}" )} " +
          $"light={( _light == null ? "(none)" : $"{_light.type} i={_light.intensity} on={_light.enabled} mask={_light.cullingMask:X8}" )} " +
          $"linear={QualitySettings.activeColorSpace} ambient={UnityEngine.RenderSettings.ambientLight}" );
    }

    private static void EnsureCamera()
    {
      if( _go != null && _cam != null )
        return;

      _go = new GameObject( "CotiTunerPreviewCamera" );
      _go.SetActive( false );

      _cam = _go.AddComponent<Camera>();
      _cam.clearFlags = CameraClearFlags.SolidColor;
      _cam.backgroundColor = new Color( 0.07f, 0.07f, 0.08f, 1f );
      _cam.fieldOfView = FieldOfViewDegrees;
      _cam.nearClipPlane = 0.01f;
      _cam.farClipPlane = 4f;
      _cam.useOcclusionCulling = false;
      _cam.allowHDR = false;
      _cam.allowMSAA = false;

      // Without a light the geometry renders as a flat black silhouette, which cannot show the COTI
      // against the host - the only thing this viewport is for.
      //
      // It is culled to the camera's layers, and those layers hold the GAME'S inspect model, so it
      // can brighten EFT's own inspect view. Hence the config toggle.
      var lightGo = new GameObject( "CotiTunerPreviewLight" );
      lightGo.transform.SetParent( _go.transform, worldPositionStays: false );
      lightGo.transform.localRotation = Quaternion.Euler( 25f, 15f, 0f );

      _light = lightGo.AddComponent<Light>();
      _light.type = LightType.Directional;
      _light.intensity = 1.15f;
      _light.shadows = LightShadows.None;

      if( !_loggedCreated )
      {
        _loggedCreated = true;
        Plugin.Log.LogInfo( "[COTI TUNE] preview camera created" );
      }
    }

    /// <summary>
    /// Allocated once at a fixed size and reused for as long as the panel stays open - see the
    /// class comment for why the size never tracks the panel's own, resizable rect.
    /// </summary>
    private static void EnsureRenderTexture()
    {
      if( _rt != null )
      {
        if( _cam.targetTexture != _rt )
          _cam.targetTexture = _rt;
        return;
      }

      _rt = new RenderTexture( TextureWidth, TextureHeight, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default )
      {
        name = "CotiTunerPreviewRT",
        autoGenerateMips = false,
        useMipMap = false,
        filterMode = FilterMode.Bilinear,
      };
      _rt.Create();
      _cam.targetTexture = _rt;

      Plugin.Log.LogInfo( $"[COTI TUNE] preview camera target {TextureWidth}x{TextureHeight}" );
    }

    /// <summary>
    /// Per-host setup: the culling mask walk allocates, so it is not repeated every Tick.
    /// </summary>
    private static void OnHostChanged( Transform root )
    {
      _trackedHostId = CotiPoseTuner.OpenHostId;
      _maskRetryCount = 0;

      ResolveAndApplyMask( root );

      ResetView();
      FrameCurrent();
    }

    /// <summary>
    /// Whether a throttled retry of the mask is due - provisional, under the attempt cap, and the
    /// retry interval has elapsed. Called every Tick, but cheap: three field reads and a time
    /// comparison, none of it a scene walk, so checking it every frame costs nothing even though
    /// acting on it (ResolveAndApplyMask) is rare and bounded.
    /// </summary>
    private static bool ShouldRetryMask()
    {
      return _maskProvisional
          && _maskRetryCount < MaxProvisionalRetries
          && Time.unscaledTime >= _nextMaskRetryTime;
    }

    /// <summary>
    /// Resolves the mask, applies it to the cached field, and logs the result - called once from
    /// OnHostChanged and again on each throttled retry <see cref="ShouldRetryMask"/> allows, never
    /// per frame otherwise. GetComponentsInChildren allocates, which is exactly why this is bounded
    /// to a host change plus a small, throttled number of retries rather than a per-frame poll.
    /// </summary>
    private static void ResolveAndApplyMask( Transform root )
    {
      var resolution = ResolveCullingMask( root );

      _cullingMask = resolution.Mask;
      _maskProvisional = resolution.Provisional;
      _maskRetryCount++;
      _nextMaskRetryTime = Time.unscaledTime + ProvisionalRetryIntervalSeconds;

      LogCullingMask( _trackedHostId, _cullingMask, _maskProvisional, _maskRetryCount );

      if( _maskProvisional && _maskRetryCount >= MaxProvisionalRetries )
      {
        Plugin.Log.LogWarning(
            $"[COTI TUNE] preview camera mask for {_trackedHostId ?? "(none)"} still provisional " +
            $"after {MaxProvisionalRetries} attempts - giving up; the COTI may be missing from the preview" );
      }
    }

    /// <summary>
    /// One resolve's result: the union CotiLayerMask.FoldLayerMask computed, and whether it can be
    /// trusted yet.
    /// </summary>
    private readonly struct CullingMaskResolution
    {
      internal readonly int Mask;
      internal readonly bool Provisional;

      internal CullingMaskResolution( int mask, bool provisional )
      {
        Mask = mask;
        Provisional = provisional;
      }
    }

    /// <summary>
    /// The union of every Renderer's layer under the item view root. Correct by construction: the
    /// COTI's layer is included because its renderer is walked, not because of any assumption about
    /// when a layer push happens relative to parenting.
    /// </summary>
    private static CullingMaskResolution ResolveCullingMask( Transform root )
    {
      var renderers = root.GetComponentsInChildren<Renderer>( includeInactive: true );
      var layers = new List<int>( renderers.Length );

      foreach( var renderer in renderers )
        layers.Add( renderer.gameObject.layer );

      var mask = CotiLayerMask.FoldLayerMask( layers, root.gameObject.layer );
      var provisional = CotiPoseTuner.OpenHostCotiAttached && !CotiRenderersExist( root );

      return new CullingMaskResolution( mask, provisional );
    }

    /// <summary>
    /// Whether the COTI's own item view has actually been parented under the mount bone yet -
    /// see ResolveCullingMask's own comment for why this, and not a layer-count heuristic, is the
    /// exact provisional test. CotiIds.ModSlotName is the same bone name CotiMountBonePatch creates
    /// and CotiAttachPatch parents the COTI's item view under.
    /// </summary>
    private static bool CotiRenderersExist( Transform root )
    {
      var bone = EftCompat.FindTransformRecursive( root, CotiIds.ModSlotName, ignoreCase: true );
      return bone != null && bone.GetComponentInChildren<Renderer>( true ) != null;
    }

    // Release, not COTI_DEV: a black viewport is the expected failure and this names the layers.
    private static void LogCullingMask( string hostId, int mask, bool provisional, int attempt )
    {
      var suffix = provisional
          ? $" - PROVISIONAL (attempt {attempt}/{MaxProvisionalRetries}, COTI attached per its " +
              "slot but not yet found under the mount bone - retrying)"
          : string.Empty;

      Plugin.Log.LogInfo(
          $"[COTI TUNE] preview camera mask for {hostId ?? "(none)"}: {mask:X8} " +
          $"[{DescribeCullingMask( mask )}]{suffix}" );
    }

    /// <summary>
    /// Internal, not private: CotiDevTools.ReportCullingMasks (COTI_DEV only) calls this rather
    /// than keeping its own near-identical copy - see LogCullingMask's own comment.
    /// </summary>
    internal static string DescribeCullingMask( int mask )
    {
      var names = new StringBuilder();

      for( var layer = 0; layer < 32; layer++ )
      {
        if( ( mask & ( 1 << layer ) ) == 0 )
          continue;

        var name = LayerMask.LayerToName( layer );
        if( names.Length > 0 )
          names.Append( ", " );
        names.Append( string.IsNullOrEmpty( name ) ? layer.ToString() : $"{layer}:{name}" );
      }

      return names.ToString();
    }

    /// <summary>
    /// Positioned directly in world space from the pivot outward, not parented to the host - the
    /// host root can be a pooled inventory view one frame and a player-worn item the next, and a
    /// parented camera would inherit whichever ancestry it currently sits under rather than the
    /// stable orbit this viewport promises.
    /// </summary>
    private static void PositionCamera( Transform root )
    {
      var bounds = CotiPoseTuner.LiveHostBounds;
      var pivot = bounds?.center ?? root.position;

      var rotation = Quaternion.Euler( _pitchDegrees, _yawDegrees, 0f );
      var forward = rotation * Vector3.forward;

      _cam.transform.SetPositionAndRotation( pivot - forward * _distanceMetres, rotation );

      // Set from the per-host resolve in OnHostChanged, not recomputed here - see ResolveCullingMask.
      _cam.cullingMask = _cullingMask;

      if( _light != null )
      {
        _light.cullingMask = _cullingMask;
        _light.enabled = Plugin.Config?.TunerPreviewLight ?? true;
      }

      // Re-asserted per frame in case something external resets it; the white background is not
      // explained by anything in this file.
      _cam.clearFlags = CameraClearFlags.SolidColor;
      _cam.backgroundColor = new Color( 0.07f, 0.07f, 0.08f, 1f );

      LogRenderStateOnce();
    }

    /// <summary>
    /// Deactivates without releasing, for a transient gap - distinct from the panel closing.
    /// </summary>
    private static void Idle()
    {
      if( _go != null && _go.activeSelf )
        _go.SetActive( false );
    }

    private static void MarkBroken( string what, Exception ex )
    {
      if( _broken )
        return;
      _broken = true;

      var detail = ex == null ? string.Empty : $": {ex}";
      Plugin.Log.LogError( $"[COTI TUNE] preview camera disabled for this session - {what} failed{detail}" );
    }

    /// <summary>
    /// Destroys the camera and releases its render target. Called every frame the panel is
    /// closed (a cheap null check when it already has nothing to tear down) and from
    /// Plugin.OnDestroy, so nothing outlives either the panel session or the plugin itself.
    /// </summary>
    internal static void Teardown()
    {
      if( _go != null )
        UnityEngine.Object.Destroy( _go );

      _go = null;
      _cam = null;
      _broken = false;
      _loggedCreated = false;
      _trackedHostId = null;
      _cullingMask = 0;
      _maskProvisional = false;
      _maskRetryCount = 0;
      _nextMaskRetryTime = 0f;

      ReleaseRenderTexture();
    }

    private static void ReleaseRenderTexture()
    {
      if( _rt == null )
        return;

      _rt.Release();
      UnityEngine.Object.Destroy( _rt );
      _rt = null;
    }
  }
}
