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
    ISptLogger<CotiDeviceRoutes> logger, CotiDeviceStore deviceStore, CotiDevicePublisher publisher,
    ProfileHelper profileHelper )
  : StaticRouter(
      jsonUtil,
      [
        new RouteAction<EmptyRequestData>( "/coti/hosts",
            async ( url, info, sessionID, output ) => await GetHosts( deviceStore, httpResponseUtil ) ),
        new RouteAction<CotiPublishRequestDto>( "/coti/hosts/publish",
            async ( url, info, sessionID, output ) =>
                await PublishDevice( info, sessionID, publisher, profileHelper, logger, httpResponseUtil ) ),
      ] )
#else
[Injectable]
public class CotiDeviceRoutes( JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil,
    ISptLogger<CotiDeviceRoutes> logger, CotiDeviceStore deviceStore, CotiDevicePublisher publisher,
    ProfileHelper profileHelper )
  : StaticRouter(
      jsonUtil,
      [
        new RouteAction<EmptyRequestData>( "/coti/hosts",
            async ( url, info, sessionID, output, cancellationToken ) => await GetHosts( deviceStore, httpResponseUtil ) ),
        new RouteAction<CotiPublishRequestDto>( "/coti/hosts/publish",
            async ( url, info, sessionID, output, cancellationToken ) =>
                await PublishDevice( info, sessionID, publisher, profileHelper, logger, httpResponseUtil ) ),
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
      CotiDevicePublisher publisher, ProfileHelper profileHelper,
      ISptLogger<CotiDeviceRoutes> logger, HttpResponseUtil httpResponseUtil )
  {
    var nickname = ResolveNickname( sessionID, profileHelper, logger );

    return Task.FromResult( httpResponseUtil.NoBody( publisher.Publish( request.ToShared(), nickname ) ) );
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
