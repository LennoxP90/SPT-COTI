#if !SPT40
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace Coti.Server;

/// <summary>
/// The thermal scope rings: a one-piece scope mount with the thermal unit built in, one model per ring size: TSR-1
/// holds 30 mm scopes, TSR-2 34 mm and TSR-3 35 mm. Each is a clone of a vanilla ring mount, keeping its slots and
/// scope filters, and fits wherever that mount fits. No real product name reaches the player.
/// </summary>
public static class ThermalScopeRing
{
  public sealed record Variant( string TplId, string DonorTplId, string BundleKey, string Name, string ShortName,
                                string NewItemName, string Description );

  private const string Maker = "Made by LennoxP90 Industries.";

  private static string Describe( string tube ) =>
      $"One-piece {tube} scope ring mount with an uncooled thermal core in a housing on its side. Contacts in the " +
      "rings feed the thermal image into the scope they hold, magnified with it. Runs on one 18650 cell. " + Maker;

  private static Variant[] Make( string model, string tube, string size, string donor, string black, string tan ) =>
  [
    new( black, donor, $"coti/thermal_scope_ring_{size}.bundle", $"LennoxP90 {model} thermal scope ring {tube}", model,
         $"mount_all_lennoxp90_{model.ToLowerInvariant().Replace( "-", "" )}_{size}mm", Describe( tube ) ),
    new( tan, donor, $"coti/thermal_scope_ring_{size}_tan.bundle", $"LennoxP90 {model} thermal scope ring {tube} (Tan)", model,
         $"mount_all_lennoxp90_{model.ToLowerInvariant().Replace( "-", "" )}_{size}mm_tan", Describe( tube ) ),
  ];

  /// <summary>Ids from CotiIds.ScopeRingTplIds; each BundleKey must match bundles.json and BuildThermalScopeRingPrefab.</summary>
  public static readonly Variant[] Variants =
  [
    ..Make( "TSR-1", "30mm", "30", "6171407e50224f204c1da3c5", Coti.Shared.CotiIds.ScopeRingTplIds[0], Coti.Shared.CotiIds.ScopeRingTplIds[1] ),
    ..Make( "TSR-2", "34mm", "34", "61713cc4d8e3106d9806c109", Coti.Shared.CotiIds.ScopeRingTplIds[2], Coti.Shared.CotiIds.ScopeRingTplIds[3] ),
    ..Make( "TSR-3", "35mm", "35", "6761759e7ee06333f108bf86", Coti.Shared.CotiIds.ScopeRingTplIds[4], Coti.Shared.CotiIds.ScopeRingTplIds[5] ),
  ];
}

/// <summary>Registers every scope ring. Custom items must exist by Preload (4.1.3).</summary>
[Injectable( TypePriority = CotiLoadOrder.Preload + 2 )]
public class ThermalScopeRingItemFactory( ISptLogger<ThermalScopeRingItemFactory> logger, CustomItemService customItemService ) : IOnLoad
{
  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    foreach( var v in ThermalScopeRing.Variants )
    {
      var details = new NewItemFromCloneDetails
      {
        ItemTplToClone = new MongoId( v.DonorTplId ),
        NewId = new MongoId( v.TplId ),
        // a tactical combo, as the riser is, so the power and mode keys and the LED's modes work
        ParentId = new MongoId( ThermalRiser.TacticalComboClassId ),
        NewItemName = v.NewItemName,
        HandbookParentId = ThermalRiser.HandbookParentId,
        HandbookPriceRoubles = 145000,
        FleaPriceRoubles = 145000,
        AddToHandbook = true,
        AddToFleaPriceDb = true,
        AddToWeaponShelf = false,
        OverrideProperties = new TemplateItemProperties
        {
          Name = v.Name,
          ShortName = v.ShortName,
          Description = v.Description,
          ModesCount = new ListOrT<int>( null, 1 ),
          Prefab = new Prefab { Path = v.BundleKey, Rcid = "" },
        },
        Locales = new Dictionary<string, LocaleDetails>
        {
          ["en"] = new LocaleDetails { Name = v.Name, ShortName = v.ShortName, Description = v.Description },
        },
      };

      var result = customItemService.CreateItemFromClone( details );
      if( result.Success != true )
        logger.Error( $"[COTI] Scope ring {v.TplId} not created: {string.Join( "; ", result.Errors ?? [] )}" );
      else
        logger.Success( $"[COTI] Scope ring created, id {v.TplId} ({v.Name})" );
    }
    return Task.CompletedTask;
  }
}

/// <summary>After every mod has loaded: each scope ring goes into every slot filter that takes its base mount.</summary>
[Injectable( TypePriority = CotiLoadOrder.PostLoad + 26 )]
public class ThermalScopeRingCompatibility( ISptLogger<ThermalScopeRingCompatibility> logger, CotiTemplateTable templateTable,
    ItemHelper itemHelper ) : IOnLoad
{
  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    var ringIds = ThermalScopeRing.Variants.Select( v => new MongoId( v.TplId ) ).ToHashSet();
    var byDonor = ThermalScopeRing.Variants.GroupBy( v => new MongoId( v.DonorTplId ) )
        .ToDictionary( g => g.Key, g => g.Select( v => new MongoId( v.TplId ) ).ToArray() );
    var hosts = 0;
    foreach( var template in templateTable.Items.Values )
    {
      if( ringIds.Contains( template.Id ) ) continue;
      foreach( var filter in ( template.Properties?.Slots ?? [] ).SelectMany( s => s.Properties?.Filters ?? [] ) )
      {
        if( filter.Filter is null ) continue;
        var added = false;
        foreach( var (donor, rings) in byDonor )
          if( filter.Filter.Contains( donor ) )
            foreach( var id in rings )
              added |= filter.Filter.Add( id );
        if( added ) hosts++;
      }
    }
    // No riser on a ring: the riser's compatibility (PostLoad + 25) puts it in every rail scope slot, the rings' own
    // top rails included, and one device feeding another's sight is not a thing.
    var riserIds = Coti.Shared.CotiIds.RiserTplIds.Select( id => new MongoId( id ) ).ToArray();
    var unfitted = 0;
    foreach( var id in ringIds )
    {
      if( !templateTable.Items.TryGetValue( id, out var ring ) ) continue;
      foreach( var filter in ( ring.Properties?.Slots ?? [] ).SelectMany( s => s.Properties?.Filters ?? [] ) )
        foreach( var riser in riserIds )
          if( filter.Filter?.Remove( riser ) == true ) unfitted++;
    }
    var taken = TakeTubePeers( ringIds );
    logger.Success( $"[COTI] Scope rings ({ringIds.Count}): fit {hosts} slots that take their base mounts; " +
                    $"the riser taken out of {unfitted} ring slot filters; {taken} scopes taken from same-tube mounts" );
    return Task.CompletedTask;
  }

  // Mods add scopes by exact slot name; ISB Aishi's modSlot ["mod_scope"] skips the mod_scope_000 slots the rings
  // inherit from their base mounts. A scope that shares another mount's scope slot with at least two scopes a ring
  // already takes, where that slot holds none another ring size takes, is the same tube: the ring takes it too.
  // Only riflescopes: the same slots also hold red dot mounts, which a ring does not.
  private int TakeTubePeers( HashSet<MongoId> ringIds )
  {
    var items = templateTable.Items;
    var ringFilters = ThermalScopeRing.Variants
        .Where( v => items.ContainsKey( new MongoId( v.TplId ) ) )
        .GroupBy( v => v.DonorTplId )
        .Select( g => g.Select( v => ScopeFilters( items[new MongoId( v.TplId )] ).ToList() ).ToList() )
        .ToList();
    var held = ringFilters.Select( size => size[0].SelectMany( f => f ).ToHashSet() ).ToList();

    var taken = 0;
    for( var s = 0; s < held.Count; s++ )
    {
      var others = held.Where( ( _, i ) => i != s ).SelectMany( h => h ).ToHashSet();
      var add = new HashSet<MongoId>();
      foreach( var template in items.Values )
      {
        if( ringIds.Contains( template.Id ) ) continue;
        foreach( var filter in ScopeFilters( template ) )
          if( filter.Count( held[s].Contains ) >= 2 && !filter.Any( others.Contains ) )
            add.UnionWith( filter.Where( id => !held[s].Contains( id ) && items.ContainsKey( id ) &&
                itemHelper.IsOfBaseclass( id, BaseClasses.OPTIC_SCOPE ) ) );
      }
      foreach( var filter in ringFilters[s].SelectMany( ring => ring ) )
        foreach( var id in add )
          if( filter.Add( id ) ) taken++;
    }
    return taken;
  }

  private static IEnumerable<HashSet<MongoId>> ScopeFilters( TemplateItem template ) =>
      ( template.Properties?.Slots ?? [] )
          .Where( s => s.Name?.StartsWith( "mod_scope" ) == true )
          .SelectMany( s => s.Properties?.Filters ?? [] )
          .Select( f => f.Filter )
          .OfType<HashSet<MongoId>>();
}
#endif
