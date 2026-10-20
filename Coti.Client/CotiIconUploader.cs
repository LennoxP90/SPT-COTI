#if SPT41
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Comfort.Common;
using Coti.Shared;
using EFT.UI;
using EFT.UI.DragAndDrop;
using Newtonsoft.Json;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Once per launch, in the menu: asks the server which item icons the ECOTI compatibility pages want, and sends the
  /// game's own cached PNG for any the server lacks or holds with other bytes. An icon the server already has is
  /// compared only when this client has it cached, never rendered for it. Only the client that claims the server's
  /// lease sends; the rest skip until their next launch.
  /// </summary>
  internal static class CotiIconUploader
  {
    // Only a cap: the wait ends when the game's render queue is empty. A model that never finishes loading would
    // otherwise hold the sync for the whole session.
    private const float RenderWaitSeconds = 300f;

    private static bool _started;

    private sealed class Wanted
    {
      [JsonProperty( "id" )] public string Id = string.Empty;
      [JsonProperty( "sha" )] public string? Sha;
    }

    private sealed class Reply
    {
      [JsonProperty( "token" )] public string? Token;
      [JsonProperty( "error" )] public string? Error;
    }

    /// <summary>Starts the sync the first time the item factory and icon creator both exist. Call from Update.</summary>
    public static void TryStart( MonoBehaviour host )
    {
      if( _started || !Singleton<EFT.ItemFactory>.Instantiated || !Singleton<ItemIconCreator>.Instantiated )
        return;

      _started = true;
      host.StartCoroutine( Run() );
    }

    private static IEnumerator Run()
    {
      var fetch = Task.Run( async () =>
          JsonConvert.DeserializeObject<List<Wanted>>( await SPT.Common.Http.RequestHandler.GetJsonAsync( "/coti/icons/wanted" ) ) );
      while( !fetch.IsCompleted )
        yield return null;

      if( fetch.IsFaulted || fetch.Result == null )
      {
        Plugin.Log.LogWarning( $"[COTI] Item icons: could not fetch /coti/icons/wanted: {fetch.Exception?.GetBaseException().Message}" );
        yield break;
      }

      // Every render is asked for up front, then the sync waits for the game's render queue to empty: at the menu the
      // game is also rendering the whole stash, and ours queue behind it.
      var creator = Singleton<ItemIconCreator>.Instance;
      var pending = new List<(Wanted Wanted, int Hash)>();
      foreach( var wanted in fetch.Result )
      {
        var item = TryCreate( wanted.Id );
        if( item == null )
          continue;

        var hash = EftCompat.GetItemHash( item );
        if( creator.TryGetIconPath( hash, out _ ) )
          pending.Add( (wanted, hash) );
        else if( CotiIconSync.ShouldRender( wanted.Sha ) )
        {
          ItemViewFactory.LoadItemIcon( item );
          pending.Add( (wanted, hash) );
        }
      }

      var until = Time.realtimeSinceStartup + RenderWaitSeconds;
      while( Time.realtimeSinceStartup < until && creator._queueCount > 0 &&
             pending.Exists( p => !creator.TryGetIconPath( p.Hash, out _ ) ) )
        yield return null;

      var send = new List<(string Id, byte[] Png)>();
      var unrendered = 0;
      foreach( var (wanted, hash) in pending )
      {
        if( !creator.TryGetIconPath( hash, out var path ) )
        {
          unrendered++;
          continue;
        }

        var png = TryRead( path );
        if( png != null && CotiIconSync.ShouldSend( wanted.Sha, CotiIconSync.Sha256Hex( png ) ) )
          send.Add( (wanted.Id, png) );
      }
      if( unrendered > 0 )
        Plugin.Log.LogInfo( $"[COTI] Item icons: {unrendered} not cached by the game (rendered before their full textures " +
                            "loaded, which the game does not save); sent on a later launch" );

      if( send.Count == 0 )
      {
        Plugin.Log.LogInfo( $"[COTI] Item icons: the server has all {fetch.Result.Count} this client can give" );
        yield break;
      }

      var upload = Task.Run( () => Upload( send ) );
      while( !upload.IsCompleted )
        yield return null;
      Plugin.Log.LogInfo( upload.IsFaulted
          ? $"[COTI] Item icons: upload failed: {upload.Exception?.GetBaseException().Message}"
          : $"[COTI] Item icons: {upload.Result}" );
    }

    private static async Task<string> Upload( List<(string Id, byte[] Png)> send )
    {
      var claim = JsonConvert.DeserializeObject<Reply>( await SPT.Common.Http.RequestHandler.PostJsonAsync( "/coti/icons/claim", "{}" ) );
      if( claim?.Token == null )
        return $"{send.Count} to send, but another client holds the lease; trying again next launch";

      var sent = 0;
      try
      {
        foreach( var (id, png) in send )
        {
          var body = JsonConvert.SerializeObject( new { token = claim.Token, id, png = Convert.ToBase64String( png ) } );
          var reply = JsonConvert.DeserializeObject<Reply>( await SPT.Common.Http.RequestHandler.PostJsonAsync( "/coti/icons/upload", body ) );
          if( reply?.Error == null )
            sent++;
          else
            Plugin.Log.LogWarning( $"[COTI] Item icon {id} refused: {reply.Error}" );
        }
      }
      finally
      {
        await SPT.Common.Http.RequestHandler.PostJsonAsync( "/coti/icons/done", JsonConvert.SerializeObject( new { token = claim.Token } ) );
      }
      return $"sent {sent} of {send.Count}";
    }

    // A template this client lacks (a server-only mod) has nothing to draw.
    private static EFT.InventoryLogic.Item? TryCreate( string tpl )
    {
      try
      {
        return Singleton<EFT.ItemFactory>.Instance.CreateItem( Guid.NewGuid().ToString( "N" ).Substring( 0, 24 ), tpl, null );
      }
      catch( Exception )
      {
        return null;
      }
    }

    private static byte[]? TryRead( string path )
    {
      try
      {
        var bytes = File.ReadAllBytes( path );
        return bytes.Length <= CotiIconSync.MaxPngBytes && CotiIconSync.IsPng( bytes ) ? bytes : null;
      }
      catch( IOException )
      {
        return null;
      }
    }
  }
}
#endif
