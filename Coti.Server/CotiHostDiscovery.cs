using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
// SPTarkov.Server.Core.Models.Eft.Common.Tables also declares a type named "Path" (a lockpicking
// path record), which collides with System.IO.Path once both usings are in scope - the same trap
// CotiDeviceStore.cs documents at its own top.
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
    var discovered = 0;

    foreach( var id in items.AllIds().ToList() )
    {
      // Re-read per candidate, not hoisted out of the loop: TryWrite below reloads the store, so
      // each iteration has to see the stub the previous one just wrote. One read gives one
      // complete snapshot, which is all this needs.
      var snapshot = deviceStore.Current;

      if( snapshot.ByHostId.ContainsKey( id ) )
        continue;

      if( !CotiNvgClassifier.IsNightVision( items, id ) )
        continue;

      if( !templateTable.Items.TryGetValue( new MongoId( id ), out var hostItem ) )
        continue;

      var family = hostItem.Properties?.Mask;
      var deviceName = ResolveUniqueDeviceName( SlugFromName( hostItem.Name, id ) );

      var seed = CotiMaskFamilies.SeedFor( family, snapshot.Devices, FamilyOf );

      var stub = new CotiDeviceFile
      {
        Schema = CotiDeviceFile.CurrentSchema,
        Device = deviceName,
        DisplayName = hostItem.Name ?? deviceName,
        Tuned = false,
        Hosts = new List<CotiHostRef>
        {
          new CotiHostRef { Id = id, Prefab = hostItem.Properties?.Prefab?.Path },
        },
        Mask = seed.Mask,
        // Empty deliberately, not an oversight: CurveRotator lives on the instantiated prefab,
        // which the server never loads and never sees, so the anchor bone cannot be seeded here.
        // The client discovers the real anchor the first time it mounts on this host and offers
        // it as a suggestion in the pose editor; Publish is what commits it. Do not try to
        // "finish" this by guessing a bone name.
        Mount = new CotiMountBlock { AnchorBone = string.Empty },
      };

      if( !deviceStore.TryWrite( stub, out var writeError ) )
      {
        logger.Warning( $"[COTI] Auto-discovery could not write a stub for {id}: {writeError}" );
        continue;
      }

      slotInjector.InjectInto( id, stub.DisplayName ?? deviceName );

      var familyLabel = family ?? "(none declared)";
      var seedDescription = seed.SeededFrom != null
          ? $"seeded from tuned device \"{seed.SeededFrom.Device}\""
          : "no tuned device in this family - using the fallback circle";

      logger.Success(
          $"[COTI] Discovered {deviceName} ({id}), family {familyLabel} - {seedDescription}" );

      discovered++;
    }

    if( discovered > 0 )
      logger.Success( $"[COTI] Auto-discovery: {discovered} new night vision host(s) stubbed." );
    else
      logger.Debug( "[COTI] Auto-discovery: no new night vision hosts found." );

    return Task.CompletedTask;
  }

  /// <summary>
  /// SeedFor's family resolution: a device's family is not carried on CotiDeviceFile itself - EFT
  /// declares it per ITEM, not per device - so this looks up the device's first host id in the
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
  /// (nvg_alfa_pnv-10t, nvg_57em, nvg_l3_gpnvg-18_anvis, ...) - this only guards the case a
  /// modded item's name is not, so a discovery never fails TryWrite's own filename check.
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
  /// Checked against both the resolved store and the raw folder, not just deviceStore.Devices -
  /// a device file whose hosts all failed to resolve (an uninstalled mod's host, say) never makes
  /// it into Devices, but its file is still sitting in FolderPath under that name, and writing
  /// over it would silently destroy someone else's device file rather than merely being rejected
  /// by CotiDeviceMerge's own "already defined" check.
  /// </summary>
  private bool DeviceNameInUse( string name )
  {
    if( deviceStore.Current.Devices.Any( d => string.Equals( d.Device, name, StringComparison.OrdinalIgnoreCase ) ) )
      return true;

    return File.Exists( Path.Combine( deviceStore.FolderPath, $"{name}.json" ) );
  }

  private string ResolveUniqueDeviceName( string baseName )
  {
    if( !DeviceNameInUse( baseName ) )
      return baseName;

    var suffix = 2;
    string candidate;

    do
    {
      candidate = $"{baseName}_{suffix}";
      suffix++;
    }
    while( DeviceNameInUse( candidate ) );

    return candidate;
  }
}
