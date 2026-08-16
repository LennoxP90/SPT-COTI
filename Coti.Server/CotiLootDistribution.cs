using Coti.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Coti.Server;

/// <summary>
/// Spawns the COTI wherever night vision already spawns, at a fraction of its weight. NVGs come from
/// the item database, not <see cref="CotiNvgHosts"/>, so modded ones count too.
/// </summary>
[Injectable( TypePriority = OnLoadOrder.PostLoad + 60 )]
public class CotiLootDistribution(
    ISptLogger<CotiLootDistribution> logger,
    CotiServerConfig config,
    TemplateTable templateTable,
    LocationTable locationTable ) : IOnLoad
{
  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    if( !config.Loot.Enabled || config.Loot.WeightFraction <= 0 )
    {
      logger.Success( "[COTI] Loot spawns disabled by config - the trader is the only source" );
      return Task.CompletedTask;
    }

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

    logger.Success(
        $"[COTI] Loot spawns registered against {nightVisionTpls.Count} night vision template(s) " +
        $"at {config.Loot.WeightFraction:P0} of their weight" );

    return Task.CompletedTask;
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
  /// mutation would be discarded by the next lookup.
  /// </summary>
  private void AddToStaticLoot( string name, Location location, HashSet<MongoId> nightVisionTpls, MongoId cotiTpl )
  {
    location.StaticLoot?.AddTransformer( staticLoot =>
    {
      if( staticLoot is null )
        return staticLoot;

      var added = 0;

      foreach( var container in staticLoot.Values )
      {
        var distribution = container.ItemDistribution?.ToList();
        if( distribution is null || distribution.Any( entry => entry.Tpl == cotiTpl ) )
          continue;

        var nightVisionWeight = distribution
            .Where( entry => nightVisionTpls.Contains( entry.Tpl ) )
            .Sum( entry => entry.RelativeProbability ?? 0 );

        if( nightVisionWeight <= 0 )
          continue;

        distribution.Add( new ItemDistribution
        {
          Tpl = cotiTpl,
          RelativeProbability = (float)( nightVisionWeight * config.Loot.WeightFraction )
        } );

        container.ItemDistribution = distribution;
        added++;
      }

      Report( $"{name} containers", added );

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
      if( looseLoot?.Spawnpoints is null )
        return looseLoot;

      var added = 0;

      foreach( var spawnpoint in looseLoot.Spawnpoints )
      {
        var template = spawnpoint.Template;
        var items = template?.Items?.ToList();
        var distribution = spawnpoint.ItemDistribution?.ToList();

        if( template is null || items is null || distribution is null )
          continue;
        if( items.Any( item => item.Template == cotiTpl ) )
          continue;

        var nightVisionWeight = WeightOfNightVisionAt( items, distribution, nightVisionTpls );
        if( nightVisionWeight <= 0 )
          continue;

        var composedKey = new MongoId().ToString();

        items.Add( new SptLootItem
        {
          Id = new MongoId(),
          Template = cotiTpl,
          ComposedKey = composedKey
        } );

        distribution.Add( new LooseLootItemDistribution
        {
          ComposedKey = new ComposedKey { Key = composedKey },
          RelativeProbability = nightVisionWeight * config.Loot.WeightFraction
        } );

        template.Items = items;
        spawnpoint.ItemDistribution = distribution;
        added++;
      }

      Report( $"{name} loose positions", added );

      return looseLoot;
    } );
  }

  /// <summary>
  /// Once per map per pool. A transformer runs on every read of the lazy-loaded table, so an
  /// unguarded line would repeat for the life of the server.
  /// </summary>
  private void Report( string what, int added )
  {
    if( !_reported.Add( what ) )
      return;

    logger.Success( $"[COTI] {what}: COTI added to {added}" );
  }

  private readonly HashSet<string> _reported = new();

  /// <summary>
  /// The distribution names a composedKey, not a template, so keys resolve back through the item list.
  /// </summary>
  private static double WeightOfNightVisionAt(
      List<SptLootItem> items,
      List<LooseLootItemDistribution> distribution,
      HashSet<MongoId> nightVisionTpls )
  {
    var nightVisionKeys = items
        .Where( item => nightVisionTpls.Contains( item.Template ) )
        .Select( item => item.ComposedKey )
        .Where( key => !string.IsNullOrEmpty( key ) )
        .ToHashSet();

    if( nightVisionKeys.Count == 0 )
      return 0;

    return distribution
        .Where( entry => entry.ComposedKey?.Key is not null && nightVisionKeys.Contains( entry.ComposedKey.Key ) )
        .Sum( entry => entry.RelativeProbability ?? 0 );
  }
}
