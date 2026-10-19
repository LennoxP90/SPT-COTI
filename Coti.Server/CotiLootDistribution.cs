using Coti.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Coti.Server;

/// <summary>
/// Spawns the COTI wherever night vision already spawns, at a fraction of its weight, and on 4.1 the
/// thermal riser the same way at its own fraction (riser.loot), split evenly between its colourways.
/// Each has its own switch. NVGs come from the item database, not <see cref="CotiDeviceStore"/>, so
/// modded ones count too.
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

  private static readonly MongoId CotiTpl = new( CotiItemFactory.CotiTplId );

  private Task LoadAsync( CancellationToken cancellationToken )
  {
    var nightVisionTpls = GetNightVisionTpls();
    if( nightVisionTpls.Count == 0 )
    {
      logger.Warning( "[COTI] No night vision templates found - the COTI and the riser will not spawn in loot" );
      return Task.CompletedTask;
    }

    foreach( var entry in locationTable.GetDictionary() )
    {
      AddToStaticLoot( entry.Key, entry.Value, nightVisionTpls );
      AddToLooseLoot( entry.Key, entry.Value, nightVisionTpls );
    }

    var coti = config.Loot.Enabled && config.Loot.WeightFraction > 0
        ? $"COTI at {config.Loot.WeightFraction:P0} of their weight"
        : "COTI disabled by config";
#if SPT40
    var riser = string.Empty;
#else
    var riser = config.Riser.Loot.Enabled && config.Riser.Loot.WeightFraction > 0
        ? $", thermal riser at {config.Riser.Loot.WeightFraction:P0}"
        : ", thermal riser disabled by config";
#endif

    logger.Success( $"[COTI] Loot spawns registered against {nightVisionTpls.Count} night vision template(s): " +
                    $"{coti}{riser}. The transformers are registered either way, so switching one on takes " +
                    "effect at the next raid." );

    return Task.CompletedTask;
  }

  /// <summary>
  /// What each spot gets: a template and its fraction of the night vision weight there. Read inside
  /// the transformers, not at load: they are registered unconditionally, and a switch takes effect
  /// at the next raid. Empty when everything is switched off.
  /// </summary>
  private List<(MongoId Tpl, double Fraction)> Additions()
  {
    var additions = new List<(MongoId Tpl, double Fraction)>();

    if( config.Loot.Enabled && config.Loot.WeightFraction > 0 )
      additions.Add( ( CotiTpl, config.Loot.WeightFraction ) );

#if !SPT40
    // The riser is 4.1 only; on 4.0 its templates never exist.
    var riser = config.Riser.Loot;
    if( riser.Enabled && riser.WeightFraction > 0 )
    {
      foreach( var tpl in CotiIds.RiserTplIds )
        additions.Add( ( new MongoId( tpl ), riser.WeightFraction / CotiIds.RiserTplIds.Length ) );
    }
#endif

    return additions;
  }

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
  /// mutation would be discarded by the next lookup. A template the container already lists is not
  /// added again.
  /// </summary>
  private void AddToStaticLoot( string name, Location location, HashSet<MongoId> nightVisionTpls )
  {
    location.StaticLoot?.AddTransformer( staticLoot =>
    {
      var additions = Additions();
      if( staticLoot is null || additions.Count == 0 )
        return staticLoot;

      var added = 0;

      foreach( var container in staticLoot.Values )
      {
        var distribution = container.ItemDistribution?.ToList();
        if( distribution is null )
          continue;

        var nightVisionWeight = StaticNightVisionWeight( distribution, nightVisionTpls );
        if( nightVisionWeight <= 0 )
          continue;

        var present = distribution.Select( entry => entry.Tpl ).ToHashSet();
        var extended = new List<ItemDistribution>( distribution );

        foreach( var ( tpl, fraction ) in additions )
        {
          if( present.Contains( tpl ) )
            continue;

          extended.Add( new ItemDistribution
          {
            Tpl = tpl,
            RelativeProbability = (float)( nightVisionWeight * fraction )
          } );
        }

        if( extended.Count == distribution.Count )
          continue;

        container.ItemDistribution = extended;
        added++;
      }

      Report( name, "containers", added );

      return staticLoot;
    } );
  }

  /// <summary>
  /// A position needs each added item in both its item list and its composedKey distribution; either
  /// alone is skipped. It spawns one item, so what is added takes weight from everything else there.
  /// A template the position already lists is not added again.
  /// </summary>
  private void AddToLooseLoot( string name, Location location, HashSet<MongoId> nightVisionTpls )
  {
    location.LooseLoot?.AddTransformer( looseLoot =>
    {
      var additions = Additions();
      if( looseLoot?.Spawnpoints is null || additions.Count == 0 )
        return looseLoot;

      var added = 0;

      foreach( var spawnpoint in looseLoot.Spawnpoints )
      {
        var template = spawnpoint.Template;
        var items = template?.Items?.ToList();
        var distribution = spawnpoint.ItemDistribution;

        if( template is null || items is null || distribution is null )
          continue;

        var nightVisionWeight = WeightOfNightVisionAt( items, distribution, nightVisionTpls );
        if( nightVisionWeight <= 0 )
          continue;

        var present = items.Select( item => item.Template ).ToHashSet();
        var newItems = new List<SptLootItem>( items );
        var newDistribution = new List<LooseLootItemDistribution>( distribution );

        foreach( var ( tpl, fraction ) in additions )
        {
          if( present.Contains( tpl ) )
            continue;

          var composedKey = new MongoId().ToString();
          newItems.Add( new SptLootItem { Id = new MongoId(), Template = tpl, ComposedKey = composedKey } );
          newDistribution.Add( new LooseLootItemDistribution
          {
            ComposedKey = new ComposedKey { Key = composedKey },
            RelativeProbability = nightVisionWeight * fraction
          } );
        }

        if( newItems.Count == items.Count )
          continue;

        template.Items = newItems;
        spawnpoint.ItemDistribution = newDistribution;
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

    logger.Debug( $"[COTI] {map} {pool}: COTI or riser added to {added}" );
  }

  private readonly HashSet<(string Map, string Pool)> _reported = new();

  /// <summary>
  /// Zero when the position holds no night vision. The distribution names a composedKey, not a
  /// template, so keys resolve back through the item list.
  /// </summary>
  private static double WeightOfNightVisionAt(
      IEnumerable<SptLootItem> items,
      IEnumerable<LooseLootItemDistribution> distribution,
      HashSet<MongoId> nightVisionTpls )
  {
    HashSet<string>? nightVisionKeys = null;

    foreach( var item in items )
    {
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
  /// Summed in double and narrowed once, as Enumerable.Sum over a float selector does.
  /// </summary>
  private static float StaticNightVisionWeight(
      IEnumerable<ItemDistribution> distribution, HashSet<MongoId> nightVisionTpls )
  {
    double weight = 0;

    foreach( var entry in distribution )
    {
      if( nightVisionTpls.Contains( entry.Tpl ) )
        weight += entry.RelativeProbability ?? 0;
    }

    return (float) weight;
  }
}
