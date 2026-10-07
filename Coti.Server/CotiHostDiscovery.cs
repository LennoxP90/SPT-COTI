using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
// SPTarkov.Server.Core.Models.Eft.Common.Tables also declares a type named "Path" (a lockpicking
// path record), which collides with System.IO.Path once both usings are in scope.
using Path = System.IO.Path;

namespace Coti.Server;

/// <summary>
/// Writes a seeded stub for every night vision item that has no device file, so a new goggle is
/// visible-but-unposed rather than absent. Stubs are tuned: false and excluded from a release.
/// </summary>
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 25 )]
public class CotiHostDiscovery : IOnLoad
{
  private readonly ISptLogger<CotiHostDiscovery> logger;
  private readonly CotiDeviceStore deviceStore;
  private readonly CotiSlotInjector slotInjector;
  private readonly CotiServerConfig config;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long before
  // that - so the table is resolved on use, inside LoadAsync, never in the constructor.
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;

  public CotiHostDiscovery(
      ISptLogger<CotiHostDiscovery> logger, DatabaseServer databaseServer, CotiDeviceStore deviceStore,
      CotiSlotInjector slotInjector, CotiServerConfig config )
  {
    this.logger = logger;
    this.databaseServer = databaseServer;
    this.deviceStore = deviceStore;
    this.slotInjector = slotInjector;
    this.config = config;
  }
#else
  private readonly CotiTemplateTable templateTable;

  public CotiHostDiscovery(
      ISptLogger<CotiHostDiscovery> logger, CotiTemplateTable templateTable, CotiDeviceStore deviceStore,
      CotiSlotInjector slotInjector, CotiServerConfig config )
  {
    this.logger = logger;
    this.templateTable = templateTable;
    this.deviceStore = deviceStore;
    this.slotInjector = slotInjector;
    this.config = config;
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
    if( !config.HostEditor.AutoDiscover )
    {
      logger.Debug( "[COTI] Auto-discovery disabled (hostEditor.autoDiscover = false) - skipped." );
      return Task.CompletedTask;
    }

    var items = new CotiTemplateItemView( templateTable );
    // Stubs are written without reloading, so this stays the pre-discovery table. Exact: each id is
    // visited once, and stubs are untuned so they never seed another stub's mask.
    var snapshot = deviceStore.Current;
    var classified = new Dictionary<string, bool>();
    var namesInUse = DeviceNamesInUse( snapshot );
    var discovered = 0;

    try
    {
      foreach( var ( key, hostItem ) in templateTable.Items )
      {
        var id = (string) key;

        if( snapshot.ByHostId.ContainsKey( id ) || !CotiNvgClassifier.IsNightVision( items, id, classified ) )
          continue;

        var family = hostItem.Properties?.Mask;
        var deviceName = UniqueName( SlugFromName( hostItem.Name, id ), namesInUse );
        var seed = CotiMaskFamilies.SeedFor( family, snapshot.Devices, FamilyOf );

        var stub = NewStub( id, deviceName, hostItem, seed.Mask );

        if( !deviceStore.TryWriteFile( stub, out var writeError ) )
        {
          logger.Warning( $"[COTI] Auto-discovery could not write a stub for {id}: {writeError}" );
          continue;
        }

        namesInUse.Add( deviceName );
        discovered++;

        // A stub is v1: discovery never writes tubes.
        slotInjector.InjectInto( id, stub.DisplayName ?? deviceName, layout: null );

        logger.Debug(
            $"[COTI] Discovered {deviceName} ({id}), family {family ?? "(none declared)"} - " +
            ( seed.SeededFrom != null
                ? $"seeded from tuned device \"{seed.SeededFrom.Device}\""
                : "no tuned device in this family - using the fallback circle" ) );
      }
    }
    finally
    {
      // Even if a later host throws, the stubs already on disk must reach the store.
      if( discovered > 0 )
        deviceStore.Reload();
    }

    if( discovered > 0 )
      logger.Success( $"[COTI] Auto-discovery: {discovered} new night vision host(s) stubbed." );
    else
      logger.Debug( "[COTI] Auto-discovery: no new night vision hosts found." );

    return Task.CompletedTask;
  }

  /// <summary>
  /// An untuned v1 device for one host. The anchor bone is left empty: CurveRotator lives on the
  /// instantiated prefab, which the server never loads, so the client discovers it when it first
  /// mounts on this host and offers it in the pose editor, and Publish commits it.
  /// </summary>
  private static CotiDeviceFile NewStub( string id, string deviceName, TemplateItem hostItem, CotiMaskBlock mask )
  {
    return new CotiDeviceFile
    {
      Schema = CotiDeviceFile.CurrentSchema,
      Device = deviceName,
      DisplayName = hostItem.Name ?? deviceName,
      Tuned = false,
      Hosts = new List<CotiHostRef>
      {
        new CotiHostRef { Id = id, Prefab = hostItem.Properties?.Prefab?.Path },
      },
      Mask = mask,
      Mount = new CotiMountBlock { AnchorBone = string.Empty },
    };
  }

  /// <summary>
  /// SeedFor's family resolution. EFT declares the family per item, so it is not on
  /// CotiDeviceFile; this looks up the device's first host id in the
  /// live item table and reads that item's Mask property. Null when the host is not installed,
  /// so SeedFor falls back rather than throwing.
  /// </summary>
  private string? FamilyOf( CotiDeviceFile device )
  {
    var hostId = device.Hosts?.FirstOrDefault( h => !string.IsNullOrEmpty( h?.Id ) )?.Id;

    if( hostId == null || !MongoId.IsValidMongoId( hostId ) )
      return null;

    return templateTable.Items.TryGetValue( new MongoId( hostId ), out var hostItem )
        ? hostItem.Properties?.Mask
        : null;
  }

  /// <summary>
  /// The item's own _name is already a filesystem-safe slug for every real NVG in the database
  /// (nvg_alfa_pnv-10t, nvg_57em, nvg_l3_gpnvg-18_anvis, ...). This guards against a modded
  /// item's name that is not, so a discovery never fails TryWriteFile's filename check.
  /// </summary>
  private static string SlugFromName( string? name, string fallbackId )
  {
    var basis = string.IsNullOrWhiteSpace( name ) ? fallbackId : name;
    var invalid = Path.GetInvalidFileNameChars();

    var chars = basis.Trim().ToLowerInvariant()
        .Select( c => char.IsWhiteSpace( c ) || invalid.Contains( c ) ? '_' : c )
        .ToArray();

    var slug = new string( chars ).Trim( '_' );
    return string.IsNullOrEmpty( slug ) ? $"nvg_{fallbackId}" : slug;
  }

  /// <summary>
  /// Every name a new stub must not take: resolved devices, and every device file TryWriteFile could
  /// land on, at any depth. Without the files, a name matching an unresolved addon file in a
  /// subfolder would overwrite it.
  /// </summary>
  private HashSet<string> DeviceNamesInUse( CotiDeviceSnapshot snapshot )
  {
    var names = new HashSet<string>( deviceStore.DeviceFileNames(), StringComparer.OrdinalIgnoreCase );

    foreach( var device in snapshot.Devices )
    {
      if( !string.IsNullOrEmpty( device.Device ) )
        names.Add( device.Device );
    }

    return names;
  }

  private static string UniqueName( string baseName, HashSet<string> inUse )
  {
    var candidate = baseName;

    for( var suffix = 2; inUse.Contains( candidate ); suffix++ )
      candidate = $"{baseName}_{suffix}";

    return candidate;
  }
}
