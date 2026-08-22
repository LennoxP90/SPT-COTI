using Coti.Shared;
using System.Linq;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;
#if !SPT40
using SPTarkov.Server.Core.Models.Utils; // IRequestData - not otherwise aliased on 4.1, see below
#endif

namespace Coti.Server;

/// <summary>
/// Wraps CotiDeviceDto for the request pipeline. CotiDeviceDto itself must stay free of every
/// SPTarkov reference because Coti.Tests source-links it with no SPT assembly present.
///
/// IRequestData is required on BOTH versions. It was gated to 4.1 on the reasoning that
/// 4.0's RouteAction only constrains TRequest to "class" - but that is the compile-time
/// constraint; the dispatcher casts to IRequestData at runtime either way, so 4.0 compiled,
/// loaded, and threw on the first publish.
/// </summary>
public sealed class CotiPublishRequestDto : CotiDeviceDto, IRequestData
{
}

/// <summary>
/// GET /coti/hosts returns the resolved table; POST /coti/hosts/publish validates one device,
/// writes it, fits any host it declares, and reloads.
///
/// Publishing is deliberately ungated: this is a single-player server, and the alternative is a
/// permission model nobody would configure. The publisher is logged instead.
/// </summary>
#if SPT40
[Injectable]
public class CotiDeviceRoutes( JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil,
    ISptLogger<CotiDeviceRoutes> logger, CotiDeviceStore deviceStore, CotiSlotInjector slotInjector,
    ProfileHelper profileHelper )
  : StaticRouter(
      jsonUtil,
      [
        new RouteAction<EmptyRequestData>( "/coti/hosts",
            async ( url, info, sessionID, output ) => await GetHosts( deviceStore, httpResponseUtil ) ),
        new RouteAction<CotiPublishRequestDto>( "/coti/hosts/publish",
            async ( url, info, sessionID, output ) =>
                await PublishDevice( info, sessionID, deviceStore, slotInjector, profileHelper, logger, httpResponseUtil ) ),
      ] )
#else
[Injectable]
public class CotiDeviceRoutes( JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil,
    ISptLogger<CotiDeviceRoutes> logger, CotiDeviceStore deviceStore, CotiSlotInjector slotInjector,
    ProfileHelper profileHelper )
  : StaticRouter(
      jsonUtil,
      [
        new RouteAction<EmptyRequestData>( "/coti/hosts",
            async ( url, info, sessionID, output, cancellationToken ) => await GetHosts( deviceStore, httpResponseUtil ) ),
        new RouteAction<CotiPublishRequestDto>( "/coti/hosts/publish",
            async ( url, info, sessionID, output, cancellationToken ) =>
                await PublishDevice( info, sessionID, deviceStore, slotInjector, profileHelper, logger, httpResponseUtil ) ),
      ] )
#endif
{
  /// <summary>Stands in for a name in the log when there is no session to resolve one from.</summary>
  private const string NoSessionNickname = "(no session)";

  private static Task<string> GetHosts( CotiDeviceStore deviceStore, HttpResponseUtil httpResponseUtil )
  {
    var table = new CotiHostTableDto();

    // ResolvedDevices, not Devices: the wire has to carry the id the server actually FITTED, not
    // the one the file declares. They differ on any host a prefab fallback recovered, and handing
    // the client the stale id is worse than not supporting the host at all - it keys the mask and
    // mount config and the inspect-button gate on an id CotiState will never see, so the player
    // gets a slot, a COTI at the host's origin at scale 1, no thermal image and no Pose button.
    // Publish then writes that stale id straight back, so it never self-corrects either.
    //
    // deviceStore.Current is read exactly once, as a single reference, and the snapshot behind it
    // is immutable - so this foreach walks one complete, self-consistent resolve pass even if a
    // concurrent publish reloads mid-iteration.
    foreach( var device in deviceStore.Current.ResolvedDevices )
      table.Devices.Add( CotiDeviceDto.FromShared( device ) );

    return Task.FromResult( httpResponseUtil.NoBody( table ) );
  }

  private static Task<string> PublishDevice( CotiPublishRequestDto request, MongoId sessionID,
      CotiDeviceStore deviceStore, CotiSlotInjector slotInjector, ProfileHelper profileHelper,
      ISptLogger<CotiDeviceRoutes> logger, HttpResponseUtil httpResponseUtil )
  {
    var nickname = ResolveNickname( sessionID, profileHelper, logger );
    var device = request.ToShared();

    // The same shared path a file on disk takes, over a list of exactly one: every per-file rule
    // CotiDeviceMerge enforces (current schema, non-blank device/displayName, a positive mask
    // radius, non-null hosts/mount) applies here too, so a publish cannot bypass anything a
    // hand-authored file is rejected for. It cannot see cross-file conflicts - a device name or
    // host id already owned by a DIFFERENT file - because there is only one file in this list;
    // that is deliberate. TryWrite's own Reload() below re-reads every file on disk together and
    // is what actually enforces cross-file uniqueness, exactly as it would for two hand-authored
    // files sharing a name or a host. A republish under the SAME device name is therefore an
    // update by design, backed by the .bak TryWrite takes - not a second, competing device.
    var merged = CotiDeviceMerge.Merge( new[] { new CotiParsedFile { Path = "<published>", Device = device } } );

    if( merged.Devices.Count == 0 )
    {
      var reason = merged.Warnings.FirstOrDefault() ?? "rejected by validation";
      logger.Warning( $"[COTI] {nickname} tried to publish \"{request.Device}\" - rejected: {reason}" );
      return Task.FromResult( httpResponseUtil.NoBody( new CotiPublishResultDto { Ok = false, Error = reason } ) );
    }

    if( !deviceStore.TryWrite( device, out var writeError ) )
    {
      logger.Warning( $"[COTI] {nickname} tried to publish \"{device.Device}\" - could not write: {writeError}" );
      return Task.FromResult( httpResponseUtil.NoBody( new CotiPublishResultDto { Ok = false, Error = writeError } ) );
    }

    // Every host the published device declares, not just newly-added ones - InjectInto is its own
    // idempotency check (AlreadyPresent is silent, see CotiSlotInjector), so re-offering a host
    // that already has the slot costs nothing and needs no separate "is this new" computation of
    // our own to get right. The outcome is inspected, not discarded: InvalidId and
    // NoSlotsCollection are real failures - CotiSlotInjector already logs the detail for each at
    // the point it happens, so this only tracks which hosts to report back to the caller.
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
          $"[COTI] {nickname}'s publish of \"{device.Device}\" wrote the file but could not fit " +
          $"{unfitHosts.Count} host(s) - see the CotiSlotInjector line(s) above for why: " +
          string.Join( ", ", unfitHosts ) );
    }

    // TryWrite already reloaded once, from disk, before InjectInto ran above - that is what makes
    // the cross-file enforcement described above real. This second Reload is for InjectInto's own
    // sake: InjectInto never touches deviceStore state (it mutates the live item template
    // directly), so nothing here strictly depends on calling Reload again, but doing so keeps
    // "publish leaves the store exactly as fresh as a restart would" true even if TryWrite is ever
    // changed to stop reloading itself.
    deviceStore.Reload();

    logger.Success(
        $"[COTI] {nickname} published \"{device.Device}\", covering {device.Hosts.Count} host(s)" );

    return Task.FromResult( httpResponseUtil.NoBody(
        new CotiPublishResultDto
        {
          Ok = true,
          Device = CotiDeviceDto.FromShared( device ),
          UnfitHosts = unfitHosts,
        } ) );
  }

  /// <summary>
  /// Logs who published. The only accountability on an ungated write.
  /// </summary>
  private static string ResolveNickname(
      MongoId sessionID, ProfileHelper profileHelper, ISptLogger<CotiDeviceRoutes> logger )
  {
    if( sessionID.IsEmpty )
    {
      logger.Debug( "[COTI] Publish request carried no session id (no PHPSESSID cookie) - " +
          "nickname cannot be resolved" );

      // A sentinel, not sessionID.ToString(): MongoId.Empty().ToString() is the EMPTY STRING, so
      // an anonymous publish logged as nothing at all. Attribution is one of only two mitigations
      // for a write we deliberately left ungated, and a blank name defeats it just as completely
      // as the throw this branch was added to prevent.
      return NoSessionNickname;
    }

    try
    {
      var nickname = profileHelper.GetPmcProfile( sessionID )?.Info?.Nickname;
      return string.IsNullOrWhiteSpace( nickname ) ? sessionID.ToString() : nickname;
    }
    catch( Exception ex )
    {
      logger.Debug( $"[COTI] Could not resolve a nickname for session {sessionID}: {ex.Message}" );
      return sessionID.ToString();
    }
  }
}
