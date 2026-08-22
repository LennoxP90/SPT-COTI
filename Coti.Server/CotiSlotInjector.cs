using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Coti.Server;

/// <summary>
/// What happened when a single host was offered to <see cref="CotiSlotInjector.InjectInto"/>.
/// The load-time loop and the dynamic callers in Tasks 10 and 11 need different reactions to the
/// same outcome - most visibly AlreadyPresent, which is unremarkable once and spam on every
/// republish - so the method reports what happened rather than deciding what to say about it.
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
/// Runs deliberately late. Other mods rewrite NVG templates (AttachmentBackport,
/// Tarkov-1.0-Backport both touch mounts) and a slot added before they run can be discarded.
/// </summary>
// Singleton, not the default Transient - InjectInto holds no per-instance state (the logger and
// the template table are both already shared regardless of lifetime), and Tasks 10 and 11 both
// take this type as a constructor dependency to call InjectInto on demand. There is nothing a
// fresh instance per resolution buys here, so one shared instance avoids the pointless
// reallocation - the same reasoning CotiDeviceStore states for its own Singleton.
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
      var outcome = InjectInto( hostId, label );

      switch( outcome )
      {
        case CotiInjectOutcome.Added:
          added++;
          break;

        case CotiInjectOutcome.AlreadyPresent:
          // Correct at load time: the device store just resolved this host fresh, so a slot
          // already sitting on it is unexpected, not the routine republish that the dynamic
          // callers in Tasks 10 and 11 hit on every save.
          logger.Warning( $"[COTI] Host {hostId} already has mod_coti - skipped" );
          break;
      }
    }

    // ByHostId only ever holds hosts that resolved, so it cannot tell us how many did not - that
    // count comes from the store's own census, over every host entry it declared eligible.
    var notInstalled = snapshot.UnresolvedHostCount;

    // Two different failures, and pointing someone at the wrong one wastes their time. A store
    // that declared zero host entries checked nothing at all - an empty or missing
    // nvghostcompat/, a staging failure, a directory the server cannot read - and needs the
    // folder path, not a shrug. A store that declared real entries and still fitted none is the
    // ordinary case below: a healthy install can genuinely have no supported device plugged in.
    if( snapshot.DeclaredHostCount == 0 )
    {
      logger.Warning(
          $"[COTI] Device store declared no host entries to check - {deviceStore.FolderPath} is " +
          $"missing, empty, or every file in it failed to load. See the warnings above for why." );
    }
    // Zero IS worth warning about: the item exists and is purchasable, but nothing can mount it.
    else if( added == 0 )
    {
      logger.Warning(
          $"[COTI] No supported night vision device is installed - the ECOTI has nothing to " +
          $"clip to. Checked {snapshot.DeclaredHostCount} host(s)." );
    }
    else if( notInstalled > 0 )
    {
      logger.Success( $"[COTI] {added} host(s) fitted, {notInstalled} not installed" );
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Mutates the live template table so <paramref name="hostId"/> can mount mod_coti, and reports
  /// what happened rather than deciding what to log about it - see <see cref="CotiInjectOutcome"/>.
  /// Every outcome except AlreadyPresent logs here, because that logging is identical for every
  /// caller (load-time, auto-discovery, and a published device all want the same line). Only
  /// AlreadyPresent is silent here, because it is the one outcome whose right reaction differs by
  /// caller - see the comment at its call site in <see cref="LoadAsync"/>.
  /// </summary>
  public CotiInjectOutcome InjectInto( string hostId, string label )
  {
    if( !MongoId.IsValidMongoId( hostId ) )
    {
      logger.Warning( $"[COTI] Host key \"{hostId}\" is not a valid MongoId - skipped" );
      return CotiInjectOutcome.InvalidId;
    }

    var items = templateTable.Items;

    // Not a problem. Some hosts ship with mods that are not required - the PVS-31A comes from
    // a seperate mod - so a host we support but the player does not have is the normal case,
    // and warning about it makes a healthy install look broken.
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

    if( host.Properties.Slots.Any( s => s.Name == CotiIds.ModSlotName ) )
      return CotiInjectOutcome.AlreadyPresent;

    // ATOMIC REFERENCE SWAP, NOT AN IN-PLACE ADD. The dynamic path can inject while another
    // client is mid-serialisation of /client/items, and on 4.1.3 that walk is LAZY - the
    // route returns StreamedJsonBody, which holds a reference rather than bytes, so the
    // window spans the whole download of a multi-megabyte payload rather than a synchronous
    // instant. Assigning a fresh list means the serialiser's enumerator holds either the old
    // list or the new one and never a torn one. Do not "simplify" this to Slots.Add.
    var slots = host.Properties.Slots.ToList();
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

    // Our own display name, not the template's ShortName - that field is BSG's internal one and
    // is Russian for some items (the PVS-14 logs as "ПНВ"). English lives in the locale files.
    logger.Success( $"[COTI] mod_coti added to {label} ({hostId})" );
    return CotiInjectOutcome.Added;
  }

  /// <summary>
  /// The declared entry comes off the resolved host itself rather than being looked up by id in
  /// device.Hosts. That lookup was wrong for exactly the hosts a prefab fallback recovered: their
  /// resolved id appears nowhere in the file, so it matched nothing and the per-host Label was
  /// lost on the one path that most needs a readable name in the log.
  /// </summary>
  private static string ResolveLabel( CotiResolvedHost resolved, string hostId )
  {
    return resolved.Declared.Label ?? resolved.Device.DisplayName ?? resolved.Device.Device ?? hostId;
  }
}
