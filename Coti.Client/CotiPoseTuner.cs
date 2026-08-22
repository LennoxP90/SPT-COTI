using System;
using System.Collections.Generic;
using System.Text;
using Coti.Shared;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The pose editor's model: per-host deltas, the nudge operations, and the keyboard shortcut.
  /// Kept free of rendering so it is testable; CotiTunerPanel draws and CotiTunerPreview renders.
  /// </summary>
  public static class CotiPoseTuner
  {
    // Public so the panel uses the same per-axis divisor as the keyboard shortcut.
    public const float FineDivisorDistance = 4f;
    public const float FineDivisorAngle = 5f;
    public const float FineDivisorScale = 4f;

    private static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, Vector3> Rotations = new Dictionary<string, Vector3>();
    private static readonly Dictionary<string, float> Scales = new Dictionary<string, float>();
    private static readonly Dictionary<string, string> AnchorOverrides = new Dictionary<string, string>();
    private static readonly Dictionary<string, PoseSnapshot> SeenConfig = new Dictionary<string, PoseSnapshot>();
    private static readonly HashSet<string> WarnedModifiers = new HashSet<string>();
    private static readonly HashSet<string> LoggedHosts = new HashSet<string>();

    /// <summary>
    /// Transform names and the CurveRotator's suggestion, per host, captured once - the mesh
    /// hierarchy does not change between mounts.
    /// </summary>
    private static readonly Dictionary<string, List<string>> BoneNamesByHost = new Dictionary<string, List<string>>();

    /// <summary>
    /// null = no CurveRotator; "" = it spins the host root; otherwise the child transform name.
    /// </summary>
    private static readonly Dictionary<string, string> SuggestedBoneByHost = new Dictionary<string, string>();

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

    private static Transform _bone;
    private static CotiNvgHostConfig _host;
    private static string _hostId;

    /// <summary>
    /// Re-resolved per OnMountPosed: a cached component from a pooled-away GameObject is not
    /// necessarily still the right one.
    /// </summary>
    private static CurveRotator _rotator;

    /// <summary>What the tuner panel's footer shows after a Publish click - see Publish().</summary>
    private static string _lastPublishNote;

    /// <summary>
    /// The host's own root, NOT _bone.root - that would be the player root in a raid, so bounds
    /// would measure the whole player every frame the header draws.
    /// </summary>
    private static Transform _hostRoot;

    /// <summary>
    /// The model's GameObject name. Template ids are unreadable when switching between hosts.
    /// </summary>
    private static string _hostName;

    /// <summary>
    /// The host the editor is showing. Separate from _hostId, which tracks whatever mounted last;
    /// the two are expected to agree but only the live re-application needs them to.
    /// </summary>
    private static Item _openHost;

    public static bool IsOpen { get; private set; }

    public static void Install()
    {
      CotiInspectButton.OpenRequested += OnOpenRequested;
    }

    private static void OnOpenRequested( Item host )
    {
      if( host == null )
        return;

      _openHost = host;
      IsOpen = true;

      var hostId = host.StringTemplateId;
      if( hostId != null && hostId != _hostId )
      {
        // Not fatal - the dictionaries below are keyed by hostId, so the numbers this panel shows
        // are still correct. Only the "watch it move live" feedback is unavailable until this host
        // mounts again, which OnMountPosed will report on its own the moment it does.
        Plugin.Log.LogWarning(
            $"[COTI TUNE] pose editor opened for {hostId} but the last mounted host was " +
            $"{_hostId ?? "(none)"} - nudges will not be visible until this host mounts again." );
      }
    }

    public static void Close()
    {
      IsOpen = false;
    }

    public static string OpenHostId => _openHost?.StringTemplateId;

    public static string OpenHostName
    {
      get
      {
        if( OpenHostId != null && OpenHostId == _hostId && _hostName != null )
          return _hostName;

        return OpenHostId ?? "(none)";
      }
    }

    public static CotiNvgHostConfig OpenHostConfig
    {
      get
      {
        var hostId = OpenHostId;
        if( hostId == null || Plugin.Config == null )
          return null;

        CotiNvgHostConfig config;
        return Plugin.Config.NvgHosts.TryGetValue( hostId, out config ) ? config : null;
      }
    }

    /// <summary>
    /// Whether the open host has a COTI fitted, which is what gates the editor button.
    /// </summary>
    internal static bool OpenHostCotiAttached => CotiSlotProbe.IsCotiAttached( _openHost );

    // ---- Anchor bone selection and the flip test ----------------------------------------------

    /// <summary>
    /// The anchor this session would publish: the override if one was picked, otherwise the
    /// device file's own value.
    /// </summary>
    public static string AnchorBoneLabel => FormatAnchorBone( PendingAnchorBone );

    /// <summary>
    /// null when this host has never reported a CurveRotator at all (either it has none, or it
    /// has not been viewed yet this session) - see SuggestedAnchorBone. Formatted the same way
    /// AnchorBoneLabel is, so "(host root)" always means the same thing on screen.
    /// </summary>
    public static string SuggestedAnchorBoneLabel
    {
      get
      {
        var suggested = SuggestedAnchorBone;
        return suggested == null ? null : FormatAnchorBone( suggested );
      }
    }

    private static string FormatAnchorBone( string name )
    {
      return string.IsNullOrEmpty( name ) ? "(host root)" : name;
    }

    /// <summary>
    /// This session's override for the open host, or the saved value if there is none. "" means
    /// the host root, matching the on-disk convention.
    /// </summary>
    public static string PendingAnchorBone
    {
      get
      {
        var hostId = OpenHostId;
        string over;
        if( hostId != null && AnchorOverrides.TryGetValue( hostId, out over ) )
          return over;

        return Saved?.Mount?.AnchorBone ?? string.Empty;
      }
    }

    /// <summary>
    /// null = no CurveRotator ever seen on this host (see ReportHostBones); "" = it spins the
    /// host's own root; anything else = the child transform name it spins.
    /// </summary>
    public static string SuggestedAnchorBone
    {
      get
      {
        var hostId = OpenHostId;
        string suggested;
        return hostId != null && SuggestedBoneByHost.TryGetValue( hostId, out suggested ) ? suggested : null;
      }
    }

    /// <summary>
    /// The host root ("") followed by every transform name found, with the suggestion guaranteed
    /// present so it can always be selected.
    /// </summary>
    public static List<string> AnchorBoneCandidates
    {
      get
      {
        var candidates = new List<string> { string.Empty };

        var hostId = OpenHostId;
        List<string> names;
        if( hostId != null && BoneNamesByHost.TryGetValue( hostId, out names ) )
          candidates.AddRange( names );

        return CotiAnchorAdvisor.EnsureSuggestedIsCandidate( candidates, SuggestedAnchorBone );
      }
    }

    /// <summary>
    /// Moves the pending anchor to the next (or, with a negative direction, previous) candidate.
    /// Offered, not forced - this only ever changes what cycling shows and what the flip test
    /// exercises; nothing is written to disk until Publish.
    /// </summary>
    public static void CycleAnchorBone( int direction )
    {
      var hostId = OpenHostId;
      if( hostId == null )
        return;

      var candidates = AnchorBoneCandidates;
      var current = PendingAnchorBone;
      var index = candidates.FindIndex( name => string.Equals( name, current, StringComparison.OrdinalIgnoreCase ) );

      SetAnchorBone( candidates[CotiAnchorAdvisor.NextCandidateIndex( index, candidates.Count, direction )] );
    }

    /// <summary>
    /// Jumps straight to whatever the CurveRotator suggested, if anything did. A human still has
    /// to click this - nothing here applies the suggestion on its own.
    /// </summary>
    public static void UseSuggestedAnchorBone()
    {
      var suggested = SuggestedAnchorBone;
      if( suggested != null )
        SetAnchorBone( suggested );
    }

    /// <summary>
    /// Records the override and re-parents the live bone now. Writing MountAnchorBone alone only
    /// takes effect on the next AttachMods pass, which an open inspect window never triggers.
    /// </summary>
    private static void SetAnchorBone( string name )
    {
      var hostId = OpenHostId;
      if( hostId == null )
        return;

      AnchorOverrides[hostId] = name ?? string.Empty;

      // OnMountPosed's own ApplyAnchorOverride call covers the other case, where the override was
      // made before this host ever mounted.
      if( _hostId != hostId )
        return;

      ApplyAnchorOverride( hostId, _host );
      ReparentLiveBone();
    }

    /// <summary>
    /// What an AttachMods pass would have done. The pose is relative to the anchor, so it has to
    /// be re-applied against the new parent.
    /// </summary>
    private static void ReparentLiveBone()
    {
      if( _bone == null || _host == null )
        return;

      var root = LiveHostRoot;
      if( root == null )
        return;

      var anchor = Patches.CotiMountBonePatch.ResolveAnchor( root, _host );
      if( anchor == null )
        return;

      _bone.transform.SetParent( anchor, worldPositionStays: false );

      // The pose is expressed relative to the anchor, so it has to be re-applied against the new
      // parent - otherwise the COTI keeps the offsets it had under the old one and lands somewhere
      // that looks like the anchor change did the wrong thing rather than nothing.
      CotiMountPose.Apply( _bone, _host, Delta( Positions, _hostId ), Delta( Rotations, _hostId ),
          ScaleDelta( _hostId ) );
    }

    private static void ApplyAnchorOverride( string hostId, CotiNvgHostConfig host )
    {
      string over;
      if( host != null && AnchorOverrides.TryGetValue( hostId, out over ) )
        host.MountAnchorBone = over.Length == 0 ? null : over;
    }

    /// <summary>
    /// The live CurveRotator, only while this host is the one currently mounted - same gate as
    /// LiveHostRoot below, and for the same reason: a rotator resolved for whichever host mounted
    /// last is not this one's flip hardware.
    /// </summary>
    private static CurveRotator LiveRotator => _rotator != null && _hostId == OpenHostId ? _rotator : null;

    /// <summary>
    /// Null exactly when the flip test buttons should be enabled. Two distinct disabled reasons,
    /// not one: a host that has simply never been viewed live this session ("model not in scene")
    /// is a different situation from a host that HAS been viewed and genuinely carries no
    /// CurveRotator ("no flip hardware") - conflating them would make a perfectly flip-capable
    /// host that just is not mounted right now look like it can never flip at all.
    /// </summary>
    public static string FlipUnavailableReason
    {
      get
      {
        if( LiveRotator != null )
          return null;

        if( _hostId != OpenHostId )
          return "model not in scene - open this device's inventory view once to detect flip hardware";

        return "this host has no flip hardware - the goggle tube never rotates, so any anchor bone behaves the same";
      }
    }

    /// <summary>
    /// Jumps to the end pose. CurveRotator's flag means DEPLOYED, and a deployed goggle is rotated
    /// DOWN - passing "up = true" through reverses both buttons.
    /// </summary>
    public static void FlipSnap( bool deployed )
    {
      var rotator = LiveRotator;
      if( rotator != null )
        rotator.Set( deployed, true );
    }

    /// <summary>
    /// Set(isOn, initial: false) - animates at the prefab's own RotationSpeed, which is what lets
    /// a pose that is correct at both extremes but sweeps through the host mid-flip be caught; a
    /// snap test alone would show both ends looking fine and miss it.
    /// </summary>
    public static void FlipAnimate( bool deployed )
    {
      var rotator = LiveRotator;
      if( rotator != null )
        rotator.Set( deployed, false );
    }

    public static string LastPublishNote => _lastPublishNote;

    // ---- Bounds measurement --------------------------------------------------------------------

    /// <summary>
    /// The host's own root, only while it is the open host - a root left over from whatever mounted
    /// last is not this one's.
    /// </summary>
    internal static Transform LiveHostRoot => _hostRoot != null && _hostId == OpenHostId ? _hostRoot : null;

    public static string MeasuredBoundsLabel
    {
      get
      {
        var bounds = MeasureBounds( LiveHostRoot );
        if( bounds == null )
          return LiveHostRoot == null ? "(model not in scene)" : "(no renderers)";

        var sizeMm = bounds.Value.size * 1000f;
        return $"{sizeMm.x:F0} x {sizeMm.y:F0} x {sizeMm.z:F0} mm";
      }
    }

    /// <summary>
    /// The live host's own world-space bounds - the same measurement <see cref="MeasuredBoundsLabel"/>
    /// is built from, so the number on screen and the preview camera's frame-the-COTI button can
    /// never disagree about what "the bounds" are. Null under the same conditions the label falls
    /// back to text for: no live model, or a live model with no renderers.
    /// </summary>
    internal static Bounds? LiveHostBounds => MeasureBounds( LiveHostRoot );

    private static Bounds? MeasureBounds( Transform root )
    {
      if( root == null )
        return null;

      var renderers = root.GetComponentsInChildren<Renderer>( includeInactive: true );
      if( renderers.Length == 0 )
        return null;

      var bounds = renderers[0].bounds;
      for( var i = 1; i < renderers.Length; i++ )
        bounds.Encapsulate( renderers[i].bounds );

      return bounds;
    }

    // ---- Live data for the panel ------------------------------------------------------------

    /// <summary>
    /// The persisted device file the currently open host belongs to - the embedded fallback until
    /// a fetch lands, then whatever <see cref="CotiHostTableClient"/> last applied. Null means the
    /// open host is not (yet) covered by any device the client knows about.
    /// </summary>
    public static CotiDeviceFile Saved => FindDevice( OpenHostId );

    /// <summary>
    /// Saved with this session's tuning deltas folded in - the pose the host is actually wearing
    /// right now (or would be, if it were mounted live). This is what Publish would write.
    /// </summary>
    public static CotiDeviceFile Current => Bake( Saved, OpenHostId );

    private static CotiDeviceFile FindDevice( string hostId )
    {
      return CotiDeviceLookup.ByHostId( CotiHostTableClient.LastApplied, hostId );
    }

    private static CotiDeviceFile Bake( CotiDeviceFile saved, string hostId )
    {
      if( saved == null || hostId == null )
        return null;

      var position = Delta( Positions, hostId );
      var rotation = Delta( Rotations, hostId );
      var scale = ScaleDelta( hostId );
      var mount = saved.Mount ?? new CotiMountBlock();

      string anchorOverride;
      var anchorBone = AnchorOverrides.TryGetValue( hostId, out anchorOverride )
          ? ( anchorOverride.Length == 0 ? null : anchorOverride )
          : mount.AnchorBone;

      return new CotiDeviceFile
      {
        Schema = saved.Schema,
        Device = saved.Device,
        DisplayName = saved.DisplayName,
        Requires = saved.Requires,
        Tuned = saved.Tuned,
        Hosts = saved.Hosts,
        Mask = saved.Mask,
        Mount = new CotiMountBlock
        {
          AnchorBone = anchorBone,
          PositionX = mount.PositionX + position.x,
          PositionY = mount.PositionY + position.y,
          PositionZ = mount.PositionZ + position.z,
          RotationX = mount.RotationX,
          RotationY = mount.RotationY,
          RotationZ = mount.RotationZ,
          RollDegrees = mount.RollDegrees + rotation.z,
          PitchDegrees = mount.PitchDegrees + rotation.x,
          YawDegrees = mount.YawDegrees + rotation.y,
          // Not clamped to CotiMountPose's MinimumScale - this is a readout of what Publish would
          // write, and clamping is a rendering safety net, not something the panel should hide.
          Scale = mount.Scale + scale,
        }
      };
    }

    // ---- Nudging -----------------------------------------------------------------------------

    /// <summary>axis 0 = X (right), 1 = Y (up), 2 = Z (forward). metres, added to this session's delta.</summary>
    public static void NudgePosition( int axis, float metres )
    {
      var hostId = OpenHostId;
      if( hostId == null || metres == 0f )
        return;

      var delta = Delta( Positions, hostId );
      delta[axis] += metres;
      Positions[hostId] = delta;

      ReapplyIfLive( hostId );
    }

    /// <summary>axis 0 = pitch, 1 = yaw, 2 = roll. degrees, added to this session's delta.</summary>
    public static void NudgeRotation( int axis, float degrees )
    {
      var hostId = OpenHostId;
      if( hostId == null || degrees == 0f )
        return;

      var delta = Delta( Rotations, hostId );
      delta[axis] += degrees;
      Rotations[hostId] = delta;

      ReapplyIfLive( hostId );
    }

    public static void NudgeScale( float amount )
    {
      var hostId = OpenHostId;
      if( hostId == null || amount == 0f )
        return;

      Scales[hostId] = ScaleDelta( hostId ) + amount;

      ReapplyIfLive( hostId );
    }

    /// <summary>
    /// Drops this session's deltas for the open host, without waiting for a config change to do it -
    /// the manual equivalent of <see cref="ForgetDeltasIfConfigChanged"/>, for when the saved pose
    /// has not changed but the player just wants their nudges back out.
    /// </summary>
    public static void Reset()
    {
      var hostId = OpenHostId;
      if( hostId == null )
        return;

      Positions.Remove( hostId );
      Rotations.Remove( hostId );
      Scales.Remove( hostId );

      if( AnchorOverrides.Remove( hostId ) && _hostId == hostId && _host != null )
        _host.MountAnchorBone = Saved?.Mount?.AnchorBone;

      _lastPublishNote = null;

      ReapplyIfLive( hostId );
    }

    /// <summary>
    /// Sends this session's pose and clears its deltas. Its own button, never a side effect of
    /// nudging - it is the one action here that writes to disk. PostJson blocks, which is
    /// acceptable only because this runs from a deliberate click.
    /// </summary>
    public static void Publish()
    {
      var hostId = OpenHostId;
      var saved = Saved;
      var current = Current;
      if( hostId == null || saved == null || current == null )
        return;

      var device = new CotiDeviceFile
      {
        Schema = saved.Schema,
        Device = saved.Device,
        DisplayName = saved.DisplayName,
        Requires = saved.Requires,
        Tuned = true,
        Hosts = saved.Hosts,
        Mask = saved.Mask,
        Mount = current.Mount,
      };

      if( !SendPublish( device, hostId, OpenHostName ) )
        return;

      Positions.Remove( hostId );
      Rotations.Remove( hostId );
      Scales.Remove( hostId );
      AnchorOverrides.Remove( hostId );

      ReapplyIfLive( hostId );
    }

    /// <summary>
    /// Sends a new mask with the mount the server already holds - the mirror of Publish. Neither
    /// editor can overwrite the other's field. An uncommitted mount delta is not folded in.
    /// </summary>
    public static bool PublishMask( string hostId, CotiMaskBlock mask )
    {
      if( hostId == null || mask == null )
        return false;

      var saved = FindDevice( hostId );
      if( saved?.Mount == null )
        return false;

      var device = new CotiDeviceFile
      {
        Schema = saved.Schema,
        Device = saved.Device,
        DisplayName = saved.DisplayName,
        Requires = saved.Requires,
        Tuned = true,
        Hosts = saved.Hosts,
        Mask = mask,
        Mount = saved.Mount,
      };

      return SendPublish( device, hostId, saved.DisplayName ?? saved.Device ?? hostId );
    }

    /// <summary>
    /// Serialise, POST, fold an accepted device back into the live table. Shared by both editors so
    /// the "Ok can be true while nothing was fitted" rule below has one home.
    /// </summary>
    private static bool SendPublish( CotiDeviceFile device, string hostId, string hostName )
    {
      // Rounded here so every publish path gets it. A NEW block, because PublishMask passes the live
      // table's own mount straight through.
      if( device?.Mount != null )
        device.Mount = CotiMountRounding.Round( device.Mount );

      CotiPublishResultDto result;
      try
      {
        var json = JsonConvert.SerializeObject( CotiDeviceDto.FromShared( device ) );
        var responseJson = SPT.Common.Http.RequestHandler.PostJson( "/coti/hosts/publish", json );
        result = JsonConvert.DeserializeObject<CotiPublishResultDto>( responseJson );
      }
      catch( Exception ex )
      {
        result = new CotiPublishResultDto { Ok = false, Error = ex.Message };
      }

      // Ok stays true even when the file was written but a host could not be fitted (InvalidId or
      // NoSlotsCollection) - see CotiPublishReport's own comment. Checking Ok alone here would
      // report a clean success while nothing was actually fitted.
      _lastPublishNote = CotiPublishReport.Describe(
          result != null && result.Ok, result?.Error, result?.UnfitHosts );

      if( result == null || !result.Ok )
      {
        Plugin.Log.LogWarning(
            $"[COTI TUNE] publish of {hostName} {hostId} failed: {result?.Error ?? "no response"}" );
        return false;
      }

      var published = ( result.Device ?? CotiDeviceDto.FromShared( device ) ).ToShared();

      ApplyPublishedDevice( published );

      var unfitHosts = result.UnfitHosts ?? new List<string>();
      Plugin.Log.LogInfo( unfitHosts.Count == 0
          ? $"[COTI TUNE] {hostName} {hostId} published to the server"
          : $"[COTI TUNE] {hostName} {hostId} published to the server - " +
              $"{unfitHosts.Count} host(s) not fitted: {string.Join( ", ", unfitHosts )}" );

      return true;
    }

    /// <summary>
    /// Folds the published device into the live table and re-runs Apply, so every host it declares
    /// gets the new pose - not just the one the panel had open. Matched by device name, so a
    /// republish updates in place instead of appending a duplicate.
    /// </summary>
    private static void ApplyPublishedDevice( CotiDeviceFile published )
    {
      var existingDevices = CotiHostTableClient.LastApplied ?? new List<CotiDeviceFile>();
      var next = new List<CotiDeviceFile>( existingDevices.Count + 1 );
      var replaced = false;

      foreach( var existing in existingDevices )
      {
        if( existing != null && existing.Device == published.Device )
        {
          next.Add( published );
          replaced = true;
        }
        else
        {
          next.Add( existing );
        }
      }

      if( !replaced )
        next.Add( published );

      // patchSlots: the server accepted this publish, so its own InjectInto pass has just run over
      // every host the device declares - this is exactly the mid-session divergence
      // CotiSlotPatcher exists to close, and the one case where the client is entitled to add the
      // slot itself rather than wait for a relaunch.
      CotiHostTableClient.Apply( next, Plugin.Config, patchSlots: true );

      CotiNvgHostConfig liveHost;
      if( _hostId != null && Plugin.Config != null && Plugin.Config.NvgHosts.TryGetValue( _hostId, out liveHost ) )
        _host = liveHost;

      // Records the just-published pose as the new baseline for every host the device declares,
      // so the next mount pass for any of them does not mistake this publish for an external
      // config change and log a spurious "config changed" line.
      foreach( var hostRef in published.Hosts )
      {
        CotiNvgHostConfig refreshed;
        if( hostRef?.Id != null && Plugin.Config != null && Plugin.Config.NvgHosts.TryGetValue( hostRef.Id, out refreshed ) )
          SeenConfig[hostRef.Id] = SnapshotOf( refreshed );
      }
    }

    private static void ReapplyIfLive( string hostId )
    {
      if( _bone == null || _hostId != hostId )
        return;

      CotiMountPose.Apply( _bone, _host, Delta( Positions, hostId ), Delta( Rotations, hostId ), ScaleDelta( hostId ) );
    }

    // ---- Called from the mount patch, on every AttachMods pass --------------------------------

    /// <summary>
    /// Re-applies the pose with this session's deltas, for the host currently mounted.
    /// </summary>
    public static void OnMountPosed(
        Transform bone, CotiNvgHostConfig host, string hostId, string hostName, Transform hostRoot )
    {
      if( bone == null || hostId == null )
        return;

      if( hostId != _hostId )
      {
        Plugin.Log.LogInfo(
            $"[COTI TUNE] now tuning {hostName} ({hostId}) - open the COTI Pose button on its " +
            $"inspect window, or hold {ModifierName()} and use the arrows/,./[]/;'/-= keys" );
      }

      _hostId = hostId;
      _hostName = hostName;
      _bone = bone;
      _host = host;
      _hostRoot = hostRoot;

      // Item views come from an object pool, so a cached component reference may belong to a
      // GameObject that has since been reused.
      _rotator = hostRoot == null ? null : hostRoot.GetComponentInChildren<CurveRotator>( true );

      // Re-applied every pass for the same reason Positions/Rotations/Scales are re-read via
      // Delta() below rather than applied once: an override made while this host was NOT the one
      // live (SetAnchorBone's own direct write only reaches _host when _hostId already matches)
      // must still take effect the moment it actually mounts, not be silently lost.
      ApplyAnchorOverride( hostId, host );

      ForgetDeltasIfConfigChanged( hostId, host );

      CotiMountPose.Apply( bone, host, Delta( Positions, hostId ), Delta( Rotations, hostId ), ScaleDelta( hostId ) );
    }

    /// <summary>
    /// Logs the host's transform names, geometry and flip axis once per host - the mesh does not
    /// change between mounts.
    /// </summary>
    public static void ReportHostBones( string templateId, Transform root )
    {
      if( templateId == null || root == null )
        return;

      if( !BoneNamesByHost.ContainsKey( templateId ) )
      {
        var names = new List<string>();
        CollectNameList( root, names, depth: 0 );
        BoneNamesByHost[templateId] = names;
        SuggestedBoneByHost[templateId] = ResolveSuggestedBone( root );
      }

      // AttachMods runs on every item view, so an unguarded line below would repeat constantly
      // while the inventory screen is open - the capture above is cheap and per-host-once
      // regardless, but the verbose report is only worth writing to the log the first time.
      if( !LoggedHosts.Add( templateId ) )
        return;

      var report = new StringBuilder();
      CollectNames( root, report, depth: 0 );

      if( report.Length == 0 )
        report.Append( " (none - the host mesh is a single object with no child transforms)" );

      Plugin.Log.LogInfo( $"[COTI] Host {templateId} ({root.name}) transforms:{report}" );
      Plugin.Log.LogInfo( $"[COTI] Host {templateId} geometry:{MeasureRenderers( root )}" );

      var suggested = SuggestedBoneByHost[templateId];
      Plugin.Log.LogInfo( suggested == null
          ? $"[COTI] Host {templateId} has no CurveRotator - it does not flip, so any anchor bone behaves the same"
          : $"[COTI] Host {templateId} flip axis suggests anchor bone "
              + ( suggested.Length == 0 ? "(host root)" : $"'{suggested}'" ) );
    }

    /// <summary>
    /// CurveRotator.RotatedTransform is the transform that rotates when the goggle flips, so for a
    /// flip-capable host it is the correct anchor - which is what the GPNVG-18's "axis" already was.
    /// </summary>
    private static string ResolveSuggestedBone( Transform root )
    {
      var rotator = root.GetComponentInChildren<CurveRotator>( true );
      var rotatedTransform = rotator == null ? null : rotator.RotatedTransform;

      return CotiAnchorAdvisor.SuggestAnchorBone(
          rotator != null,
          rotatedTransform != null && rotatedTransform == root,
          rotatedTransform == null ? null : rotatedTransform.name );
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

    /// <summary>
    /// Transform names for the anchor picker, de-duplicated and depth-limited the same way the
    /// diagnostic walk is.
    /// </summary>
    private static void CollectNameList( Transform transform, List<string> into, int depth )
    {
      if( depth > 3 )
        return;

      for( var i = 0; i < transform.childCount; i++ )
      {
        var child = transform.GetChild( i );
        if( !into.Exists( n => string.Equals( n, child.name, StringComparison.OrdinalIgnoreCase ) ) )
          into.Add( child.name );

        CollectNameList( child, into, depth + 1 );
      }
    }

    /// <summary>
    /// Every renderer's size and centre in the host's own local space, in millimetres - which is
    /// what a mount pose is measured against. World bounds converted to local, so any scaling baked
    /// into the hierarchy is accounted for.
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

    // ---- The keyboard shortcut, unchanged from before promotion --------------------------------

    /// <summary>
    /// The arrow-key nudge, arming and step sizes all as before - the pose editor's on-screen
    /// buttons are an additional input surface over the same deltas, not a replacement for this
    /// one. Gated on EnablePoseModifier same as always: the editor's own buttons need no such gate,
    /// since clicking one is already a deliberate action, but these keys are otherwise free ones
    /// EFT does not bind, and firing on every keypress without an opt-in would be a surprise.
    /// </summary>
    public static void Tick()
    {
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
    }

    // ---- Shared delta bookkeeping ---------------------------------------------------------------

    private static Vector3 Delta( Dictionary<string, Vector3> deltas, string hostId )
    {
      if( hostId == null )
        return Vector3.zero;

      Vector3 value;
      return deltas.TryGetValue( hostId, out value ) ? value : Vector3.zero;
    }

    private static float ScaleDelta( string hostId )
    {
      if( hostId == null )
        return 0f;

      float value;
      return Scales.TryGetValue( hostId, out value ) ? value : 0f;
    }

    private static PoseSnapshot SnapshotOf( CotiNvgHostConfig host )
    {
      return new PoseSnapshot
      {
        Position = host == null
            ? Vector3.zero
            : new Vector3( host.MountPositionX, host.MountPositionY, host.MountPositionZ ),
        Rotation = host == null
            ? Vector3.zero
            : new Vector3( host.MountPitchDegrees, host.MountYawDegrees, host.MountRollDegrees ),
        Scale = host == null ? 1f : host.MountScale
      };
    }

    /// <summary>
    /// A config change means the numbers were baked in, so the deltas that produced them have
    /// already been absorbed and must be dropped. Clearing here rather than asking the tuner to
    /// guess keeps the invariant simple: a delta is always relative to the config currently in
    /// force.
    /// </summary>
    private static void ForgetDeltasIfConfigChanged( string hostId, CotiNvgHostConfig host )
    {
      var current = SnapshotOf( host );

      PoseSnapshot seen;
      if( SeenConfig.TryGetValue( hostId, out seen ) )
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
  }
}
