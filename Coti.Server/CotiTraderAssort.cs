using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
#if SPT41
using SPTarkov.Server.Core.Models.Spt.Tables; // TradersTable is a Dictionary<MongoId, Trader>, so no alias is needed
#endif

namespace Coti.Server;

/// <summary>
/// Adds the COTI and, on 4.1, each thermal riser colourway and thermal scope ring to Peacekeeper's
/// assort. Loyalty level, price and purchase limit come from config/config.json: the COTI's trader
/// block, and riser.trader for every riser and ring (a ring at riser.trader.ringPriceUsd). Runs after the item factories so the templates already exist. The Debug-only $1
/// riser offer is ThermalRiserTestAssort, separate from these.
/// </summary>
// TraderRegistration, not PostLoad: PostLoad runs after SPT has built the flea offers. See
// CotiLoadOrder.TraderRegistration.
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.TraderRegistration + 30 )]
public class CotiTraderAssort : IOnLoad
{
  private readonly ISptLogger<CotiTraderAssort> logger;
  private readonly CotiServerConfig config;

  /// <summary>
  /// The assort entries this mod added, each with the live settings it follows, for re-applying the
  /// config. Empty until OnLoad has run, or if the trader was not there to add them to.
  /// </summary>
  private readonly List<(MongoId AssortId, CotiTraderSettings Settings, Func<int> Price)> entries = new();

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long
  // before that - so the table is resolved on use, inside OnLoad, never in the constructor.
  private Dictionary<MongoId, Trader> tradersTable => databaseServer.GetTables().Traders;

  public CotiTraderAssort(
      ISptLogger<CotiTraderAssort> logger,
      CotiServerConfig config,
      DatabaseServer databaseServer )
  {
    this.logger = logger;
    this.config = config;
    this.databaseServer = databaseServer;
  }
#else
  private readonly Dictionary<MongoId, Trader> tradersTable;

  public CotiTraderAssort(
      ISptLogger<CotiTraderAssort> logger,
      CotiServerConfig config,
      TradersTable tradersTable )
  {
    this.logger = logger;
    this.config = config;
    this.tradersTable = tradersTable;
  }
#endif

  // The interface member differs between versions; the work does not.
#if SPT40
  public Task OnLoad() => LoadAsync( CancellationToken.None );
#else
  public Task OnLoadAsync( CancellationToken cancellationToken ) => LoadAsync( cancellationToken );
#endif

  private Task LoadAsync( CancellationToken cancellationToken )
  {
    if( !tradersTable.TryGetValue( Traders.PEACEKEEPER, out var peacekeeper ) )
    {
      logger.Error( "[COTI] Peacekeeper not found in TradersTable - assort not added" );
      return Task.CompletedTask;
    }

    if( peacekeeper.Assort is null )
    {
      logger.Error( "[COTI] Peacekeeper has no Assort - assort not added" );
      return Task.CompletedTask;
    }

    Add( peacekeeper.Assort, CotiItemFactory.CotiTplId, config.Trader, "COTI", () => config.Trader.PriceUsd );
    Report( "COTI", config.Trader, config.Trader.PriceUsd );
#if !SPT40
    // The riser is 4.1 only, so the 4.0 build must not list a template it never registers.
    foreach( var colourway in ThermalRiser.Colourways )
      Add( peacekeeper.Assort, colourway.TplId, config.Riser.Trader, colourway.Name, () => config.Riser.Trader.PriceUsd );
    Report( $"TR-1 thermal sight riser ({ThermalRiser.Colourways.Length} colourways)", config.Riser.Trader, config.Riser.Trader.PriceUsd );
    foreach( var ring in ThermalScopeRing.Variants )
      Add( peacekeeper.Assort, ring.TplId, config.Riser.Trader, ring.Name, () => config.Riser.Trader.RingPriceUsd );
    Report( $"TSR thermal scope rings ({ThermalScopeRing.Variants.Length})", config.Riser.Trader, config.Riser.Trader.RingPriceUsd );
#endif

    return Task.CompletedTask;
  }

  private void Add( TraderAssort assort, string tplId, CotiTraderSettings settings, string name, Func<int> price )
  {
    var id = new MongoId();

    assort.Items.Add( new Item
    {
      Id = id,
      Template = new MongoId( tplId ),
      ParentId = "hideout",
      SlotId = "hideout",
      Upd = new Upd
      {
        StackObjectsCount = 9999999,
        UnlimitedCount = false,
        BuyRestrictionCurrent = 0
      }
    } );

    Write( assort, id, settings, price() );
    entries.Add( ( id, settings, price ) );

    logger.Debug( $"[COTI] {name} added to Peacekeeper, assort {id}" );
  }

  // One line per device type; each item's own line is at debug level.
  private void Report( string what, CotiTraderSettings settings, int priceUsd ) =>
      logger.Success( $"[COTI] {what} added to Peacekeeper LL{settings.LoyaltyLevel} at ${priceUsd}, limit {settings.BuyLimit}" );

  /// <summary>
  /// Loyalty level, price and purchase limit of one entry, from its settings as they are now.
  /// </summary>
  private static void Write( TraderAssort assort, MongoId id, CotiTraderSettings settings, int priceUsd )
  {
    var item = assort.Items.FirstOrDefault( entry => entry.Id == id );
    if( item is not null )
    {
      item.Upd ??= new Upd();
      item.Upd.BuyRestrictionMax = settings.BuyLimit;
    }

    assort.BarterScheme[id] = new List<List<BarterScheme>>
    {
      new List<BarterScheme>
      {
        new BarterScheme { Count = priceUsd, Template = ItemTpl.MONEY_DOLLARS }
      }
    };
    assort.LoyalLevelItems[id] = settings.LoyaltyLevel;
  }

  /// <summary>
  /// Rewrites the live assort from the current config. Peacekeeper's own screen shows the new
  /// values immediately; the flea copy follows on the next ragfair update, which
  /// RefreshTraderRagfairOffers asks for.
  /// </summary>
  public bool ApplyConfig()
  {
    if( entries.Count == 0 )
      return false;

    if( !tradersTable.TryGetValue( Traders.PEACEKEEPER, out var peacekeeper ) || peacekeeper.Assort is null )
      return false;

    foreach( var ( id, settings, price ) in entries )
      Write( peacekeeper.Assort, id, settings, price() );

    if( peacekeeper.Base is not null )
      peacekeeper.Base.RefreshTraderRagfairOffers = true;

    logger.Debug(
        $"[COTI] Trader settings re-applied: COTI LL{config.Trader.LoyaltyLevel}, ${config.Trader.PriceUsd}, " +
        $"limit {config.Trader.BuyLimit}; riser LL{config.Riser.Trader.LoyaltyLevel}, " +
        $"${config.Riser.Trader.PriceUsd} (rings ${config.Riser.Trader.RingPriceUsd}), limit {config.Riser.Trader.BuyLimit}" );

    return true;
  }
}
