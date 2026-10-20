#if !SPT40
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coti.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Coti.Server;

/// <summary>
/// Item icons for the ECOTI compatibility pages, uploaded once by the first game client that has them: the server
/// cannot render an icon itself. Files live in icons/ beside the mod as &lt;id&gt;.png with icons/index.json mapping
/// each id to its SHA-256, and each is served at /coti/icons/&lt;id&gt;.png through SPT's ImageRouter. Only ids the
/// pages list are wanted or accepted, one file per id, so the folder stays at about a few megabytes.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiIconStore
{
  private readonly ISptLogger<CotiIconStore> logger;
  private readonly ImageRouter imageRouter;
  private readonly ThermalRiserCompatibility riser;
  private readonly ThermalScopeRingCompatibility rings;
  private readonly object gate = new();
  private readonly CotiIconLease lease = new();
  private readonly Dictionary<string, string> index = new( StringComparer.OrdinalIgnoreCase );
  private readonly string folder = string.Empty;

  public CotiIconStore( ISptLogger<CotiIconStore> logger, ModHelper modHelper, ImageRouter imageRouter,
      ThermalRiserCompatibility riser, ThermalScopeRingCompatibility rings )
  {
    this.logger = logger;
    this.imageRouter = imageRouter;
    this.riser = riser;
    this.rings = rings;
    try
    {
      folder = Path.Combine( modHelper.GetAbsolutePathToModFolder( typeof( CotiIconStore ).Assembly ), "icons" );
      if( File.Exists( IndexPath ) )
        foreach( var (id, sha) in JsonSerializer.Deserialize<Dictionary<string, string>>( File.ReadAllText( IndexPath ) ) ?? [] )
          if( File.Exists( PngPath( id ) ) )
            Register( id, sha );
    }
    catch( Exception ex )
    {
      logger.Warning( $"[COTI] Item icons not loaded ({ex.Message}); the pages fall back to tarkov.dev" );
    }
  }

  private string IndexPath => Path.Combine( folder, "index.json" );
  private string PngPath( string id ) => Path.Combine( folder, id.ToLowerInvariant() + ".png" );

  /// <summary>The page's image address with a version from the hash, so a replaced icon is not served from cache; null when none.</summary>
  public string? Url( string id )
  {
    lock( gate )
      return index.TryGetValue( id, out var sha ) ? $"/coti/icons/{id.ToLowerInvariant()}.png?v={sha[..8]}" : null;
  }

  public int Count { get { lock( gate ) return index.Count; } }

  /// <summary>Every id either page lists, with the hash the server holds or null.</summary>
  public List<CotiWantedIcon> Wanted()
  {
    var ids = riser.RuleMatches.Select( id => id.ToString() ).Concat( rings.RuleMatches.Keys.Select( id => id.ToString() ) )
        .Distinct( StringComparer.OrdinalIgnoreCase );
    lock( gate )
      return ids.Select( id => new CotiWantedIcon { Id = id, Sha = index.GetValueOrDefault( id ) } ).ToList();
  }

  public string? Claim()
  {
    lock( gate )
      return lease.TryClaim( DateTime.UtcNow );
  }

  public void Release( string? token )
  {
    lock( gate )
      lease.Release( token );
  }

  /// <summary>Writes one icon; the reason when refused. Temp file and move, then the index, so a crash leaves at most a stray .tmp.</summary>
  public string? Accept( string? token, string? id, byte[] png, string sessionId )
  {
    if( string.IsNullOrEmpty( id ) || !ThermalRiserSightsFile.IsTemplateId( id ) || !Wanted().Any( w => w.Id.Equals( id, StringComparison.OrdinalIgnoreCase ) ) )
      return "not an id the pages list";
    if( png.Length > CotiIconSync.MaxPngBytes || !CotiIconSync.IsPng( png ) )
      return "not a PNG under the size limit";

    lock( gate )
    {
      if( !lease.Accepts( token, DateTime.UtcNow ) )
        return "no lease";
      try
      {
        Directory.CreateDirectory( folder );
        File.WriteAllBytes( PngPath( id ) + ".tmp", png );
        File.Move( PngPath( id ) + ".tmp", PngPath( id ), overwrite: true );
        Register( id, CotiIconSync.Sha256Hex( png ) );
        File.WriteAllText( IndexPath + ".tmp", JsonSerializer.Serialize( index ) );
        File.Move( IndexPath + ".tmp", IndexPath, overwrite: true );
      }
      catch( Exception ex )
      {
        return ex.Message;
      }
    }
    logger.Debug( $"[COTI] Item icon {id} ({png.Length} bytes) from session {sessionId}" );
    return null;
  }

  /// <summary>Deletes every icon; the next client sends them again.</summary>
  public void Clear()
  {
    lock( gate )
    {
      index.Clear();
      if( Directory.Exists( folder ) )
        Directory.Delete( folder, recursive: true );
    }
  }

  private void Register( string id, string sha )
  {
    index[id.ToLowerInvariant()] = sha;
    imageRouter.AddRoute( $"/coti/icons/{id.ToLowerInvariant()}", PngPath( id ) );
  }
}

public class CotiWantedIcon
{
  [JsonPropertyName( "id" )] public string Id { get; set; } = string.Empty;
  [JsonPropertyName( "sha" )] public string? Sha { get; set; }
}

public sealed class CotiIconUploadRequest : IRequestData
{
  [JsonPropertyName( "token" )] public string? Token { get; set; }
  [JsonPropertyName( "id" )] public string? Id { get; set; }
  [JsonPropertyName( "png" )] public string? Png { get; set; }
}

/// <summary>
/// /coti/icons/wanted, /claim, /upload and /done for the client's one-time sync (Coti.Client CotiIconUploader).
/// Responses are NoBody: the client deserialises these shapes itself.
/// </summary>
[Injectable]
public class CotiIconRoutes( JsonUtil jsonUtil, HttpResponseUtil http, CotiIconStore store, ISptLogger<CotiIconRoutes> logger )
  : StaticRouter(
      jsonUtil,
      [
        new RouteAction<EmptyRequestData>( "/coti/icons/wanted",
            async ( url, info, sessionId, output, ct ) => await Task.FromResult( http.NoBody( store.Wanted() ) ) ),
        new RouteAction<EmptyRequestData>( "/coti/icons/claim",
            async ( url, info, sessionId, output, ct ) => await Task.FromResult( http.NoBody( new { token = store.Claim() } ) ) ),
        new RouteAction<CotiIconUploadRequest>( "/coti/icons/upload",
            async ( url, info, sessionId, output, ct ) => await Task.FromResult( http.NoBody( new { error = Upload( store, info, sessionId ) } ) ) ),
        new RouteAction<CotiIconUploadRequest>( "/coti/icons/done",
            async ( url, info, sessionId, output, ct ) =>
            {
              store.Release( info.Token );
              logger.Info( $"[COTI] Item icons: upload from session {sessionId} done, {store.Count} held" );
              return await Task.FromResult( http.NoBody( new { error = (string?)null } ) );
            } ),
      ] )
{
  private static string? Upload( CotiIconStore store, CotiIconUploadRequest info, MongoId sessionId )
  {
    byte[] png;
    try
    {
      png = Convert.FromBase64String( info.Png ?? string.Empty );
    }
    catch( FormatException )
    {
      return "not base64";
    }
    return store.Accept( info.Token, info.Id, png, sessionId.ToString() );
  }
}
#endif
