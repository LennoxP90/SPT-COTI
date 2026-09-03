using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Coti.Shared;
using Newtonsoft.Json;

namespace Coti.Client
{
  /// <summary>
  /// A table waiting to be applied, plus where it came from. The origin travels with the table
  /// because only the server's own table may drive the slot patch, and the drain site cannot tell
  /// the embedded fallback from a fetched one.
  /// </summary>
  public sealed class CotiPendingTable
  {
    public CotiPendingTable( List<CotiDeviceFile> devices, bool fromServer )
    {
      Devices = devices;
      FromServer = fromServer;
    }

    public List<CotiDeviceFile> Devices { get; }

    /// <summary>True only for a table GET /coti/hosts actually returned.</summary>
    public bool FromServer { get; }
  }

  /// <summary>
  /// Fetches the server's host table and hands it to the main thread. Seeded from the embedded
  /// hosts/*.json so an offline client keeps every device this build shipped with.
  ///
  /// Pending has two writers - Awake's seed and the background fetch - so a read-then-null-out
  /// would lose an update with nothing to notice. TakePending is the only accessor.
  /// </summary>
  public static class CotiHostTableClient
  {
    /// <summary>
    /// Matched by substring, so a rename of either half fails as "resource missing" rather than
    /// silently as "no hosts".
    /// </summary>
    private const string EmbeddedHostsMarker = ".Hosts.";

    private static bool _loggedFetchFailure;
    private static bool _itemFactoryRetryDone;

    /// <summary>Two writers. Never read or cleared directly - go through TakePending.</summary>
    public static CotiPendingTable? Pending;

    /// <summary>Atomic read-and-clear, so a fetch landing mid-drain cannot be discarded.</summary>
    public static CotiPendingTable? TakePending()
    {
      return System.Threading.Interlocked.Exchange( ref Pending, null );
    }

    /// <summary>
    /// The last table given to Apply, so the slot half can be re-run once ItemFactory exists.
    /// </summary>
    public static List<CotiDeviceFile>? LastApplied { get; private set; }

    /// <summary>Carried so the retry makes the same call the original did, not a wider one.</summary>
    private static bool _lastAppliedPatchedSlots;

    /// <summary>
    /// Parses every embedded hosts/*.json file into the shared device shape. Never throws: a
    /// single malformed embedded file is logged and skipped, the same way a malformed file on
    /// disk is skipped by CotiDeviceMerge server-side, rather than taking the whole fallback down
    /// over one bad entry.
    /// </summary>
    public static List<CotiDeviceFile> LoadEmbeddedFallback()
    {
      var result = new List<CotiDeviceFile>();
      var assembly = Assembly.GetExecutingAssembly();

      foreach( var name in assembly.GetManifestResourceNames() )
      {
        if( !name.Contains( EmbeddedHostsMarker ) || !name.EndsWith( ".json", StringComparison.Ordinal ) )
          continue;

        try
        {
          using( var stream = assembly.GetManifestResourceStream( name ) )
          using( var reader = new StreamReader( stream ) )
          {
            var dto = JsonConvert.DeserializeObject<CotiDeviceDto>( reader.ReadToEnd() );
            if( dto != null )
              result.Add( dto.ToShared() );
          }
        }
        catch( Exception ex )
        {
          Plugin.Log?.LogError( $"[COTI] Embedded host file {name} failed to parse: {ex.Message}" );
        }
      }

      return result;
    }

    /// <summary>
    /// Fire-and-forget from Awake. RequestHandler.SessionId is parsed from the command line in its
    /// own static constructor, so this needs no wait for a session and no #if - both SPT lines
    /// carry a byte-identical RequestHandler. A failed fetch leaves Pending exactly as Awake left
    /// it (the embedded fallback, applied or about to be) and logs once rather than on every
    /// attempt - there is no retry loop to spam.
    /// </summary>
    public static void BeginFetch()
    {
      System.Threading.Tasks.Task.Run( async () =>
      {
        try
        {
          var json = await SPT.Common.Http.RequestHandler.GetJsonAsync( "/coti/hosts" );
          var table = JsonConvert.DeserializeObject<CotiHostTableDto>( json );

          if( table?.Devices != null )
            Pending = new CotiPendingTable( table.Devices.ConvertAll( d => d.ToShared() ), fromServer: true );
        }
        catch( Exception ex )
        {
          if( !_loggedFetchFailure )
          {
            _loggedFetchFailure = true;
            Plugin.Log?.LogWarning(
                $"[COTI] Could not fetch /coti/hosts - keeping the embedded fallback: {ex.Message}" );
          }
        }
      } );
    }

    /// <summary>
    /// Rewrites Plugin.Config.NvgHosts from the table, and optionally patches slots. NvgHosts only
    /// ever gains entries - a host the server no longer sends keeps its old config rather than
    /// vanishing mid-session.
    /// </summary>
    public static void Apply( List<CotiDeviceFile>? devices, CotiConfig config, bool patchSlots )
    {
      if( devices == null || config == null )
        return;

      LastApplied = devices;
      _lastAppliedPatchedSlots = patchSlots;

      var hostCount = 0;
      var patchedBefore = CotiSlotPatcher.TemplatesPatchedCount;

      foreach( var device in devices )
      {
        if( device?.Hosts == null )
          continue;

        foreach( var host in device.Hosts )
        {
          if( string.IsNullOrEmpty( host?.Id ) )
            continue;

          hostCount++;
          config.NvgHosts[host!.Id!] = ToHostConfig( device );

          if( patchSlots )
            CotiSlotPatcher.EnsureSlot( host.Id! );
        }
      }

      var patchedThisCall = CotiSlotPatcher.TemplatesPatchedCount - patchedBefore;
      Plugin.Log?.LogInfo( patchSlots
          ? $"[COTI] host table applied: {hostCount} host(s), {patchedThisCall} template(s) patched"
          : $"[COTI] host table applied: {hostCount} host(s), slot patching skipped " +
              "(offline fallback, not the server's table)" );

#if SPT41
      LogPoses( config );
      CotiPoseTuner.OnHostTableReapplied();
#endif
    }

#if SPT41
    /// <summary>
    /// The pose each host resolves to, logged from the menu. Runs on every apply, so a pushed
    /// table shows its new numbers here.
    /// </summary>
    private static void LogPoses( CotiConfig config )
    {
      foreach( var entry in config.NvgHosts )
      {
        var host = entry.Value;
        if( host == null )
          continue;

        var pose = CotiMountTransform.Compute( host.ToMountBlock() );

        Plugin.Log?.LogInfo(
            $"[COTI] pose {entry.Key} ({host.MaskName ?? "?"}) " +
            $"pos=({pose.Position.X:F4}, {pose.Position.Y:F4}, {pose.Position.Z:F4}) " +
            $"quat=({pose.Rotation.X:F5}, {pose.Rotation.Y:F5}, {pose.Rotation.Z:F5}, {pose.Rotation.W:F5}) " +
            $"scale={pose.Scale:F4} " +
            $"mask=({host.MaskCenterX:F4}, {host.MaskCenterY:F4}) r={host.MaskRadius:F4}" );
      }
    }
#endif

    // ItemFactory cannot exist before /client/items is parsed, so the first Apply call cannot
    // patch templates. Retried once the singleton appears.
    public static void RetrySlotPassOnceItemFactoryIsReady()
    {
      if( _itemFactoryRetryDone || !CotiSlotPatcher.ItemFactoryReady || LastApplied == null )
        return;

      _itemFactoryRetryDone = true;
      Apply( LastApplied, Plugin.Config, _lastAppliedPatchedSlots );
    }

    // MaskName is the device's own name - a label for logs, not a mask selector.
    public static CotiNvgHostConfig ToHostConfig( CotiDeviceFile device )
    {
      var mask = device.Mask ?? new CotiMaskBlock();
      var mount = device.Mount ?? new CotiMountBlock();

      return new CotiNvgHostConfig
      {
        MaskName = device.Device,
        MaskCenterX = mask.CenterX,
        MaskCenterY = mask.CenterY,
        MaskRadius = mask.Radius,
        MaskFeather = mask.Feather,
        MountAnchorBone = mount.AnchorBone,
        MountPositionX = mount.PositionX,
        MountPositionY = mount.PositionY,
        MountPositionZ = mount.PositionZ,
        MountRotationX = mount.RotationX,
        MountRotationY = mount.RotationY,
        MountRotationZ = mount.RotationZ,
        MountRollDegrees = mount.RollDegrees,
        MountPitchDegrees = mount.PitchDegrees,
        MountYawDegrees = mount.YawDegrees,
        MountScale = mount.Scale,
      };
    }
  }
}
