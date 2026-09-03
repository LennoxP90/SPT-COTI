#if SPT41
using System;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Coti.Client
{
  /// <summary>
  /// Listens for the server's "host table changed" push and re-fetches, so an edit made in the
  /// server's web editor reaches a running game. The frame carries no payload; /coti/hosts stays
  /// the one source of the table.
  /// </summary>
  public static class CotiHostSocketClient
  {
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds( 5 );

    private static bool _started;

    /// <summary>
    /// Fire and forget from Awake. Reconnects for the life of the process.
    /// </summary>
    public static void Start()
    {
      if( _started )
        return;

      _started = true;
      Task.Run( ListenForeverAsync );
    }

    private static async Task ListenForeverAsync()
    {
      var url = SocketUrl();
      if( url == null )
      {
        Plugin.Log?.LogWarning( "[COTI] No backend url on the command line - live host updates are off" );
        return;
      }

      TrustSptCertificate( url );

      var loggedFailure = false;

      while( true )
      {
        try
        {
          using( var socket = new ClientWebSocket() )
          {
            await socket.ConnectAsync( url, CancellationToken.None );
            loggedFailure = false;
            Plugin.Log?.LogInfo( "[COTI] listening for host table updates" );

            await ReceiveUntilClosedAsync( socket );
          }
        }
        catch( Exception ex )
        {
          // Once per outage, not once per retry.
          if( !loggedFailure )
          {
            loggedFailure = true;
            Plugin.Log?.LogWarning( $"[COTI] host update socket unavailable, retrying: {ex.Message}" );
          }
        }

        await Task.Delay( RetryDelay );
      }
    }

    private static async Task ReceiveUntilClosedAsync( ClientWebSocket socket )
    {
      var buffer = new byte[256];

      while( socket.State == WebSocketState.Open )
      {
        var result = await socket.ReceiveAsync( new ArraySegment<byte>( buffer ), CancellationToken.None );

        if( result.MessageType == WebSocketMessageType.Close )
          return;

        // Any frame means the same thing; the payload is never parsed.
        Plugin.Log?.LogInfo( "[COTI] host table changed on the server - re-fetching" );
        CotiHostTableClient.BeginFetch();
      }
    }

    /// <summary>
    /// Trusts SPT's self-signed certificate through the process-wide callback, which is net472's
    /// only lever for a ClientWebSocket. Scoped to the SPT backend's host and port, and chained to
    /// whatever was already installed.
    /// </summary>
    private static void TrustSptCertificate( Uri url )
    {
      var previous = ServicePointManager.ServerCertificateValidationCallback;

      ServicePointManager.ServerCertificateValidationCallback =
          ( sender, certificate, chain, errors ) =>
          {
            var request = sender as WebRequest;
            var host = request?.RequestUri?.Host ?? ( sender as string );
            var port = request?.RequestUri?.Port ?? -1;

            if( string.Equals( host, url.Host, StringComparison.OrdinalIgnoreCase )
                && ( port == -1 || port == url.Port ) )
            {
              return true;
            }

            return previous == null
                ? errors == SslPolicyErrors.None
                : previous( sender, certificate, chain, errors );
          };
    }

    /// <summary>
    /// wss when the backend is https. The session id names the connection in the server's log;
    /// the server does not authenticate on it.
    /// </summary>
    private static Uri SocketUrl()
    {
      var host = SPT.Common.Http.RequestHandler.Host;
      if( string.IsNullOrEmpty( host ) )
        return null;

      var scheme = host.StartsWith( "https", StringComparison.OrdinalIgnoreCase ) ? "wss" : "ws";
      var authority = host.Substring( host.IndexOf( "://", StringComparison.Ordinal ) + 3 );

      return new Uri( $"{scheme}://{authority}/coti/ws/{SPT.Common.Http.RequestHandler.SessionId}" );
    }
  }
}
#endif
