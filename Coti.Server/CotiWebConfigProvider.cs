#if SPT41
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;

namespace Coti.Server;

/// <summary>
/// Puts config/config.json into the server's web Config Editor. 4.1 only. Three registrations
/// over the one file, each naming what a change to its section needs and hiding the other two
/// sections from the Controls tab.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiWebConfigProvider(
    ISptLogger<CotiWebConfigProvider> logger,
    CotiServerConfig config,
    CotiConfigApplier applier ) : IConfigEditorConfigProvider
{
  private const string TraderAndFlea = "coti-trader-flea";
  private const string Loot = "coti-loot";
  private const string HostEditor = "coti-host-editor";

  public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
  {
    yield return Build(
        TraderAndFlea,
        "ECOTI - trader and flea (relaunch the game client)",
        [ "/loot", "/hostEditor" ] );

    yield return Build(
        Loot,
        "ECOTI - loot (applies at the next raid)",
        [ "/trader", "/flea", "/hostEditor" ] );

    yield return Build(
        HostEditor,
        "ECOTI - host editor (needs a server restart)",
        [ "/trader", "/flea", "/loot" ] );
  }

  private ConfigEditorConfigRegistration Build( string id, string displayName, string[] hidden )
  {
    return new ConfigEditorConfigRegistration
    {
      Id = id,
      DisplayName = displayName,
      RuntimeConfig = config.ToFile(),
      RuntimeType = typeof( CotiConfigFile ),
      FilePath = config.FilePath,
      FileName = "config.json",
      IgnoredSectionPaths = new HashSet<string>( hidden ),
      LoadFromDiskAsync = LoadAsync,
      SaveToDiskAsync = SaveAsync,
      ApplyToRuntimeAsync = ApplyAsync
    };
  }

  private ValueTask<object?> LoadAsync( CancellationToken cancellationToken )
  {
    return new ValueTask<object?>( config.ReadFromDisk() );
  }

  private async ValueTask SaveAsync( object edited, CancellationToken cancellationToken )
  {
    if( !await config.SaveToDiskAsync( AsFile( edited ), cancellationToken ) )
      logger.Error( "[COTI] No config path resolved - the web editor cannot save" );
  }

  private ValueTask ApplyAsync( object edited, CancellationToken cancellationToken )
  {
    applier.Apply( AsFile( edited ) );
    return default;
  }

  /// <summary>
  /// The editor hands back whatever RuntimeType deserialised to; a round-trip through JSON
  /// normalises it.
  /// </summary>
  private static CotiConfigFile AsFile( object edited )
  {
    if( edited is CotiConfigFile file )
      return file;

    return JsonSerializer.Deserialize<CotiConfigFile>( JsonSerializer.Serialize( edited ) ) ?? new CotiConfigFile();
  }
}
#endif
