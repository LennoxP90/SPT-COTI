using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace Coti.Server;

/// <summary>
/// Adds the COTI to Peacekeeper's assort. Loyalty level, price and purchase limit come from
/// config/config.json. Runs after CotiItemFactory so the COTI template already exists.
/// </summary>
[Injectable( TypePriority = OnLoadOrder.PostSptModLoader + 30 )]
public class CotiTraderAssort(
    ISptLogger<CotiTraderAssort> logger,
    CotiServerConfig config,
    DatabaseServer databaseServer ) : IOnLoad
{
  public Task OnLoad()
  {
    if( !databaseServer.GetTables().Traders.TryGetValue( Traders.PEACEKEEPER, out var peacekeeper ) )
    {
      logger.Error( "[COTI] Peacekeeper not found in the trader database - assort not added" );
      return Task.CompletedTask;
    }

    if( peacekeeper.Assort is null )
    {
      logger.Error( "[COTI] Peacekeeper has no Assort - assort not added" );
      return Task.CompletedTask;
    }

    var assortItemId = new MongoId();

    peacekeeper.Assort.Items.Add( new Item
    {
      Id = assortItemId,
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

    peacekeeper.Assort.BarterScheme[assortItemId] = new List<List<BarterScheme>>
    {
      new List<BarterScheme>
      {
        new BarterScheme { Count = config.Trader.PriceUsd, Template = ItemTpl.MONEY_DOLLARS }
      }
    };
    peacekeeper.Assort.LoyalLevelItems[assortItemId] = config.Trader.LoyaltyLevel;

    logger.Success(
        $"[COTI] Added to Peacekeeper LL{config.Trader.LoyaltyLevel} at ${config.Trader.PriceUsd}, " +
        $"limit {config.Trader.BuyLimit}" );

    return Task.CompletedTask;
  }
}
