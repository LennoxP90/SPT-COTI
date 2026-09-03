#if SPT41
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Servers.Ws;

namespace Coti.Server;

/// <summary>
/// Tells connected clients the host table changed. Carries no payload: the client re-fetches
/// /coti/hosts, which stays the single source of the table. 4.1 only.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiHostSocket( ISptLogger<CotiHostSocket> logger ) : IWebSocketConnectionHandler
{
  public const string HookUrl = "/coti/ws/";

  /// <summary>The one message ever sent. The client treats any frame as a re-fetch.</summary>
  private const string HostsChanged = "hosts-changed";

  private readonly ConcurrentDictionary<string, WebSocket> sockets = new();

  public string GetHookUrl() => HookUrl;

  public string GetSocketId() => "COTI host table";

  public Task OnConnectionAsync( WebSocket ws, HttpContext context, string sessionIdContext )
  {
    sockets[sessionIdContext] = ws;
    logger.Debug( $"[COTI] host socket connected ({sessionIdContext}), {sockets.Count} open" );

    return Task.CompletedTask;
  }

  // The client never sends anything; it only listens.
  public Task OnMessageAsync( byte[] rawData, WebSocketMessageType messageType, WebSocket ws, HttpContext context )
  {
    return Task.CompletedTask;
  }

  public Task OnCloseAsync( WebSocket ws, HttpContext context, string sessionIdContext )
  {
    sockets.TryRemove( sessionIdContext, out _ );

    return Task.CompletedTask;
  }

  /// <summary>
  /// Fire and forget. A dead socket is dropped rather than retried.
  /// </summary>
  public void NotifyHostsChanged()
  {
    if( sockets.IsEmpty )
      return;

    var payload = new ArraySegment<byte>( Encoding.UTF8.GetBytes( HostsChanged ) );

    foreach( var ( id, socket ) in sockets )
    {
      if( socket.State != WebSocketState.Open )
      {
        sockets.TryRemove( id, out _ );
        continue;
      }

      _ = SendAsync( id, socket, payload );
    }

    logger.Success( $"[COTI] host change pushed to {sockets.Count} client(s)" );
  }

  private async Task SendAsync( string id, WebSocket socket, ArraySegment<byte> payload )
  {
    try
    {
      await socket.SendAsync( payload, WebSocketMessageType.Text, true, CancellationToken.None );
    }
    catch( Exception ex )
    {
      sockets.TryRemove( id, out _ );
      logger.Debug( $"[COTI] host socket {id} dropped: {ex.Message}" );
    }
  }
}
#endif
