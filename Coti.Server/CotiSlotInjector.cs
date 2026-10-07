using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Coti.Server;

/// <summary>
/// What happened when a single host was offered to <see cref="CotiSlotInjector.InjectInto"/>.
/// The load-time loop and the dynamic callers (CotiHostDiscovery, CotiDevicePublisher) react
/// differently to the same outcome - AlreadyPresent is notable at load and routine on every
/// republish - so the method reports what happened and leaves the reaction to the caller.
/// </summary>
public enum CotiInjectOutcome
{
  Added,
  AlreadyPresent,
  NotInstalled,
  NoSlotsCollection,
  InvalidId
}

/// <summary>
/// Runs late. Other mods rewrite NVG templates (AttachmentBackport, Tarkov-1.0-Backport both
/// touch mounts) and a slot added before they run can be discarded.
/// </summary>
// Singleton: InjectInto holds no per-instance state, and the dynamic callers take this type as a
// constructor dependency to call InjectInto on demand.
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 20 )]
public class CotiSlotInjector : IOnLoad
{
  private readonly ISptLogger<CotiSlotInjector> logger;
  private readonly CotiDeviceStore deviceStore;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long
  // before that - so the table is resolved on use, inside OnLoad, never in the constructor.
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;

  public CotiSlotInjector(
      ISptLogger<CotiSlotInjector> logger, DatabaseServer databaseServer, CotiDeviceStore deviceStore )
  {
    this.logger = logger;
    this.databaseServer = databaseServer;
    this.deviceStore = deviceStore;
  }
#else
  private readonly CotiTemplateTable templateTable;

  public CotiSlotInjector(
      ISptLogger<CotiSlotInjector> logger, CotiTemplateTable templateTable, CotiDeviceStore deviceStore )
  {
    this.logger = logger;
    this.templateTable = templateTable;
    this.deviceStore = deviceStore;
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
    var added = 0;

    // One snapshot, read once: the host table and the two census counts below have to describe
    // the same resolve pass. See CotiDeviceSnapshot.
    var snapshot = deviceStore.Current;

    foreach( var ( hostId, resolved ) in snapshot.ByHostId )
    {
      var label = ResolveLabel( resolved, hostId );
      var outcome = InjectInto( hostId, label, LayoutOf( resolved.Device ) );

      switch( outcome )
      {
        case CotiInjectOutcome.Added:
          added++;
          break;

        case CotiInjectOutcome.AlreadyPresent:
          // At load time the device store just resolved this host, so an existing slot is
          // unexpected. The dynamic callers see this outcome routinely on every save.
          logger.Warning( $"[COTI] Host {hostId} already has its COTI slots - skipped" );
          break;
      }
    }

    LogLoadSummary( snapshot, added );

    return Task.CompletedTask;
  }

  /// <summary>
  /// One line per start. A store that declared no host entries checked nothing (an empty or
  /// missing nvghostcompat/, a staging failure, an unreadable directory); a store that declared
  /// entries and fitted none means the item exists and is purchasable but nothing can mount it.
  /// ByHostId only holds hosts that resolved, so the not-installed count comes from the store's
  /// census over every host entry it declared eligible.
  /// </summary>
  private void LogLoadSummary( CotiDeviceSnapshot snapshot, int added )
  {
    if( snapshot.DeclaredHostCount == 0 )
    {
      logger.Warning(
          $"[COTI] Device store declared no host entries to check - {deviceStore.FolderPath} is " +
          $"missing, empty, or every file in it failed to load. See the warnings above for why." );
    }
    else if( added == 0 )
    {
      logger.Warning(
          $"[COTI] No supported night vision device is installed - the ECOTI has nothing to " +
          $"clip to. Checked {snapshot.DeclaredHostCount} host(s)." );
    }
    else
    {
      logger.Success( $"[COTI] {added} host(s) fitted, {snapshot.UnresolvedHostCount} not installed" );
    }
  }

  /// <summary>
  /// The layout whose slots a device's hosts get, or null for a v1 device (mod_coti only). Read
  /// after CotiDeviceMerge, so a file whose layout or tubes it rejected is v1 here too.
  /// </summary>
  public static CotiLayout? LayoutOf( CotiDeviceFile device )
  {
    return device.IsMultiTube && CotiLayouts.TryGet( device.Layout, out var layout ) ? layout : null;
  }

  /// <summary>
  /// Mutates the live template table so <paramref name="hostId"/> has every COTI slot of
  /// <paramref name="layout"/> (mod_coti alone when it is null), and reports what happened - see
  /// <see cref="CotiInjectOutcome"/>. Every outcome except AlreadyPresent logs here, because every
  /// caller wants the same line; Added and NotInstalled log at Debug, since each caller writes its
  /// own summary. AlreadyPresent (every slot already there) is silent because the right reaction to
  /// it differs by caller - see its call site in <see cref="LoadAsync"/>.
  /// </summary>
  public CotiInjectOutcome InjectInto( string hostId, string label, CotiLayout? layout )
  {
    if( !MongoId.IsValidMongoId( hostId ) )
    {
      logger.Warning( $"[COTI] Host key \"{hostId}\" is not a valid MongoId - skipped" );
      return CotiInjectOutcome.InvalidId;
    }

    var items = templateTable.Items;

    // Normal case: some hosts come from optional mods (the PVS-31A is a separate mod), so a
    // supported host that is not installed is logged at Debug only.
    if( !items.TryGetValue( new MongoId( hostId ), out var host ) )
    {
      logger.Debug( $"[COTI] {label} ({hostId}) not installed - skipped" );
      return CotiInjectOutcome.NotInstalled;
    }

    if( host.Properties?.Slots is null )
    {
      logger.Warning( $"[COTI] Host {hostId} has no Slots collection - skipped" );
      return CotiInjectOutcome.NoSlotsCollection;
    }

    // Appended, never stopped at mod_coti: a device upgraded to more tubes and republished at
    // runtime has mod_coti and still needs the rest. EFT fills the first free slot that takes the
    // item, so they go in the auto-pick order SlotNames returns.
    var present = host.Properties.Slots.Select( s => s.Name ).ToHashSet();
    var missing = CotiTubes.SlotNames( layout ).Where( name => !present.Contains( name ) ).ToList();

    if( missing.Count == 0 )
      return CotiInjectOutcome.AlreadyPresent;

    // Atomic reference swap rather than an in-place add. The dynamic path can inject while
    // another client is serialising /client/items, and that walk is lazy: the route returns
    // StreamedJsonBody, which holds a reference rather than bytes, so the window spans the whole
    // multi-megabyte download. Assigning a fresh list means the serialiser's
    // enumerator holds either the old list or the new one, never a torn one. Do not replace
    // this with Slots.Add.
    var slots = host.Properties.Slots.ToList();
    slots.AddRange( missing.Select( name => NewSlot( name, hostId ) ) );

    host.Properties.Slots = slots;

    // Uses the device's display name: the template's ShortName is BSG's internal one and is
    // Russian for some items (the PVS-14's is "ПНВ"). English lives in the locale files.
    logger.Debug( $"[COTI] {string.Join( ", ", missing )} added to {label} ({hostId})" );
    return CotiInjectOutcome.Added;
  }

  private static Slot NewSlot( string name, string hostId )
  {
    return new Slot
    {
      Name = name,
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
    };
  }

  /// <summary>
  /// The declared entry comes off the resolved host itself rather than a lookup by id in
  /// device.Hosts: a host recovered by prefab fallback has a resolved id that appears nowhere in
  /// the file, so an id lookup would lose its Label.
  /// </summary>
  private static string ResolveLabel( CotiResolvedHost resolved, string hostId )
  {
    return resolved.Declared.Label ?? resolved.Device.DisplayName ?? resolved.Device.Device ?? hostId;
  }
}
