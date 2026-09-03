using Coti.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Coti.Server;

/// <summary>
/// Pushes edited settings onto the live server. Shared by the web config editor entry and the
/// ECOTI page. hostEditor is read once during load and is not re-applied here.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiConfigApplier
{
  private readonly ISptLogger<CotiConfigApplier> logger;
  private readonly CotiServerConfig config;
  private readonly CotiTraderAssort traderAssort;

#if SPT40
  private readonly DatabaseServer databaseServer;
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;

  public CotiConfigApplier(
      ISptLogger<CotiConfigApplier> logger, CotiServerConfig config,
      CotiTraderAssort traderAssort, DatabaseServer databaseServer )
  {
    this.logger = logger;
    this.config = config;
    this.traderAssort = traderAssort;
    this.databaseServer = databaseServer;
  }
#else
  private readonly CotiTemplateTable templateTable;

  public CotiConfigApplier(
      ISptLogger<CotiConfigApplier> logger, CotiServerConfig config,
      CotiTraderAssort traderAssort, CotiTemplateTable templateTable )
  {
    this.logger = logger;
    this.config = config;
    this.traderAssort = traderAssort;
    this.templateTable = templateTable;
  }
#endif

  /// <summary>
  /// Replaces the live settings and re-applies everything that can be re-applied. Returns false
  /// when the trader half could not be reached.
  /// </summary>
  public bool Apply( CotiConfigFile file )
  {
    config.Apply( file );

    var trader = traderAssort.ApplyConfig();
    ApplyFlea();

    if( !trader )
      logger.Warning( "[COTI] Trader settings saved but not applied - they take effect on restart" );

    return trader;
  }

  /// <summary>
  /// Sets CanSellOnRagfair on the live template. Offers already on the flea keep the old rule
  /// until they are regenerated, and the client reads the flag at login.
  /// </summary>
  private void ApplyFlea()
  {
    if( templateTable.Items.TryGetValue( new MongoId( CotiItemFactory.CotiTplId ), out var item )
        && item.Properties is not null )
    {
      item.Properties.CanSellOnRagfair = config.Flea.PlayerSellable;
    }
  }
}
