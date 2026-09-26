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
/// IRequestData is required on both versions. 4.0's RouteAction only constrains TRequest to
/// "class" at compile time, but the dispatcher casts to IRequestData at runtime.
/// </summary>
public sealed class CotiPublishRequestDto : CotiDeviceDto, IRequestData
{
}

/// <summary>
/// GET /coti/hosts returns the resolved table; POST /coti/hosts/publish validates one device,
/// writes it, fits any host it declares, and reloads.
///
/// Publishing is ungated because this is a single-player server; the publisher is logged instead.
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

    // ResolvedDevices carries the id the server fitted, which differs from the declared id on any
    // host a prefab fallback recovered. The client keys the mask, mount and inspect-button gate on
    // this id, and Publish writes it back, so it has to match what CotiState sees.
    //
    // deviceStore.Current is read once and the snapshot behind it is immutable, so this walks one
    // consistent resolve pass even if a concurrent publish reloads mid-iteration.
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

      // A sentinel rather than sessionID.ToString(), which is an empty string for an empty
      // MongoId and would leave the publish unattributed.
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
