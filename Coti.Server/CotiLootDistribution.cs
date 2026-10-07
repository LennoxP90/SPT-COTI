using Coti.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Coti.Server;

/// <summary>
/// Spawns the COTI wherever night vision already spawns, at a fraction of its weight. NVGs come from
/// the item database, not <see cref="CotiDeviceStore"/>, so modded ones count too.
/// </summary>
[Injectable( TypePriority = CotiLoadOrder.PostLoad + 60 )]
public class CotiLootDistribution : IOnLoad
{
  private readonly ISptLogger<CotiLootDistribution> logger;
  private readonly CotiServerConfig config;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long
  // before that - so both tables are resolved on use, inside OnLoad, never in the constructor.
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;
  private CotiLocationTable locationTable => databaseServer.GetTables().Locations;

  public CotiLootDistribution(
      ISptLogger<CotiLootDistribution> logger,
      CotiServerConfig config,
      DatabaseServer databaseServer )
  {
    this.logger = logger;
    this.config = config;
    this.databaseServer = databaseServer;
  }
#else
  private readonly CotiTemplateTable templateTable;
  private readonly CotiLocationTable locationTable;

  public CotiLootDistribution(
      ISptLogger<CotiLootDistribution> logger,
      CotiServerConfig config,
      CotiTemplateTable templateTable,
      CotiLocationTable locationTable )
  {
    this.logger = logger;
    this.config = config;
    this.templateTable = templateTable;
    this.locationTable = locationTable;
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
    var nightVisionTpls = GetNightVisionTpls();
    if( nightVisionTpls.Count == 0 )
    {
      logger.Warning( "[COTI] No night vision templates found - the COTI will not spawn in loot" );
      return Task.CompletedTask;
    }

    var cotiTpl = new MongoId( CotiItemFactory.CotiTplId );

    foreach( var entry in locationTable.GetDictionary() )
    {
      AddToStaticLoot( entry.Key, entry.Value, nightVisionTpls, cotiTpl );
      AddToLooseLoot( entry.Key, entry.Value, nightVisionTpls, cotiTpl );
    }

    logger.Success( LootEnabled
        ? $"[COTI] Loot spawns registered against {nightVisionTpls.Count} night vision template(s) " +
          $"at {config.Loot.WeightFraction:P0} of their weight"
        : "[COTI] Loot spawns disabled by config - the trader is the only source. The transformers "
          + "are registered, so switching it on takes effect at the next raid." );

    return Task.CompletedTask;
  }

  /// <summary>
  /// Read inside the transformers, not at load. The transformers are registered unconditionally,
  /// and switching loot on takes effect at the next raid.
  /// </summary>
  private bool LootEnabled => config.Loot.Enabled && config.Loot.WeightFraction > 0;

  private HashSet<MongoId> GetNightVisionTpls()
  {
    var nightVision = new MongoId( BaseClasses.NIGHT_VISION );

    return templateTable.Items.Values
        .Where( item => item.Parent == nightVision )
        .Select( item => item.Id )
        .ToHashSet();
  }

  /// <summary>
  /// A transformer, not a direct write: LazyLoad.Value re-deserialises from disk on every read, so a
  /// mutation would be discarded by the next lookup.
  /// </summary>
  private void AddToStaticLoot( string name, Location location, HashSet<MongoId> nightVisionTpls, MongoId cotiTpl )
  {
    location.StaticLoot?.AddTransformer( staticLoot =>
    {
      if( staticLoot is null || !LootEnabled )
        return staticLoot;

      var added = 0;

      foreach( var container in staticLoot.Values )
      {
        var distribution = container.ItemDistribution;
        if( distribution is null )
          continue;

        var nightVisionWeight = StaticNightVisionWeight( distribution, nightVisionTpls, cotiTpl );
        if( nightVisionWeight <= 0 )
          continue;

        container.ItemDistribution = new List<ItemDistribution>( distribution )
        {
          new ItemDistribution
          {
            Tpl = cotiTpl,
            RelativeProbability = (float)( nightVisionWeight * config.Loot.WeightFraction )
          }
        };

        added++;
      }

      Report( name, "containers", added );

      return staticLoot;
    } );
  }

  /// <summary>
  /// A position needs the COTI in both its item list and its composedKey distribution; either alone
  /// is skipped. It spawns one item, so the COTI takes weight from everything else there.
  /// </summary>
  private void AddToLooseLoot( string name, Location location, HashSet<MongoId> nightVisionTpls, MongoId cotiTpl )
  {
    location.LooseLoot?.AddTransformer( looseLoot =>
    {
      if( looseLoot?.Spawnpoints is null || !LootEnabled )
        return looseLoot;

      var added = 0;

      foreach( var spawnpoint in looseLoot.Spawnpoints )
      {
        var template = spawnpoint.Template;
        var items = template?.Items;
        var distribution = spawnpoint.ItemDistribution;

        if( template is null || items is null || distribution is null )
          continue;

        var nightVisionWeight = WeightOfNightVisionAt( items, distribution, nightVisionTpls, cotiTpl );
        if( nightVisionWeight <= 0 )
          continue;

        var composedKey = new MongoId().ToString();

        template.Items = new List<SptLootItem>( items )
        {
          new SptLootItem { Id = new MongoId(), Template = cotiTpl, ComposedKey = composedKey }
        };

        spawnpoint.ItemDistribution = new List<LooseLootItemDistribution>( distribution )
        {
          new LooseLootItemDistribution
          {
            ComposedKey = new ComposedKey { Key = composedKey },
            RelativeProbability = nightVisionWeight * config.Loot.WeightFraction
          }
        };

        added++;
      }

      Report( name, "loose positions", added );

      return looseLoot;
    } );
  }

  /// <summary>
  /// Once per map per pool. A transformer runs on every read of the lazy-loaded table, so an
  /// unguarded line would repeat for the life of the server. Keyed on the parts rather than the
  /// formatted line, so a repeat read builds no string.
  /// </summary>
  private void Report( string map, string pool, int added )
  {
    // Two maps' tables can load at once on different threads.
    lock( _reported )
    {
      if( !_reported.Add( ( map, pool ) ) )
        return;
    }

    logger.Debug( $"[COTI] {map} {pool}: COTI added to {added}" );
  }

  private readonly HashSet<(string Map, string Pool)> _reported = new();

  /// <summary>
  /// Zero when the position already holds the COTI or holds no night vision. The distribution names a
  /// composedKey, not a template, so keys resolve back through the item list.
  /// </summary>
  private static double WeightOfNightVisionAt(
      IEnumerable<SptLootItem> items,
      IEnumerable<LooseLootItemDistribution> distribution,
      HashSet<MongoId> nightVisionTpls,
      MongoId cotiTpl )
  {
    HashSet<string>? nightVisionKeys = null;

    foreach( var item in items )
    {
      if( item.Template == cotiTpl )
        return 0;

      if( !string.IsNullOrEmpty( item.ComposedKey ) && nightVisionTpls.Contains( item.Template ) )
        ( nightVisionKeys ??= new HashSet<string>() ).Add( item.ComposedKey );
    }

    if( nightVisionKeys is null )
      return 0;

    double weight = 0;

    foreach( var entry in distribution )
    {
      if( entry.ComposedKey?.Key is { } key && nightVisionKeys.Contains( key ) )
        weight += entry.RelativeProbability ?? 0;
    }

    return weight;
  }

  /// <summary>
  /// Zero when the container already holds the COTI. Summed in double and narrowed once, as
  /// Enumerable.Sum over a float selector does.
  /// </summary>
  private static float StaticNightVisionWeight(
      IEnumerable<ItemDistribution> distribution, HashSet<MongoId> nightVisionTpls, MongoId cotiTpl )
  {
    double weight = 0;

    foreach( var entry in distribution )
    {
      if( entry.Tpl == cotiTpl )
        return 0;

      if( nightVisionTpls.Contains( entry.Tpl ) )
        weight += entry.RelativeProbability ?? 0;
    }

    return (float) weight;
  }
}
