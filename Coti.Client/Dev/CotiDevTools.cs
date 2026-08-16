using System.Diagnostics;
using UnityEngine;

#if COTI_DEV
using System;
using System.Collections.Generic;
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
    /// <summary>Re-applies the pose with this session's tuning deltas on top.</summary>
    [Conditional( "COTI_DEV" )]
    public static void OnMountPosed( Transform bone, CotiNvgHostConfig host, string hostId, string hostName )
    {
#if COTI_DEV
      if( bone == null || hostId == null )
        return;

      if( hostId != _hostId )
      {
        Plugin.Log.LogInfo(
            $"[COTI TUNE] now tuning {hostName} ({hostId}) - {ModifierName()} and the arrows/,./[]/;' keys" );
      }

      _hostId = hostId;
      _hostName = hostName;
      _bone = bone;
      _host = host;

      ForgetDeltasIfConfigChanged( hostId, host );

      CotiMountPose.Apply( bone, host, Delta( Positions, hostId ), Delta( Rotations, hostId ) );
#endif
    }

    [Conditional( "COTI_DEV" )]
    public static void Tick()
    {
#if COTI_DEV
      if( Plugin.Config == null || !Plugin.Config.EnablePoseModifier )
        return;
      if( _bone == null || _hostId == null )
        return;
      if( !ModifierHeld() )
        return;

      var fine = Input.GetKey( KeyCode.LeftShift );
      var move = ( fine ? Plugin.Config.TunerStepMm / FineDivisorDistance : Plugin.Config.TunerStepMm ) / 1000f;
      var turn = fine ? Plugin.Config.TunerStepDegrees / FineDivisorAngle : Plugin.Config.TunerStepDegrees;

      var dp = Vector3.zero;
      var dr = Vector3.zero;

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

      if( dp == Vector3.zero && dr == Vector3.zero )
        return;

      Positions[_hostId] = Delta( Positions, _hostId ) + dp;
      Rotations[_hostId] = Delta( Rotations, _hostId ) + dr;

      CotiMountPose.Apply( _bone, _host, Positions[_hostId], Rotations[_hostId] );

      var rotation = Rotations[_hostId];
      var position = _bone.localPosition;

      Plugin.Log.LogInfo(
          $"[COTI TUNE] {_hostName} {_hostId}  \"mountPositionX\": {position.x:F3}, " +
          $"\"mountPositionY\": {position.y:F3}, \"mountPositionZ\": {position.z:F3}, " +
          $"\"mountRollDegrees\": {( _host == null ? 0f : _host.MountRollDegrees ) + rotation.z:F0}, " +
          $"\"mountPitchDegrees\": {( _host == null ? 0f : _host.MountPitchDegrees ) + rotation.x:F0}, " +
          $"\"mountYawDegrees\": {( _host == null ? 0f : _host.MountYawDegrees ) + rotation.y:F0}" );
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

    private static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> Rotations = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3[]> SeenConfig = new Dictionary<string, Vector3[]>();
    private static readonly HashSet<string> LoggedHosts = new HashSet<string>();
    private static readonly HashSet<string> WarnedModifiers = new HashSet<string>();

    private static bool _loggedCullingMasks;

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

    /// <summary>
    /// A config change means the numbers were baked in, so the deltas that produced them have already
    /// been absorbed and must be dropped. Clearing here rather than asking the tuner to guess keeps
    /// the invariant simple: a delta is always relative to the config currently in force.
    /// </summary>
    private static void ForgetDeltasIfConfigChanged( string hostId, CotiNvgHostConfig host )
    {
      var position = host == null
          ? Vector3.zero
          : new Vector3( host.MountPositionX, host.MountPositionY, host.MountPositionZ );
      var rotation = host == null
          ? Vector3.zero
          : new Vector3( host.MountPitchDegrees, host.MountYawDegrees, host.MountRollDegrees );

      if( SeenConfig.TryGetValue( hostId, out var seen ) )
      {
        if( seen[0] == position && seen[1] == rotation )
          return;

        Positions.Remove( hostId );
        Rotations.Remove( hostId );

        Plugin.Log.LogInfo( $"[COTI TUNE] {hostId} config changed - tuning deltas reset to match" );
      }

      SeenConfig[hostId] = new[] { position, rotation };
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
