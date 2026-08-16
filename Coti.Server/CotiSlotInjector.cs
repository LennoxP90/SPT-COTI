using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace Coti.Server;

/// <summary>
/// Runs deliberately late. Other mods rewrite NVG templates (AttachmentBackport,
/// Tarkov-1.0-Backport both touch mounts) and a slot added before they run can be discarded.
/// </summary>
[Injectable( TypePriority = OnLoadOrder.PostSptModLoader + 20 )]
public class CotiSlotInjector(
    ISptLogger<CotiSlotInjector> logger,
    DatabaseServer databaseServer ) : IOnLoad
{
  public Task OnLoad()
  {
    var items = databaseServer.GetTables().Templates.Items;
    var added = 0;
    var notInstalled = 0;

    foreach( var nvgHost in CotiNvgHosts.All )
    {
      var hostId = nvgHost.TemplateId;

      if( !MongoId.IsValidMongoId( hostId ) )
      {
        logger.Warning( $"[COTI] Host key \"{hostId}\" is not a valid MongoId - skipped" );
        continue;
      }

      // Not a problem. Some hosts ship with mods that are not required - the PVS-31A comes from
      // a seperate mod - so a host we support but the player does not have is the normal case,
      // and warning about it makes a healthy install look broken.
      if( !items.TryGetValue( new MongoId( hostId ), out var host ) )
      {
        logger.Debug( $"[COTI] {nvgHost.DisplayName} ({hostId}) not installed - skipped" );
        notInstalled++;
        continue;
      }

      if( host.Properties?.Slots is null )
      {
        logger.Warning( $"[COTI] Host {hostId} has no Slots collection - skipped" );
        continue;
      }

      var slots = host.Properties.Slots.ToList();

      if( slots.Any( s => s.Name == CotiIds.ModSlotName ) )
      {
        logger.Warning( $"[COTI] Host {hostId} already has mod_coti - skipped" );
        continue;
      }

      slots.Add( new Slot
      {
        Name = CotiIds.ModSlotName,
        Id = new MongoId(),
        Parent = new MongoId( hostId ),
        Required = false,
        MergeSlotWithChildren = false,
        Properties = new SlotProperties
        {
          Filters = new List<SlotFilter>
                    {
                        new SlotFilter { Filter = new HashSet<MongoId> { new MongoId(CotiItemFactory.CotiTplId) } }
                    }
        }
      } );

      host.Properties.Slots = slots;
      added++;
      // Our own display name, not the template's ShortName - that field is BSG's internal one and
      // is Russian for some items (the PVS-14 logs as "ПНВ"). English lives in the locale files.
      logger.Success( $"[COTI] mod_coti added to {nvgHost.DisplayName} ({hostId})" );
    }

    // Zero IS worth warning about: the item exists and is purchasable, but nothing can mount it.
    if( added == 0 )
    {
      logger.Warning(
          $"[COTI] No supported night vision device is installed - the ECOTI has nothing to " +
          $"clip to. Checked {CotiNvgHosts.All.Count} host(s)." );
    }
    else if( notInstalled > 0 )
    {
      logger.Success( $"[COTI] {added} host(s) fitted, {notInstalled} not installed" );
    }

    return Task.CompletedTask;
  }
}
