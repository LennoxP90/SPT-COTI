#if !SPT40
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace Coti.Server;

/// <summary>
/// The red dot thermal riser: a Picatinny riser that sits under a supported red dot.
/// 4.1 only; the 4.0 line is retired.
/// </summary>
public static class ThermalRiser
{
  /// <summary>One riser colourway: its own template and bundle, everything else shared.</summary>
  public sealed record Colourway( string TplId, string BundleKey, string Name, string NewItemName );

  /// <summary>
  /// Black, then tan: the same riser in two colours, as separate items like the HHS-1 and HHS-1 Tan. Each BundleKey
  /// must match bundles.json and the Unity BuildThermalRiserPrefab.Colourways.
  /// </summary>
  public static readonly Colourway[] Colourways =
  [
    new( Coti.Shared.CotiIds.RiserTplId, "coti/thermal_riser.bundle", "LennoxP90 TR-1 thermal sight riser",
         "mount_all_lennoxp90_thermal_riser" ),
    new( Coti.Shared.CotiIds.RiserTanTplId, "coti/thermal_riser_tan.bundle", "LennoxP90 TR-1 thermal sight riser (Tan)",
         "mount_all_lennoxp90_thermal_riser_tan" ),
  ];

  /// <summary>LaRue Picatinny riser QD LT101: the most widely accepted single-slot scope riser.</summary>
  public const string DonorTplId = "5c064c400db834001d23f468";

  public const string TacticalComboClassId = "55818b164bdc2ddc698b456c";
  public const string HandbookParentId = "5b5f755f86f77447ec5d770e"; // Mounts (the donor's own category)
}

/// <summary>Registers a template per riser colourway. Custom items must exist by Preload (4.1.3).</summary>
[Injectable( TypePriority = CotiLoadOrder.Preload + 1 )]
public class ThermalRiserItemFactory( ISptLogger<ThermalRiserItemFactory> logger, CustomItemService customItemService ) : IOnLoad
{
  private const string ShortName = "TR-1";
  private const string Description =
      "Picatinny sight riser with an uncooled thermal core in its housing. Pogo contacts in its top rail feed the " +
      "thermal image into the red dot or sight mounted on it. Runs on one 18650 cell. Made by LennoxP90 Industries.";

  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    foreach( var colourway in ThermalRiser.Colourways )
      Create( colourway );

    return Task.CompletedTask;
  }

  private void Create( ThermalRiser.Colourway colourway )
  {
    var details = new NewItemFromCloneDetails
    {
      ItemTplToClone = new MongoId( ThermalRiser.DonorTplId ),
      NewId = new MongoId( colourway.TplId ),
      ParentId = new MongoId( ThermalRiser.TacticalComboClassId ),
      NewItemName = colourway.NewItemName,
      HandbookParentId = ThermalRiser.HandbookParentId,
      HandbookPriceRoubles = 95000,
      FleaPriceRoubles = 95000,
      AddToHandbook = true,
      AddToFleaPriceDb = true,
      AddToWeaponShelf = false,
      OverrideProperties = new TemplateItemProperties
      {
        Name = colourway.Name,
        ShortName = ShortName,
        Description = Description,
        Weight = 0.32,
        Width = 2,
        Height = 1,
        ModesCount = new ListOrT<int>( null, 1 ),
        Prefab = new Prefab { Path = colourway.BundleKey, Rcid = "" },
      },
      Locales = new Dictionary<string, LocaleDetails>
      {
        ["en"] = new LocaleDetails { Name = colourway.Name, ShortName = ShortName, Description = Description },
      },
    };

    var result = customItemService.CreateItemFromClone( details );
    if( result.Success != true )
      logger.Error( $"[COTI] Thermal riser {colourway.TplId} not created: {string.Join( "; ", result.Errors ?? [] )}" );
    else
      logger.Success( $"[COTI] Thermal riser created, id {colourway.TplId} ({colourway.Name})" );
  }
}

/// <summary>
/// After every mod has loaded, for every riser colourway: a place in every slot that takes the donor riser, a conflict
/// with every rear sight (anything any rear sight slot accepts, see CotiRiserSightRule.IsRearSightSlot), and the riser's own scope filter,
/// found by rule, minus config/riser-sights.json.
/// Singleton so the Riser sights page sees the same RuleMatches.
/// </summary>
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 25 )]
public class ThermalRiserCompatibility(
    ISptLogger<ThermalRiserCompatibility> logger, CotiTemplateTable templateTable, ItemHelper itemHelper,
    ThermalRiserDenyList denyList ) : IOnLoad
{
  private static readonly MongoId[] RedDotClasses = [BaseClasses.COLLIMATOR, BaseClasses.COMPACT_COLLIMATOR];

  // Ring-free scopes: a peer of these classes clamps on the rail with its own base. SpecialScope (thermal and night
  // vision sights) stays out.
  private static readonly MongoId[] ScopeClasses = [BaseClasses.ASSAULT_SCOPE, BaseClasses.OPTIC_SCOPE];

  /// <summary>What the rule accepted before the deny list. Empty until PostLoad + 25 has run.</summary>
  public IReadOnlyCollection<MongoId> RuleMatches { get; private set; } = [];

  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    var items = templateTable.Items;
    var donorId = new MongoId( ThermalRiser.DonorTplId );
    var risers = new List<TemplateItem>();
    foreach( var colourway in ThermalRiser.Colourways )
    {
      if( items.TryGetValue( new MongoId( colourway.TplId ), out var riser ) && riser.Properties?.Slots is not null )
        risers.Add( riser );
      else
        logger.Error( $"[COTI] Thermal riser template {colourway.TplId} missing - compatibility not applied to it" );
    }

    if( risers.Count == 0 )
      return Task.CompletedTask;

    var riserIds = risers.Select( r => r.Id ).ToHashSet();

    // Peers: everything that shares a slot filter with the donor, so anything that can sit on a Picatinny rail.
    var hosts = 0;
    var peers = new HashSet<MongoId>();
    var rearSights = new HashSet<MongoId>();
    foreach( var template in items.Values )
    {
      foreach( var slot in template.Properties?.Slots ?? [] )
      {
        foreach( var filter in slot.Properties?.Filters ?? [] )
        {
          if( filter.Filter is null ) continue;
          if( Coti.Shared.CotiRiserSightRule.IsRearSightSlot( slot.Name ) ) rearSights.UnionWith( filter.Filter );
          if( riserIds.Contains( template.Id ) || !filter.Filter.Contains( donorId ) ) continue;
          peers.UnionWith( filter.Filter );
          var added = false;
          foreach( var id in riserIds )
            added |= filter.Filter.Add( id );
          if( added ) hosts++;
        }
      }
    }

    // The deny check, like the rear sight one, applies to the peer only, so removing denied ids afterwards gives the
    // same set as excluding them inside Accept.
    RuleMatches = Coti.Shared.CotiRiserSightRule.Accept( peers, rearSights.Contains, IsRedDot, IsMount, IsScope,
        IsMagnified, ScopeHeld, Held );
    var denied = ReadDenied( items );
    var accepted = RuleMatches.Where( id => !denied.Contains( id ) ).ToHashSet();

    // Each colourway gets its own copy of both sets, so a later edit to one riser's sets leaves the other's alone.
    foreach( var riser in risers )
    {
      var scopeFilter = riser.Properties!.Slots!.FirstOrDefault( s => s.Name == "mod_scope" )?.Properties?.Filters?.FirstOrDefault();
      if( scopeFilter is not null )
        scopeFilter.Filter = [..accepted];
      else
        logger.Error( $"[COTI] Thermal riser {riser.Id} has no mod_scope filter, so it takes no sight" );

      riser.Properties.ConflictingItems = [..rearSights];
    }

    // Beyond the LaRue set: every rail scope slot that takes something the riser takes.
    var extraHosts = 0;
    foreach( var template in items.Values )
    {
      foreach( var slot in template.Properties?.Slots ?? [] )
      {
        foreach( var filter in slot.Properties?.Filters ?? [] )
        {
          if( filter.Filter is null || filter.Filter.Contains( donorId ) ||
              !Coti.Shared.CotiRiserSightRule.IsExtraHostSlot( slot.Name, template.Id, filter.Filter, IsSight, accepted ) )
            continue;
          var added = false;
          foreach( var id in riserIds )
            added |= filter.Filter.Add( id );
          if( added ) extraHosts++;
        }
      }
    }

    // One line with a count per mod; each id only at debug level, for chasing a sight that is missing.
    var bySource = accepted.GroupBy( id => SourceOf( items[id].Name ) ).OrderBy( g => g.Key )
        .Select( g => $"{g.Key} {g.Count()}" );
    logger.Success( $"[COTI] Thermal riser ({risers.Count} colourways): {accepted.Count} sights and mounts " +
                    $"({string.Join( ", ", bySource )}), fits {hosts} slots with the LaRue and {extraHosts} more scope " +
                    $"slots, conflicts with {rearSights.Count} rear sights" );
    foreach( var id in accepted.OrderBy( id => items[id].Name ) )
      logger.Debug( $"[COTI] Thermal riser accepts {id} {items[id].Name}" );
    return Task.CompletedTask;

    // ContainsKey first: ItemHelper warns about every id it cannot find, and filters can hold removed mods' ids.
    bool IsRedDot( MongoId id ) => items.ContainsKey( id ) && itemHelper.IsOfBaseclasses( id, RedDotClasses );

    // A riser counts as a sight here, so neither riser hosts the other.
    bool IsSight( MongoId id ) => riserIds.Contains( id ) ||
        ( items.ContainsKey( id ) && itemHelper.IsOfBaseclass( id, BaseClasses.SIGHTS ) );

    bool IsMount( MongoId id ) => items.ContainsKey( id ) && itemHelper.IsOfBaseclass( id, BaseClasses.MOUNT );

    bool IsScope( MongoId id ) => items.ContainsKey( id ) && itemHelper.IsOfBaseclasses( id, ScopeClasses );

    bool IsMagnified( MongoId id ) =>
        items.TryGetValue( id, out var template ) && ( IsScope( id ) ||
            Coti.Shared.CotiRiserSightRule.Magnifies( template.Properties?.Zooms, template.Properties?.SightModType ) );

    // What a mount's mod_scope* slots accept. Mod micro dots reach the riser through their mount's own filter.
    IEnumerable<MongoId> ScopeHeld( MongoId id ) => SlotHeld( id, s => s?.StartsWith( "mod_scope" ) == true );

    IEnumerable<MongoId> Held( MongoId id ) => SlotHeld( id, Coti.Shared.CotiRiserSightRule.IsHolderSlot );

    // Filters can hold removed mods' ids; an id with no template holds nothing.
    IEnumerable<MongoId> SlotHeld( MongoId id, Func<string?, bool> slot ) =>
        !items.TryGetValue( id, out var template ) ? [] :
        template.Properties?.Slots?
            .Where( s => slot( s.Name ) )
            .SelectMany( s => s.Properties?.Filters ?? [] )
            .SelectMany( f => f.Filter ?? [] ) ?? [];
  }

  /// <summary>The mod a template comes from, by the "[Mod]_(...)" name modded items carry; vanilla otherwise.</summary>
  private static string SourceOf( string? name ) =>
      name is { Length: > 2 } && name[0] == '[' && name.IndexOf( ']' ) is var end and > 1 ? name[1..end] : "vanilla";

  // A missing or broken file denies nothing rather than taking the riser down; the page shows the reason and its save
  // replaces the file.
  private HashSet<MongoId> ReadDenied( Dictionary<MongoId, TemplateItem> items )
  {
    var file = denyList.Read( out var error );
    if( file is null )
    {
      logger.Warning( $"[COTI] Thermal riser: {denyList.FilePath} not read ({error}), so no sight is denied" );
      return [];
    }

    if( error.Length > 0 )
      logger.Warning( $"[COTI] Thermal riser: {denyList.FilePath}: {error}" );

    var ids = file.Deny.Select( d => new MongoId( d.Id ) ).ToHashSet();
    logger.Info( $"[COTI] Thermal riser: {ids.Count} denied ({ids.Count( id => !items.ContainsKey( id ) )} not in this " +
                 $"database) from {denyList.FilePath}" );
    return ids;
  }
}

/// <summary>
/// config/riser-sights.json, read by ThermalRiserCompatibility and written by the ECOTI Riser sights page. The server
/// never rewrites the file itself, so a broken file stays as the owner left it.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class ThermalRiserDenyList
{
  /// <summary>Absolute. Empty when the mod folder could not be resolved.</summary>
  public string FilePath { get; } = string.Empty;

  public ThermalRiserDenyList( ISptLogger<ThermalRiserDenyList> logger, ModHelper modHelper )
  {
    try
    {
      // Explicit assembly, as in CotiServerConfig: the helper's GetCallingAssembly can change under JIT inlining.
      FilePath = System.IO.Path.Combine( modHelper.GetAbsolutePathToModFolder( typeof( ThermalRiserDenyList ).Assembly ),
          "config", ThermalRiserSightsFile.FileName );
    }
    catch( Exception ex )
    {
      logger.Error( "[COTI] Could not locate the mod folder - the thermal riser denies nothing.", ex );
    }
  }

  /// <summary>
  /// The file, or null with the reason. Never throws. A file with a non-empty error had entries skipped
  /// (ThermalRiserSightsFile.Parse).
  /// </summary>
  public ThermalRiserSightsFile? Read( out string error )
  {
    if( string.IsNullOrEmpty( FilePath ) )
    {
      error = "the mod folder could not be resolved";
      return null;
    }

    try
    {
      return ThermalRiserSightsFile.Parse( File.ReadAllText( FilePath ), out error );
    }
    catch( Exception ex )
    {
      error = ex.Message;
      return null;
    }
  }

  private readonly object writeLock = new();

  /// <summary>The file's last write time, MinValue when it does not exist. Take it before Read, pass it to TryWrite.</summary>
  public DateTime LastWriteUtc => File.Exists( FilePath ) ? File.GetLastWriteTimeUtc( FilePath ) : DateTime.MinValue;

  /// <summary>
  /// Writes to a temp file and moves it over the target, copying the old file to riser-sights.json.bak first, as
  /// CotiDeviceStore.TryWriteFile does. Takes effect on the next server start. Refused when the file changed since
  /// <paramref name="readAt"/> (another tab's save or a hand edit), so a stale page cannot overwrite it; the lock keeps
  /// two saves from sharing the temp file.
  /// </summary>
  public bool TryWrite( ThermalRiserSightsFile file, DateTime readAt, out string error )
  {
    error = string.Empty;
    if( string.IsNullOrEmpty( FilePath ) )
    {
      error = "the mod folder could not be resolved";
      return false;
    }

    lock( writeLock )
    {
      try
      {
        if( LastWriteUtc != readAt )
        {
          error = "the file changed since this page read it. Reload, then make the change again";
          return false;
        }

        Directory.CreateDirectory( System.IO.Path.GetDirectoryName( FilePath )! );
        if( File.Exists( FilePath ) )
          File.Copy( FilePath, FilePath + ".bak", overwrite: true );

        File.WriteAllText( FilePath + ".tmp", file.Serialize() );
        File.Move( FilePath + ".tmp", FilePath, overwrite: true );
        return true;
      }
      catch( Exception ex )
      {
        error = ex.Message;
        return false;
      }
    }
  }
}

#if DEBUG
/// <summary>Test builds only: Peacekeeper LL1 sells every riser colourway and scope ring for $1.</summary>
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.TraderRegistration + 31 )]
public class ThermalRiserTestAssort( ISptLogger<ThermalRiserTestAssort> logger, TradersTable tradersTable ) : IOnLoad
{
  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    if( !tradersTable.TryGetValue( Traders.PEACEKEEPER, out var peacekeeper ) || peacekeeper.Assort is null )
      return Task.CompletedTask;

    var forSale = ThermalRiser.Colourways.Select( c => (c.TplId, c.Name) )
        .Concat( ThermalScopeRing.Variants.Select( v => (v.TplId, v.Name) ) );
    foreach( var colourway in forSale )
    {
      var id = new MongoId();
      peacekeeper.Assort.Items.Add( new Item
      {
        Id = id,
        Template = new MongoId( colourway.TplId ),
        ParentId = "hideout",
        SlotId = "hideout",
        Upd = new Upd { StackObjectsCount = 9999999, UnlimitedCount = true },
      } );
      peacekeeper.Assort.BarterScheme[id] =
          [[new BarterScheme { Count = 1, Template = ItemTpl.MONEY_DOLLARS }]];
      peacekeeper.Assort.LoyalLevelItems[id] = 1;
      logger.Warning( $"[COTI] TEST BUILD: {colourway.Name} on Peacekeeper LL1 for $1" );
    }
    return Task.CompletedTask;
  }
}
#endif
#endif
