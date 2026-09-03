using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
#if SPT41
using SPTarkov.Server.Core.Models.Spt.Tables; // TradersTable - no alias needed, it IS a Dictionary<MongoId, Trader>
#endif

namespace Coti.Server;

/// <summary>
/// Adds the COTI to Peacekeeper's assort. Loyalty level, price and purchase limit come from
/// config/config.json. Runs after CotiItemFactory so the COTI template already exists.
/// </summary>
// TraderRegistration, not PostLoad: PostLoad runs after SPT has built the flea offers. See
// CotiLoadOrder.TraderRegistration.
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.TraderRegistration + 30 )]
public class CotiTraderAssort : IOnLoad
{
  private readonly ISptLogger<CotiTraderAssort> logger;
  private readonly CotiServerConfig config;

  /// <summary>
  /// The assort entry this mod added, for re-applying the config to it. Null until OnLoad has
  /// run, or if the trader was not there to add it to.
  /// </summary>
  private MongoId? assortItemId;

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

    var newAssortItemId = new MongoId();

    peacekeeper.Assort.Items.Add( new Item
    {
      Id = newAssortItemId,
      Template = new MongoId( CotiItemFactory.CotiTplId ),
      ParentId = "hideout",
      SlotId = "hideout",
      Upd = new Upd
      {
        StackObjectsCount = 9999999,
        UnlimitedCount = false,
        BuyRestrictionMax = config.Trader.BuyLimit,
        BuyRestrictionCurrent = 0
      }
    } );

    peacekeeper.Assort.BarterScheme[newAssortItemId] = new List<List<BarterScheme>>
    {
      new List<BarterScheme>
      {
        new BarterScheme { Count = config.Trader.PriceUsd, Template = ItemTpl.MONEY_DOLLARS }
      }
    };
    peacekeeper.Assort.LoyalLevelItems[newAssortItemId] = config.Trader.LoyaltyLevel;

    assortItemId = newAssortItemId;

    logger.Success(
        $"[COTI] Added to Peacekeeper LL{config.Trader.LoyaltyLevel} at ${config.Trader.PriceUsd}, " +
        $"limit {config.Trader.BuyLimit}" );

    return Task.CompletedTask;
  }

  /// <summary>
  /// Rewrites the live assort from the current config. Peacekeeper's own screen shows the new
  /// values immediately; the flea copy follows on the next ragfair update, which
  /// RefreshTraderRagfairOffers asks for.
  /// </summary>
  public bool ApplyConfig()
  {
    if( assortItemId is null )
      return false;

    if( !tradersTable.TryGetValue( Traders.PEACEKEEPER, out var peacekeeper ) || peacekeeper.Assort is null )
      return false;

    var id = assortItemId.Value;
    var item = peacekeeper.Assort.Items.FirstOrDefault( entry => entry.Id == id );
    if( item is null )
      return false;

    item.Upd ??= new Upd();
    item.Upd.BuyRestrictionMax = config.Trader.BuyLimit;

    peacekeeper.Assort.BarterScheme[id] = new List<List<BarterScheme>>
    {
      new List<BarterScheme>
      {
        new BarterScheme { Count = config.Trader.PriceUsd, Template = ItemTpl.MONEY_DOLLARS }
      }
    };
    peacekeeper.Assort.LoyalLevelItems[id] = config.Trader.LoyaltyLevel;

    if( peacekeeper.Base is not null )
      peacekeeper.Base.RefreshTraderRagfairOffers = true;

    logger.Success(
        $"[COTI] Trader settings re-applied: LL{config.Trader.LoyaltyLevel}, " +
        $"${config.Trader.PriceUsd}, limit {config.Trader.BuyLimit}" );

    return true;
  }
}
