using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;

namespace Coti.Server;

/// <summary>
/// The one path a device file is written by. The in-game tuner reaches it through
/// /coti/hosts/publish and the web editor calls it directly.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiDevicePublisher(
    ISptLogger<CotiDevicePublisher> logger, CotiDeviceStore deviceStore, CotiSlotInjector slotInjector
#if SPT41
    , CotiHostSocket hostSocket
#endif
    )
{
  /// <param name="publishedBy">Who to name in the log.</param>
  public CotiPublishResultDto Publish( CotiDeviceFile device, string publishedBy )
  {
    // The shared merge path over a list of one, applying every per-file rule CotiDeviceMerge
    // enforces: current schema, non-blank device and displayName, a positive mask radius, non-null
    // hosts and mount. Cross-file conflicts are caught by TryWrite's own Reload below, which
    // re-reads every file on disk together. A republish under the same device name is an update,
    // backed by the .bak TryWrite takes.
    var merged = CotiDeviceMerge.Merge( new[] { new CotiParsedFile { Path = "<published>", Device = device } } );

    if( merged.Devices.Count == 0 )
    {
      var reason = merged.Warnings.FirstOrDefault() ?? "rejected by validation";
      logger.Warning( $"[COTI] {publishedBy} tried to publish \"{device.Device}\" - rejected: {reason}" );
      return new CotiPublishResultDto { Ok = false, Error = reason };
    }

    if( !deviceStore.TryWrite( device, out var writeError ) )
    {
      logger.Warning( $"[COTI] {publishedBy} tried to publish \"{device.Device}\" - could not write: {writeError}" );
      return new CotiPublishResultDto { Ok = false, Error = writeError };
    }

    // Every host the published device declares. InjectInto is idempotent and logs its own
    // detail; the outcome is tracked here only to report the failing hosts back to the caller.
    var unfitHosts = new List<string>();

    foreach( var host in device.Hosts )
    {
      if( host?.Id == null )
        continue;

      var outcome = slotInjector.InjectInto( host.Id, device.DisplayName ?? device.Device ?? host.Id );

      if( outcome == CotiInjectOutcome.InvalidId || outcome == CotiInjectOutcome.NoSlotsCollection )
        unfitHosts.Add( $"{host.Id}: {outcome}" );
    }

    if( unfitHosts.Count > 0 )
    {
      logger.Warning(
          $"[COTI] {publishedBy}'s publish of \"{device.Device}\" wrote the file but could not fit " +
          $"{unfitHosts.Count} host(s) - see the CotiSlotInjector line(s) above for why: " +
          string.Join( ", ", unfitHosts ) );
    }

    // Leaves the store as fresh as a restart would.
    deviceStore.Reload();

    logger.Success(
        $"[COTI] {publishedBy} published \"{device.Device}\", covering {device.Hosts.Count} host(s)" );

#if SPT41
    // Sent after the reload; a re-fetch then reads the new table.
    hostSocket.NotifyHostsChanged();
#endif

    return new CotiPublishResultDto
    {
      Ok = true,
      Device = CotiDeviceDto.FromShared( device ),
      UnfitHosts = unfitHosts,
    };
  }
}
