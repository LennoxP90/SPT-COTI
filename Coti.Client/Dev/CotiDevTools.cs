using System.Diagnostics;
using UnityEngine;

#if COTI_DEV
using System.Linq;
using System.Text;
using EFT;
#endif

namespace Coti.Client.Dev
{
  /// <summary>
  /// Dev-only diagnostics for adding support for a new night vision device: measure its geometry
  /// and report what EFT does to the model on attach and what the optic/camera pipeline is doing.
  ///
  /// The pose editor itself - tuning deltas, the config-changed reset, the keyboard shortcut and
  /// the on-screen panel - was extracted to release in <see cref="CotiPoseTuner"/> and
  /// <see cref="CotiTunerPanel"/>; it is a first-class feature reached from the inspect window's
  /// COTI Pose button, not a debug tool that needs arming. What is left here is genuinely dev-only:
  /// camera and lens probes with no player-facing use.
  ///
  /// Entry points are [Conditional] on COTI_DEV, defined for Debug only, so Release drops the calls
  /// rather than testing a flag every frame.
  /// </summary>
  public static class CotiDevTools
  {
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
    /// Re-fetches the host table, which is the only way to reach CotiSlotPatcher deliberately.
    ///
    /// The patcher runs when the client's item templates lack a slot the server has, and the client
    /// fetches /client/items once at login - so that gap only opens if the server gains a slot
    /// mid-session. Nothing in the UI can produce it: a publish comes from a host whose slot the
    /// client already has. Re-fetching applies the server's current table with patchSlots on, which
    /// is exactly what a client that had been running through someone else's publish would do.
    ///
    /// Watch for "template(s) patched" in the log: a non-zero count is the patcher working.
    /// </summary>
    private static void TickRefetchKey()
    {
      if( !Input.GetKeyDown( KeyCode.F8 ) )
        return;

      Plugin.Log.LogInfo( "[COTI] F8: re-fetching the host table" );
      CotiHostTableClient.BeginFetch();
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
      TickRefetchKey();
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

      // CotiTunerPreview.DescribeCullingMask, not a private copy here: that method has to compile
      // in Release (it is the primary in-raid check for the preview camera's own mask), so it is
      // the one copy of this layer-name walk that always exists - keeping a second one here would
      // be two independently-evolving implementations of the same loop.
      Plugin.Log.LogInfo( $"[COTI] culling mask prefab {prefabMask:X8} [{CotiTunerPreview.DescribeCullingMask( prefabMask )}]" );
      Plugin.Log.LogInfo( $"[COTI] culling mask main   {mainMask:X8} [{CotiTunerPreview.DescribeCullingMask( mainMask )}]" );
      Plugin.Log.LogInfo( $"[COTI] dropped by intersecting [{CotiTunerPreview.DescribeCullingMask( mainMask & ~prefabMask )}]" );
#endif
    }

#if COTI_DEV
    private static bool _loggedCullingMasks;
    private static int _probeCount;

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
#endif
  }
}
