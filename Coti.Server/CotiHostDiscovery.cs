using Coti.Shared;
using System.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
// SPTarkov.Server.Core.Models.Eft.Common.Tables also declares a type named "Path" (a lockpicking
// path record), which collides with System.IO.Path once both usings are in scope.
using Path = System.IO.Path;

namespace Coti.Server;

/// <summary>
/// A goggle with no device file gets no COTI slot and one log line saying so, unless a saved profile still holds
/// something in one of its COTI slots: those slots are kept, in memory only, so the profile still loads. A starting
/// device file for a new goggle is written only on request, from the mount editor's "Create device file".
/// </summary>
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 25 )]
public class CotiHostDiscovery : IOnLoad
{
  private readonly ISptLogger<CotiHostDiscovery> logger;
  private readonly CotiDeviceStore deviceStore;
  private readonly CotiSlotInjector slotInjector;
  private readonly SaveServer saveServer;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long before
  // that - so the table is resolved on use, inside LoadAsync, never in the constructor.
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;

  public CotiHostDiscovery(
      ISptLogger<CotiHostDiscovery> logger, DatabaseServer databaseServer, CotiDeviceStore deviceStore,
      CotiSlotInjector slotInjector, SaveServer saveServer )
  {
    this.logger = logger;
    this.databaseServer = databaseServer;
    this.deviceStore = deviceStore;
    this.slotInjector = slotInjector;
    this.saveServer = saveServer;
  }
#else
  private readonly CotiTemplateTable templateTable;

  public CotiHostDiscovery(
      ISptLogger<CotiHostDiscovery> logger, CotiTemplateTable templateTable, CotiDeviceStore deviceStore,
      CotiSlotInjector slotInjector, SaveServer saveServer )
  {
    this.logger = logger;
    this.templateTable = templateTable;
    this.deviceStore = deviceStore;
    this.slotInjector = slotInjector;
    this.saveServer = saveServer;
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
    ReportUndeclared();
    return Task.CompletedTask;
  }

  /// <summary>Night vision items with no device file, by name, for the mount editor's "Create device file".</summary>
  public IReadOnlyList<( string Id, string Name )> Undeclared()
  {
    var items = new CotiTemplateItemView( templateTable );
    var snapshot = deviceStore.Current;
    var classified = new Dictionary<string, bool>();
    var found = new List<( string Id, string Name )>();

    foreach( var ( key, hostItem ) in templateTable.Items )
    {
      var id = (string) key;
      if( !snapshot.ByHostId.ContainsKey( id ) && CotiNvgClassifier.IsNightVision( items, id, classified ) )
        found.Add( ( id, hostItem.Name ?? id ) );
    }

    found.Sort( ( a, b ) => string.CompareOrdinal( a.Name, b.Name ) );
    return found;
  }

  /// <summary>
  /// Writes the starting device file for one night vision item that has none, gives the item
  /// its COTI slot in the live database and reloads the store. A game already running sees the slot after a relaunch.
  /// </summary>
  public bool TryCreateDevice( string id, out string deviceName, out string error )
  {
    deviceName = string.Empty;
    var snapshot = deviceStore.Current;

    if( !MongoId.IsValidMongoId( id ) || !templateTable.Items.TryGetValue( new MongoId( id ), out var hostItem ) )
    {
      error = $"{id} is not an item in this database";
      return false;
    }

    if( snapshot.ByHostId.ContainsKey( id ) )
    {
      error = $"{hostItem.Name ?? id} already has a device file";
      return false;
    }

    if( !CotiNvgClassifier.IsNightVision( new CotiTemplateItemView( templateTable ), id, new Dictionary<string, bool>() ) )
    {
      error = $"{hostItem.Name ?? id} is not a night vision item";
      return false;
    }

    if( !TryStub( id, hostItem, snapshot, DeviceNamesInUse( snapshot ), out deviceName, out error ) )
      return false;

    deviceStore.Reload();
    logger.Info( $"[COTI] Device file {deviceName} created for {hostItem.Name ?? id} ({id}) from the mount editor" );
    return true;
  }

  /// <summary>
  /// Writes an untuned stub for one host, seeded from a tuned device of the same mask family, and gives the host its
  /// COTI slot.
  /// </summary>
  private bool TryStub(
      string id, TemplateItem hostItem, CotiDeviceSnapshot snapshot, HashSet<string> namesInUse,
      out string deviceName, out string error )
  {
    var family = hostItem.Properties?.Mask;
    deviceName = UniqueName( SlugFromName( hostItem.Name, id ), namesInUse );
    var seed = CotiMaskFamilies.SeedFor( family, snapshot.Devices, FamilyOf );
    var stub = NewStub( id, deviceName, hostItem, seed.Mask );

    if( !deviceStore.TryWriteFile( stub, out error ) )
      return false;

    namesInUse.Add( deviceName );

    // A stub is v1: it never carries tubes.
    slotInjector.InjectInto( id, stub.DisplayName ?? deviceName, layout: null );
    return true;
  }

  /// <summary>
  /// Every night vision item with no device file: one line each, and its COTI slots kept only where a saved profile
  /// still holds something in them.
  /// </summary>
  private void ReportUndeclared()
  {
    var items = new CotiTemplateItemView( templateTable );
    var snapshot = deviceStore.Current;
    var classified = new Dictionary<string, bool>();
    HashSet<string>? occupied = null;

    foreach( var ( key, hostItem ) in templateTable.Items )
    {
      var id = (string) key;

      if( snapshot.ByHostId.ContainsKey( id ) || !CotiNvgClassifier.IsNightVision( items, id, classified ) )
        continue;

      occupied ??= HostsWithOccupiedCotiSlots();
      var name = hostItem.Name ?? id;

      if( occupied.Contains( id ) )
      {
        slotInjector.InjectInto( id, name, layout: null );
        logger.Warning(
            $"[COTI] {name} ({id}) has no device file, but a saved profile has a COTI fitted to one. Its COTI slot is " +
            "kept so that profile loads, but the COTI draws nothing on it. Install the host's addon, or take the COTI off." );
      }
      else
      {
        logger.Info( $"[COTI] {name} ({id}) has no device file, so it takes no COTI. See ADDONS.md to add one." );
      }
    }
  }

  /// <summary>
  /// Templates holding something in a COTI slot in any saved item list: both characters' inventories, mail,
  /// insurance returns and equipment builds. Profiles are loaded at SaveCallbacks, well before PostLoad.
  /// </summary>
  private HashSet<string> HostsWithOccupiedCotiSlots()
  {
    var hosts = new HashSet<string>();

    foreach( var profile in saveServer.GetProfiles().Values )
    {
      Collect( profile.CharacterData?.PmcData?.Inventory?.Items, hosts );
      Collect( profile.CharacterData?.ScavData?.Inventory?.Items, hosts );

      foreach( var insurance in profile.InsuranceList ?? Enumerable.Empty<Insurance>() )
        Collect( insurance.Items, hosts );

      foreach( var dialogue in profile.DialogueRecords?.Values ?? Enumerable.Empty<Dialogue>() )
      {
        foreach( var message in dialogue.Messages ?? Enumerable.Empty<Message>() )
          Collect( message.Items?.Data, hosts );
      }

      foreach( var build in profile.UserBuildData?.EquipmentBuilds ?? Enumerable.Empty<EquipmentBuild>() )
        Collect( build.Items, hosts );
    }

    return hosts;
  }

  private static void Collect( List<Item>? items, HashSet<string> hosts )
  {
    if( items == null || items.Count == 0 )
      return;

    var saved = items.ConvertAll( i => new CotiSavedItem( i.Id.ToString(), i.Template.ToString(), i.ParentId, i.SlotId ) );
    CotiSlotUse.CollectHostsInUse( saved, hosts );
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
  /// item's name that is not, so a new device file never fails TryWriteFile's filename check.
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
