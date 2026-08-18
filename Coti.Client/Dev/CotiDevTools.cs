using System.Diagnostics;
using UnityEngine;

#if COTI_DEV
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
#endif

namespace Coti.Client.Dev
{
  /// <summary>
  /// Tools for adding support for a new night vision device: measure its geometry, report what EFT
  /// does to the model on attach, and nudge the mount pose live.
  ///
  /// Entry points are [Conditional] on COTI_DEV, defined for Debug only, so Release drops the calls
  /// rather than testing a flag every frame. Reporting is automatic in a dev build; the config switch
  /// arms only the pose keys.
  /// </summary>
  public static class CotiDevTools
  {
    /// <summary>
    /// Re-applies the pose with this session's tuning deltas on top.
    /// </summary>
    [Conditional( "COTI_DEV" )]
    public static void OnMountPosed( Transform bone, CotiNvgHostConfig host, string hostId, string hostName )
    {
#if COTI_DEV
      if( bone == null || hostId == null )
        return;

      if( hostId != _hostId )
      {
        Plugin.Log.LogInfo(
            $"[COTI TUNE] now tuning {hostName} ({hostId}) - {ModifierName()} and the arrows/,./[]/;'/-= keys" );
      }

      _hostId = hostId;
      _hostName = hostName;
      _bone = bone;
      _host = host;

      ForgetDeltasIfConfigChanged( hostId, host );

      CotiMountPose.Apply( bone, host, Delta( Positions, hostId ), Delta( Rotations, hostId ), ScaleDelta( hostId ) );
#endif
    }

    /// <summary>
    /// Dumps every active camera on demand, so the optic pipeline can be read from a raid rather
    /// than guessed at. Press once while not aiming and once while looking through a scope: the
    /// difference identifies the optic camera, its field of view, and whether it renders into a
    /// texture drawn onto a lens (picture in picture) or straight to the screen.
    ///
    /// On a key rather than per frame deliberately. A previous verbose session produced a 104 MB
    /// log; two deliberate dumps are worth more than ten thousand frames of it.
    /// </summary>
    [Conditional( "COTI_DEV" )]
    public static void TickCameraProbe()
    {
#if COTI_DEV
      if( !Input.GetKeyDown( KeyCode.F9 ) )
        return;

      var report = new StringBuilder();
      report.Append( $"[COTI PROBE] {_probeCount++}: active cameras, main FOV {( Camera.main == null ? -1f : Camera.main.fieldOfView ):F2}" );

      // Camera.allCameras is enabled cameras only, which is what matters: a disabled optic camera
      // is not what the player is looking through.
      var cameras = Camera.allCameras.OrderByDescending( c => c.depth ).ToArray();

      foreach( var cam in cameras )
      {
        var target = cam.targetTexture == null
            ? "SCREEN"
            : $"RT {cam.targetTexture.width}x{cam.targetTexture.height} \"{cam.targetTexture.name}\"";

        report.Append(
            $"\n  {cam.name}" +
            $"\n      fov={cam.fieldOfView:F2} depth={cam.depth} target={target}" +
            $"\n      rect=({cam.rect.x:F3},{cam.rect.y:F3},{cam.rect.width:F3},{cam.rect.height:F3})" +
            $" pixels={cam.pixelWidth}x{cam.pixelHeight}" +
            $"\n      clip={cam.nearClipPlane:F3}..{cam.farClipPlane:F0} mask={cam.cullingMask:X8}" +
            $" parent='{( cam.transform.parent == null ? "(none)" : cam.transform.parent.name )}'" +
            $" isMain={ReferenceEquals( cam, Camera.main )}" );
      }

      // Who DRAWS the optic texture onto the lens. The overlay was measured painting straight over
      // the lens, so a magnified thermal stays invisible until it can be masked out of exactly that
      // shape, and this is the only thing that knows what the shape is.
      ReportLensRenderers( report );

      ReportOpticAlignment( report );

      report.Append( $"\n  COTI thermal output: {( CotiThermalCamera.Output == null ? "(none)" : CotiThermalCamera.Output.width + "x" + CotiThermalCamera.Output.height )}" );
      report.Append( $"\n  COTI active: {CotiState.Active}, mask: {( CotiState.Mask == null ? "(none)" : CotiState.Mask.width + "x" + CotiState.Mask.height )}" );

      Plugin.Log.LogInfo( report.ToString() );
#endif
    }


#if COTI_DEV
    /// <summary>
    /// Dumps every thermal target. On a key because opening F12 moves the camera, so the frame
    /// written would not be the frame being looked at.
    /// </summary>
    private static void TickDumpKey()
    {
      if( !Input.GetKeyDown( KeyCode.F10 ) )
        return;

      CotiFrameDump.RequestBatch( 2 );
      Plugin.Log.LogInfo(
          $"[COTI] F10: dumping 2 frames at {Plugin.Config?.ThermalCamera?.Height ?? 0} rows" );
    }

    /// <summary>
    /// Steps the sensor resolution, for the same reason - the comparison is between resolutions on
    /// one unchanged view. Writes config directly; an F12 change overwrites it, which is correct.
    /// </summary>
    private static void TickResolutionKey()
    {
      if( !Input.GetKeyDown( KeyCode.F11 ) )
        return;

      var camera = Plugin.Config?.ThermalCamera;
      if( camera == null )
        return;

      camera.Height = CotiSensorResolutions.Next( camera.Height );
      camera.Width = CotiSensorResolutions.WidthFor( camera.Height );

      Plugin.Log.LogInfo( $"[COTI] F11: sensor now {camera.Width}x{camera.Height}" );
    }

    /// <summary>
    /// Whether the magnified path lined UP, not just whether it ran. A mismatch in field of view or
    /// transform is invisible in the camera list, where both cameras look equally healthy.
    /// </summary>
    private static void ReportOpticAlignment( StringBuilder report )
    {
      var optic = CotiOpticCamera.Read();

      if( !optic.Present )
      {
        report.Append(
            "\n  optic: none this frame - hipfire, iron sights or a non-magnified sight. Expected, " +
            "and the case where the 1x overlay already lines up on its own." );
        return;
      }

      var main = Camera.main;
      var mainFov = main == null ? 0f : main.fieldOfView;

      report.Append(
          $"\n  optic: fov={optic.FieldOfView:F2} vs main {mainFov:F2}" +
          $" = {CotiOpticFusion.Magnification( mainFov, optic.FieldOfView ):F2}x" +
          $" (gate is {CotiOpticFusion.MinimumMagnification:F2}x)," +
          $" lens={( optic.Lens == null ? "(none)" : optic.Lens.name )}" );

      var output = CotiOpticThermalCamera.Output;
      report.Append(
          $"\n      magnifying={CotiOpticThermalCamera.Magnifying}" +
          $" output={( output == null ? "(none)" : output.width + "x" + output.height )}" );

      // The optic camera's own transform, so a silent failure to copy it shows up here rather than
      // being reported as matched on the strength of Configure having been called.
      var theirs = optic.Camera.transform;
      report.Append( $"\n      optic at pos={theirs.position} rot={theirs.rotation.eulerAngles}" );

      ReportLensExclusion( report );
    }

    /// <summary>
    /// Where the 1x overlay is cutting the lens out of itself, read at a moment of your choosing -
    /// the one-shot log fires mid-ADS, nowhere near where the lens settles.
    ///
    /// Pixels as well as normalised: a round lens is a taller ellipse in normalised space on a
    /// non-square view, so the raw radii look wrong at a glance.
    /// </summary>
    private static void ReportLensExclusion( StringBuilder report )
    {
      var lens = CotiOverlayCompositor.LensExclusion;

      if( lens.z <= 0f || lens.w <= 0f )
      {
        report.Append(
            "\n      lens exclusion: NONE - the 1x heat is still painted over the lens. Expected only" +
            " with Magnified Lens Cover at 0, or if the lens projection was rejected." );
        return;
      }

      var camera = Camera.main;
      var width = camera == null ? 0 : camera.pixelWidth;
      var height = camera == null ? 0 : camera.pixelHeight;

      report.Append(
          $"\n      lens exclusion: centre ({lens.x:F3}, {lens.y:F3}) radius ({lens.z:F3}, {lens.w:F3})" +
          $" = centre ({lens.x * width:F0}, {lens.y * height:F0}) radius ({lens.z * width:F0}," +
          $" {lens.w * height:F0}) px of {width}x{height}" +
          $", cover={Plugin.Config?.MagnifiedLensCover ?? 1f:F2}" );
    }

    /// <summary>
    /// Finds every renderer whose material samples an optic render target, and reports where it
    /// lands on screen. That is the candidate mask for keeping the 1x overlay off the lens.
    ///
    /// Searched by TEXTURE rather than by name or shader: the thing being looked for is defined by
    /// what it draws, which survives a rename.
    /// </summary>
    private static void ReportLensRenderers( StringBuilder report )
    {
      var camera = Camera.main;
      var found = 0;
      var blocks = 0;

      // Deliberately unfiltered. The narrow version looked for a texture named like an optic target
      // on four guessed property names and found nothing, which could equally have meant the wrong
      // property name, a per-instance binding, or no renderer at all. This reports every render
      // texture reachable from any renderer, so the answer is read rather than guessed.
      foreach( var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>() )
      {
        if( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
          continue;

        if( renderer.HasPropertyBlock() )
          blocks++;

        // Per-instance bindings first. The two searches before this found nothing on the shared
        // materials while 1039 renderers carried a property block, which is where a per-frame
        // texture assignment hides.
        if( renderer.HasPropertyBlock() )
        {
          renderer.GetPropertyBlock( _probeBlock );

          foreach( var name in TextureProperties )
          {
            var blockTexture = _probeBlock.GetTexture( Shader.PropertyToID( name ) );
            if( !( blockTexture is RenderTexture ) )
              continue;

            found++;
            if( found <= 20 )
            {
              report.Append(
                  $"\nRT IN PROPERTY BLOCK on '{renderer.name}' property='{name}' texture='{blockTexture.name}'" +
                  $"\n      shader='{( renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ? "?" : renderer.sharedMaterial.shader.name )}'" +
                  $" layer={renderer.gameObject.layer} ({LayerMask.LayerToName( renderer.gameObject.layer )})" +
                  $"\n      screen {ScreenRect( renderer, camera )}" );
            }
          }
        }

        foreach( var material in renderer.sharedMaterials )
        {
          if( material == null || material.shader == null )
            continue;

          foreach( var name in TextureProperties )
          {
            if( !material.HasProperty( name ) )
              continue;

            var texture = material.GetTexture( name );
            if( !( texture is RenderTexture ) )
              continue;

            found++;
            if( found > 20 )
              continue;

            report.Append(
                $"\nRT ON RENDERER '{renderer.name}' property='{name}' texture='{texture.name}'" +
                $"\n      shader='{material.shader.name}' queue={material.renderQueue}" +
                $" layer={renderer.gameObject.layer} ({LayerMask.LayerToName( renderer.gameObject.layer )})" +
                $"\n      screen {ScreenRect( renderer, camera )}" );
          }
        }
      }

      report.Append( $"\n  renderers sampling a RenderTexture: {found}, renderers with a MaterialPropertyBlock: {blocks}" );
    }

    /// <summary>
    /// Shader property names are only enumerable through UnityEditor at edit time, so at runtime the
    /// list has to be a fixed one. This is every texture property name seen on EFT weapon and optic
    /// materials, plus the obvious Unity defaults.
    /// </summary>
    private static readonly string[] TextureProperties =
    {
      "_MainTex", "_BaseMap", "_Texture", "_OpticTexture", "_ScopeTexture", "_LensTexture",
      "_CameraTexture", "_RenderTex", "_SecondTex", "_DetailTex", "_EmissionMap", "_ReflectionTex",
      "_GlassTexture", "_SightTexture", "_ReticleTex", "_Overlay", "_OverlayTex",
    };

    /// <summary>
    /// Reused rather than allocated per renderer: this walks a whole raid scene.
    /// </summary>
    private static readonly MaterialPropertyBlock _probeBlock = new MaterialPropertyBlock();

    private static string ScreenRect( Renderer renderer, Camera camera )
    {
      if( camera == null )
        return "(no camera)";

      var b = renderer.bounds;
      var minX = float.MaxValue; var minY = float.MaxValue;
      var maxX = float.MinValue; var maxY = float.MinValue;

      for( var corner = 0; corner < 8; corner++ )
      {
        var point = new Vector3(
            ( corner & 1 ) == 0 ? b.min.x : b.max.x,
            ( corner & 2 ) == 0 ? b.min.y : b.max.y,
            ( corner & 4 ) == 0 ? b.min.z : b.max.z );

        var screen = camera.WorldToScreenPoint( point );
        if( screen.x < minX ) minX = screen.x;
        if( screen.y < minY ) minY = screen.y;
        if( screen.x > maxX ) maxX = screen.x;
        if( screen.y > maxY ) maxY = screen.y;
      }

      return $"x {minX:F0}..{maxX:F0}, y {minY:F0}..{maxY:F0} (camera {camera.pixelWidth}x{camera.pixelHeight})";
    }
#endif

    [Conditional( "COTI_DEV" )]
    public static void Tick()
    {
#if COTI_DEV
      TickCameraProbe();
      TickDumpKey();
      TickResolutionKey();

      if( Plugin.Config == null || !Plugin.Config.EnablePoseModifier )
        return;
      if( _bone == null || _hostId == null )
        return;
      if( !ModifierHeld() )
        return;

      var fine = Input.GetKey( KeyCode.LeftShift );
      var move = ( fine ? Plugin.Config.TunerStepMm / FineDivisorDistance : Plugin.Config.TunerStepMm ) / 1000f;
      var turn = fine ? Plugin.Config.TunerStepDegrees / FineDivisorAngle : Plugin.Config.TunerStepDegrees;
      var grow = fine ? Plugin.Config.TunerStepScale / FineDivisorScale : Plugin.Config.TunerStepScale;

      var dp = Vector3.zero;
      var dr = Vector3.zero;
      var ds = 0f;

      if( Input.GetKeyDown( KeyCode.UpArrow ) ) dp.y += move;
      if( Input.GetKeyDown( KeyCode.DownArrow ) ) dp.y -= move;
      if( Input.GetKeyDown( KeyCode.LeftArrow ) ) dp.x -= move;
      if( Input.GetKeyDown( KeyCode.RightArrow ) ) dp.x += move;
      if( Input.GetKeyDown( KeyCode.PageUp ) ) dp.z += move;       // depth, forward
      if( Input.GetKeyDown( KeyCode.PageDown ) ) dp.z -= move;     // depth, backward

      if( Input.GetKeyDown( KeyCode.Comma ) ) dr.z -= turn;        // roll, left
      if( Input.GetKeyDown( KeyCode.Period ) ) dr.z += turn;       // roll, right
      if( Input.GetKeyDown( KeyCode.LeftBracket ) ) dr.x -= turn;  // pitch, nose down
      if( Input.GetKeyDown( KeyCode.RightBracket ) ) dr.x += turn; // pitch, nose up
      if( Input.GetKeyDown( KeyCode.Semicolon ) ) dr.y -= turn;    // yaw, nose left
      if( Input.GetKeyDown( KeyCode.Quote ) ) dr.y += turn;        // yaw, nose right

      if( Input.GetKeyDown( KeyCode.Minus ) ) ds -= grow;          // smaller
      if( Input.GetKeyDown( KeyCode.Equals ) ) ds += grow;         // larger

      if( dp == Vector3.zero && dr == Vector3.zero && ds == 0f )
        return;

      Positions[_hostId] = Delta( Positions, _hostId ) + dp;
      Rotations[_hostId] = Delta( Rotations, _hostId ) + dr;
      Scales[_hostId] = ScaleDelta( _hostId ) + ds;

      CotiMountPose.Apply( _bone, _host, Positions[_hostId], Rotations[_hostId], Scales[_hostId] );

      var rotation = Rotations[_hostId];
      var position = _bone.localPosition;

      // Read back off the transform rather than recomputing: CotiMountPose clamps the scale, so a
      // recomputed figure could be one the device is not actually wearing.
      var scale = _bone.localScale.x;

      Plugin.Log.LogInfo(
          $"[COTI TUNE] {_hostName} {_hostId}  \"mountPositionX\": {position.x:F3}, " +
          $"\"mountPositionY\": {position.y:F3}, \"mountPositionZ\": {position.z:F3}, " +
          $"\"mountRollDegrees\": {( _host == null ? 0f : _host.MountRollDegrees ) + rotation.z:F0}, " +
          $"\"mountPitchDegrees\": {( _host == null ? 0f : _host.MountPitchDegrees ) + rotation.x:F0}, " +
          $"\"mountYawDegrees\": {( _host == null ? 0f : _host.MountYawDegrees ) + rotation.y:F0}, " +
          $"\"mountScale\": {scale:F3}" );
#endif
    }

    /// <summary>
    /// Reports the COTI's size and placement at the instant EFT parents it to the mount bone, so a
    /// per-host pose can be derived from measurements instead of guessed from screenshots.
    ///
    /// It also records the rotation EFT imposes. InsertItem takes one of two branches:
    ///
    ///     ModPlacer placer = itemView.GetComponent&lt;ModPlacer&gt;();
    ///     if (placer != null) { ...use placer's position/rotation/scale... }
    ///     else                { localPosition = zero; localRotation = Euler(90, 0, 0); scale = one; }
    ///
    /// Our prefab carries no ModPlacer, so it always takes the second branch and is turned 90 degrees
    /// about X no matter what the bone says. That is not a bug to fix - it is a constant the mount
    /// rotation has to be expressed relative to, and knowing it beats trial and error.
    /// </summary>
    [Conditional( "COTI_DEV" )]
    public static void ReportAttach( GameObject itemView, Transform bone )
    {
#if COTI_DEV
      var report = new StringBuilder();
      report.Append( $"[COTI] Attached {itemView.name} to {bone.name}" );
      report.Append( $"\n  local pos {itemView.transform.localPosition}, " +
                    $"rot {itemView.transform.localEulerAngles}, " +
                    $"scale {itemView.transform.localScale}" );
      report.Append( $"\n  ModPlacer present: {itemView.GetComponent<ModPlacer>() != null}" );
      report.Append( $"\n  ancestry:{DescribeAncestry( bone )}" );
      report.Append( $"\n  layers: item {itemView.layer} ({LayerMask.LayerToName( itemView.layer )}), " +
                    $"bone {bone.gameObject.layer} ({LayerMask.LayerToName( bone.gameObject.layer )}), " +
                    $"host {bone.root.gameObject.layer} ({LayerMask.LayerToName( bone.root.gameObject.layer )})" );

      var renderers = itemView.GetComponentsInChildren<Renderer>( includeInactive: true );
      if( renderers.Length == 0 )
      {
        report.Append( "\n  NO RENDERERS - the prefab loaded but has nothing to draw" );
      }
      else
      {
        var bounds = renderers[0].bounds;
        for( var i = 1; i < renderers.Length; i++ )
          bounds.Encapsulate( renderers[i].bounds );

        var size = bounds.size * 1000f;

        // Relative to the HOST, not the bone: the bone is what we are trying to place, so
        // measuring against it would be circular.
        var host = bone.root;
        var centre = host.InverseTransformPoint( bounds.center ) * 1000f;

        report.Append( $"\n  {renderers.Length} renderer(s), size {size.x:F0} x {size.y:F0} x {size.z:F0} mm" );
        report.Append( $"\n  centre in host space ({centre.x:F0}, {centre.y:F0}, {centre.z:F0}) mm, host = {host.name}" );

        foreach( var renderer in renderers )
          DescribeMaterials( report, renderer );
      }

      Plugin.Log.LogInfo( report.ToString() );
#endif
    }

    /// <summary>
    /// What BSG's scope camera renders against what the player's does. The difference is what this
    /// camera can safely drop.
    /// </summary>
    [Conditional( "COTI_DEV" )]
    public static void ReportCullingMasks( int prefabMask, int mainMask )
    {
#if COTI_DEV
      if( _loggedCullingMasks )
        return;

      _loggedCullingMasks = true;

      Plugin.Log.LogInfo( $"[COTI] culling mask prefab {prefabMask:X8} [{DescribeMask( prefabMask )}]" );
      Plugin.Log.LogInfo( $"[COTI] culling mask main   {mainMask:X8} [{DescribeMask( mainMask )}]" );
      Plugin.Log.LogInfo( $"[COTI] dropped by intersecting [{DescribeMask( mainMask & ~prefabMask )}]" );
#endif
    }

    /// <summary>
    /// The host's transform names and renderer geometry, once per host. Where an anchor bone name for
    /// a new host comes from.
    /// </summary>
    [Conditional( "COTI_DEV" )]
    public static void ReportHostBones( string templateId, Transform root )
    {
#if COTI_DEV
      // AttachMods runs on every item view, so an unguarded line would repeat constantly while the
      // inventory screen is open.
      if( templateId == null || !LoggedHosts.Add( templateId ) )
        return;

      var names = new StringBuilder();
      CollectNames( root, names, depth: 0 );

      if( names.Length == 0 )
        names.Append( " (none - the host mesh is a single object with no child transforms)" );

      Plugin.Log.LogInfo( $"[COTI] Host {templateId} ({root.name}) transforms:{names}" );
      Plugin.Log.LogInfo( $"[COTI] Host {templateId} geometry:{MeasureRenderers( root )}" );
#endif
    }

#if COTI_DEV
    private const float FineDivisorDistance = 4f;
    private const float FineDivisorAngle = 5f;
    private const float FineDivisorScale = 4f;

    private static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> Rotations = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, float> Scales = new Dictionary<string, float>();
    private static readonly Dictionary<string, PoseSnapshot> SeenConfig = new Dictionary<string, PoseSnapshot>();

    /// <summary>
    /// The configured pose a set of deltas is relative to.
    /// </summary>
    private class PoseSnapshot
    {
      public Vector3 Position;
      public Vector3 Rotation;
      public float Scale;

      public bool Matches( PoseSnapshot other )
      {
        return Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;
      }
    }
    private static readonly HashSet<string> LoggedHosts = new HashSet<string>();
    private static readonly HashSet<string> WarnedModifiers = new HashSet<string>();

    private static bool _loggedCullingMasks;
    private static int _probeCount;

    private static string DescribeMask( int mask )
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

    private static Transform _bone;
    private static CotiNvgHostConfig _host;
    private static string _hostId;

    /// <summary>
    /// The host model's own GameObject name, e.g. "nvg_pvs_14(Clone)". Template ids are unreadable,
    /// and with four hosts opened one after another it is genuinely unclear which one the keys are
    /// driving. Taken from the model rather than a hardcoded table, so it cannot go stale.
    /// </summary>
    private static string _hostName;

    private static Vector3 Delta( Dictionary<string, Vector3> deltas, string hostId )
    {
      return deltas.TryGetValue( hostId, out var value ) ? value : Vector3.zero;
    }

    private static float ScaleDelta( string hostId )
    {
      return Scales.TryGetValue( hostId, out var value ) ? value : 0f;
    }

    /// <summary>
    /// A config change means the numbers were baked in, so the deltas that produced them have already
    /// been absorbed and must be dropped. Clearing here rather than asking the tuner to guess keeps
    /// the invariant simple: a delta is always relative to the config currently in force.
    /// </summary>
    private static void ForgetDeltasIfConfigChanged( string hostId, CotiNvgHostConfig host )
    {
      var current = new PoseSnapshot
      {
        Position = host == null
            ? Vector3.zero
            : new Vector3( host.MountPositionX, host.MountPositionY, host.MountPositionZ ),
        Rotation = host == null
            ? Vector3.zero
            : new Vector3( host.MountPitchDegrees, host.MountYawDegrees, host.MountRollDegrees ),
        Scale = host == null ? 1f : host.MountScale
      };

      if( SeenConfig.TryGetValue( hostId, out var seen ) )
      {
        if( seen.Matches( current ) )
          return;

        Positions.Remove( hostId );
        Rotations.Remove( hostId );
        Scales.Remove( hostId );

        Plugin.Log.LogInfo( $"[COTI TUNE] {hostId} config changed - tuning deltas reset to match" );
      }

      SeenConfig[hostId] = current;
    }

    private static string ModifierName()
    {
      var modifier = Plugin.Config == null ? null : Plugin.Config.TunerModifier;
      return string.IsNullOrEmpty( modifier ) ? "no modifier" : modifier;
    }

    private static bool ModifierHeld()
    {
      var modifier = Plugin.Config.TunerModifier;
      if( string.IsNullOrEmpty( modifier ) )
        return true;

      foreach( var part in modifier.Split( '+' ) )
      {
        var name = part.Trim();
        if( name.Length == 0 )
          continue;

        KeyCode key;
        try
        {
          key = (KeyCode)Enum.Parse( typeof( KeyCode ), name, ignoreCase: true );
        }
        catch( Exception )
        {
          if( WarnedModifiers.Add( name ) )
          {
            Plugin.Log.LogWarning( $"[COTI] Tuner modifier '{name}' is not a KeyCode - ignoring that part" );
          }

          continue;
        }

        if( !Input.GetKey( key ) )
          return false;
      }

      return true;
    }

    /// <summary>
    /// The full ancestor chain, with layers and whether any Player component sits on it.
    /// </summary>
    private static string DescribeAncestry( Transform bone )
    {
      var chain = new StringBuilder();
      var depth = 0;

      for( var t = bone; t != null && depth < 12; t = t.parent, depth++ )
      {
        var player = t.GetComponent<Player>();
        chain.Append( $"\n    [{depth}] {t.name} layer={t.gameObject.layer}" +
                     $"({LayerMask.LayerToName( t.gameObject.layer )})" +
                     ( player != null ? $" <- Player, IsYourPlayer={player.IsYourPlayer}" : string.Empty ) );
      }

      return chain.ToString();
    }

    private static void DescribeMaterials( StringBuilder report, Renderer renderer )
    {
      foreach( var material in renderer.materials )
      {
        if( material == null )
        {
          report.Append( $"\n  material on '{renderer.name}': NULL" );
          continue;
        }

        var shader = material.shader;
        report.Append( $"\n  material '{material.name}' on '{renderer.name}' " +
                      $"shader={( shader == null ? "NULL" : shader.name )} " +
                      $"supported={( shader != null && shader.isSupported )} " +
                      $"renderQueue={material.renderQueue}" );

        foreach( var property in new[] { "_MainTex", "_SpecMap", "_BumpMap" } )
        {
          if( !material.HasProperty( property ) )
          {
            report.Append( $"\n    {property}: property ABSENT" );
            continue;
          }

          var texture = material.GetTexture( property );
          report.Append( $"\n    {property} = {( texture == null ? "NULL" : $"{texture.name} {texture.width}x{texture.height}" )}" );
        }
      }
    }

    /// <summary>
    /// Every renderer's size and centre expressed in the HOST'S OWN local space, in millimetres.
    /// This is what a mount pose is derived from: where the host's tubes sit relative to the origin
    /// the COTI gets parented to.
    ///
    /// World-space bounds converted to local, rather than mesh bounds, because that accounts for any
    /// scaling baked into the hierarchy.
    /// </summary>
    private static string MeasureRenderers( Transform root )
    {
      var renderers = root.GetComponentsInChildren<Renderer>( includeInactive: true );
      if( renderers.Length == 0 )
        return " (no renderers)";

      var report = new StringBuilder();

      foreach( var renderer in renderers )
      {
        var centre = root.InverseTransformPoint( renderer.bounds.center ) * 1000f;
        var size = renderer.bounds.size * 1000f;

        report.Append( $"\n  {renderer.name}: size {size.x:F0} x {size.y:F0} x {size.z:F0} mm, " +
                      $"centre ({centre.x:F0}, {centre.y:F0}, {centre.z:F0}) mm" );
      }

      return report.ToString();
    }

    private static void CollectNames( Transform transform, StringBuilder into, int depth )
    {
      // Deep enough to reach mount hardware, shallow enough not to dump every screw: NVG hierarchies
      // bottom out in per-vertex helper objects that are useless as anchors.
      if( depth > 3 )
        return;

      for( var i = 0; i < transform.childCount; i++ )
      {
        var child = transform.GetChild( i );
        into.Append( "\n  " ).Append( ' ', depth * 2 ).Append( child.name );
        CollectNames( child, into, depth + 1 );
      }
    }
#endif
  }
}
