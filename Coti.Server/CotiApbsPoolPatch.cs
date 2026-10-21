#if !SPT40
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Coti.Server;

/// <summary>
/// Puts the thermal riser and the scope rings into APBS's bot attachment pools, which APBS builds from its own data and
/// a curated optics list, so neither ever reaches a bot through APBS otherwise. Postfixes APBS's
/// BotEquipmentHelper.GetTierMods (the pools a bot is built from): wherever a parent's slot filter takes one of ours, it
/// joins that slot's pool, and each gets its base item's own pools (a ring the Era-Tac's or IMS's, the riser the
/// LaRue's, kept to what its filters take). CotiBotStripPatch then keeps each with riser.loot.botChance. Without APBS
/// the target does not exist and nothing is patched.
/// </summary>
[Injectable( TypePriority = CotiLoadOrder.PostLoad + 27 )]
public class CotiApbsPoolPatch( ISptLogger<CotiApbsPoolPatch> logger, CotiTemplateTable templates, CotiServerConfig config )
    : AbstractPatch, IOnLoad
{
  private const string HelperType = "ProgressiveBotSystem.Helpers.BotEquipmentHelper";

  private static CotiTemplateTable _templates = default!;
  private static CotiServerConfig _config = default!;

  // A pool's slot table is shared by every bot of its tier until APBS reloads its data, so each is filled once.
  private static readonly ConditionalWeakTable<Dictionary<string, HashSet<MongoId>>, object> Filled = new();

  /// <summary>Our items with the item whose pools each borrows.</summary>
  private static readonly (MongoId Ours, MongoId Base)[] Ours =
      ThermalScopeRing.Variants.Select( v => ( new MongoId( v.TplId ), new MongoId( v.DonorTplId ) ) )
          .Concat( ThermalRiser.Colourways.Select( c => ( new MongoId( c.TplId ), new MongoId( ThermalRiser.DonorTplId ) ) ) )
          .ToArray();

  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    _templates = templates;
    _config = config;
    if( GetTargetMethod() is null )
    {
      logger.Info( "[COTI] APBS not installed: its bot pools are left alone" );
      return Task.CompletedTask;
    }

    Enable();
    logger.Success( "[COTI] APBS bot pools take the thermal riser and the scope rings" );
    return Task.CompletedTask;
  }

  protected override MethodBase GetTargetMethod() =>
      AccessTools.TypeByName( HelperType ) is { } type ? AccessTools.Method( type, "GetTierMods" ) : null!;

  [PatchPostfix]
  public static void Postfix( Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>> __result )
  {
    if( __result is null )
      return;

    var force = Forced();
    foreach( var ( parentId, slots ) in __result )
    {
      // APBS generates bots in parallel, so the check and the mark must be one atomic step.
      if( !_templates.Items.TryGetValue( parentId, out var parent ) || !Filled.TryAdd( slots, new object() ) )
        continue;
      foreach( var ( slotName, pool ) in slots )
      {
        var takes = Accepted( parent, slotName );
        var added = Ours.Select( o => o.Ours ).Where( takes.Contains ).ToList();
        if( added.Count == 0 )
          continue;
        if( force )
          pool.Clear();
        pool.UnionWith( added );
      }
    }

    // Our own pools, on this bot's copy of the table: the base item's, kept to what our slot filters take.
    foreach( var ( ours, @base ) in Ours )
    {
      if( __result.ContainsKey( ours ) || !__result.TryGetValue( @base, out var basePools )
          || !_templates.Items.TryGetValue( ours, out var template ) )
        continue;
      __result[ours] = basePools.ToDictionary( s => s.Key,
          s => s.Value.Where( Accepted( template, s.Key ).Contains ).ToHashSet() );
    }
  }

  private static HashSet<MongoId> Accepted( TemplateItem template, string slotName ) =>
      template.Properties?.Slots?.FirstOrDefault( s => s.Name == slotName )?.Properties?.Filters?
          .SelectMany( f => f.Filter ?? [] ).ToHashSet() ?? [];

  /// <summary>
  /// Test builds only: riser.loot.botChance at 1 or more makes a pool that takes one of ours offer only ours, so APBS
  /// bots carry them on demand and the bot rules can be checked in a raid.
  /// </summary>
  private static bool Forced() =>
#if DEBUG
      _config.Riser.Loot.BotChance >= 1;
#else
      false;
#endif
}
#endif
