using Coti.Shared;
using System.Linq;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
// SPTarkov.Server.Core.Models.Eft.Common.Tables also declares a type named "Path" (a lockpicking
// path record), which collides with System.IO.Path once both usings are in scope.
using Path = System.IO.Path;

namespace Coti.Server;

/// <summary>
/// One immutable view of the folder, published in a single write, so a reader cannot see half of
/// one reload and half of the next.
/// </summary>
public sealed class CotiDeviceSnapshot
{
  public static readonly CotiDeviceSnapshot Empty = new CotiDeviceSnapshot(
      new Dictionary<string, CotiResolvedHost>(), new List<CotiDeviceFile>(),
      new List<CotiDeviceFile>(), 0, 0 );

  public CotiDeviceSnapshot(
      IReadOnlyDictionary<string, CotiResolvedHost> byHostId,
      IReadOnlyList<CotiDeviceFile> devices,
      IReadOnlyList<CotiDeviceFile> resolvedDevices,
      int declaredHostCount,
      int unresolvedHostCount )
  {
    ByHostId = byHostId;
    Devices = devices;
    ResolvedDevices = resolvedDevices;
    DeclaredHostCount = declaredHostCount;
    UnresolvedHostCount = unresolvedHostCount;
  }

  /// <summary>Every host that resolved, keyed by the id the item table really has.</summary>
  public IReadOnlyDictionary<string, CotiResolvedHost> ByHostId { get; }

  /// <summary>Devices with their hosts exactly as authored - see CotiResolveResult.Devices.</summary>
  public IReadOnlyList<CotiDeviceFile> Devices { get; }

  /// <summary>
  /// The same devices with resolved ids substituted - the shape GET /coti/hosts must serve. See
  /// CotiResolveResult.ResolvedDevices for why the wire and the file disagree on purpose.
  /// </summary>
  public IReadOnlyList<CotiDeviceFile> ResolvedDevices { get; }

  /// <summary>
  /// Host entries declared by devices whose requires gate passed. Counted here rather than in the
  /// resolver so the fitted and not-installed numbers cannot disagree.
  /// </summary>
  public int DeclaredHostCount { get; }

  /// <summary>
  /// DeclaredHostCount minus ByHostId.Count - the declared host entries that did not resolve
  /// into an installed item, for whatever reason (not installed, an ambiguous prefab match, or an
  /// id another device had already claimed). ByHostId cannot answer this itself, because
  /// CotiHostResolver only writes entries for hosts that resolved.
  /// </summary>
  public int UnresolvedHostCount { get; }
}

/// <summary>
/// Reads nvghostcompat/, merges and resolves, and holds the result for the injector, the routes
/// and discovery. Reloaded on publish, so a device is usable without a restart.
/// </summary>
[Injectable( InjectionType.Singleton, TypePriority = CotiLoadOrder.PostLoad + 10 )]
public class CotiDeviceStore : IOnLoad
{
  private readonly ISptLogger<CotiDeviceStore> logger;
  private readonly IReadOnlyList<SptMod> loadedMods;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long before
  // that - so the table is resolved on use, inside Reload(), never in the constructor.
  private CotiTemplateTable templateTable => databaseServer.GetTables().Templates;
#else
  private readonly CotiTemplateTable templateTable;
#endif

  public string FolderPath { get; }

  private CotiDeviceSnapshot snapshot = CotiDeviceSnapshot.Empty;

  /// <summary>
  /// Everything the last Reload() resolved, as one reference. Take it into a local and read that;
  /// two reads in one calculation can straddle a concurrent publish's Reload and disagree.
  /// Volatile on both sides so a reader on a weakly-ordered CPU cannot observe the reference
  /// before the object it points at is fully written.
  /// </summary>
  public CotiDeviceSnapshot Current => Volatile.Read( ref snapshot );

  private static readonly JsonSerializerOptions WriteOptions = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

#if SPT40
  public CotiDeviceStore(
      ISptLogger<CotiDeviceStore> logger, ModHelper modHelper, IReadOnlyList<SptMod> loadedMods,
      DatabaseServer databaseServer )
  {
    this.logger = logger;
    this.loadedMods = loadedMods;
    this.databaseServer = databaseServer;
    FolderPath = ResolveFolderPath( modHelper );
  }
#else
  public CotiDeviceStore(
      ISptLogger<CotiDeviceStore> logger, ModHelper modHelper, IReadOnlyList<SptMod> loadedMods,
      CotiTemplateTable templateTable )
  {
    this.logger = logger;
    this.loadedMods = loadedMods;
    this.templateTable = templateTable;
    FolderPath = ResolveFolderPath( modHelper );
  }
#endif

  // Explicit assembly: GetJsonDataFromModFile-style helpers resolve it via GetCallingAssembly,
  // which JIT inlining can change. CotiServerConfig.cs does the same.
  private static string ResolveFolderPath( ModHelper modHelper )
  {
    var modFolder = modHelper.GetAbsolutePathToModFolder( typeof( CotiDeviceStore ).Assembly );
    return Path.Combine( modFolder, "nvghostcompat" );
  }

  // The interface member differs between versions; the work does not.
#if SPT40
  public Task OnLoad() { Reload(); return Task.CompletedTask; }
#else
  public Task OnLoadAsync( CancellationToken cancellationToken ) { Reload(); return Task.CompletedTask; }
#endif

  /// <summary>
  /// Re-reads the folder, merges and resolves. Safe to call repeatedly; TryWrite calls it after
  /// each write.
  /// </summary>
  public void Reload()
  {
    var parsedFiles = ReadParsedFiles();

    // Built before the merge because the merge needs it: a device whose requires is
    // unmet must be dropped before it claims a host, or the stub that could cover that host is
    // warned off as a duplicate of a file that then gets dropped anyway. See Merge's own note.
    var items = new CotiTemplateItemView( templateTable );
    var loadedGuids = new HashSet<string>(
        loadedMods.Select( m => m.ModMetadata.ModGuid ), StringComparer.OrdinalIgnoreCase );

    var merged = CotiDeviceMerge.Merge( parsedFiles, loadedGuids );

    foreach( var warning in merged.Warnings )
      logger.Warning( $"[COTI] {warning}" );

    // Info rather than Debug. A merge note means a device declared a "requires" guid the server
    // has not loaded: either the host mod is not installed, which is fine, or the guid is wrong,
    // a common addon-author mistake that only the log reports. It is one line per device file.
    // The resolve notes stay at Debug because an absent host id is per item and can run to dozens
    // on a healthy install.
    foreach( var note in merged.Notes )
      logger.Info( $"[COTI] {note}" );

    var resolved = CotiHostResolver.Resolve( merged, items, loadedGuids );

    // Warnings are something a human should act on; Notes are the normal case - an absent host
    // from an optional mod happens on every healthy install, and logging it as a warning would
    // make a healthy install look broken.
    foreach( var warning in resolved.Warnings )
      logger.Warning( $"[COTI] {warning}" );

    foreach( var note in resolved.Notes )
      logger.Debug( $"[COTI] {note}" );

    // Applies the same Requires gate Resolve does: a device gated out by a missing mod never had
    // any hosts eligible to resolve, so it must not inflate either number.
    var declaredHostCount = merged.Devices
        .Where( d => string.IsNullOrWhiteSpace( d.Requires )
            || loadedGuids.Contains( d.Requires, StringComparer.OrdinalIgnoreCase ) )
        .Sum( d => d.Hosts?.Count( h => h?.Id != null ) ?? 0 );

    // One assignment, after everything is built, so readers never see a partial reload. See
    // CotiDeviceSnapshot.
    Volatile.Write( ref snapshot, new CotiDeviceSnapshot(
        resolved.ByHostId, resolved.Devices, resolved.ResolvedDevices,
        declaredHostCount, declaredHostCount - resolved.ByHostId.Count ) );

    logger.Success(
        $"[COTI] Device store: {resolved.Devices.Count} device(s) resolved, covering " +
        $"{resolved.ByHostId.Count} host(s), from {FolderPath}" );
  }

  /// <summary>
  /// Writes to a temp file then moves it over the target, and copies any existing file to
  /// "&lt;device&gt;.json.bak" first. A half-written device file fails to parse and is skipped on
  /// the next load, losing the tuned pose, so the write never happens in place.
  /// </summary>
  public bool TryWrite( CotiDeviceFile device, out string error )
  {
    error = string.Empty;

    if( device == null || string.IsNullOrWhiteSpace( device.Device ) )
    {
      error = "device.Device is required to name the file";
      return false;
    }

    if( device.Device.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 )
    {
      error = $"device name \"{device.Device}\" contains characters not valid in a file name";
      return false;
    }

    try
    {
      Directory.CreateDirectory( FolderPath );

      // Existing location first, folder root only for a device that has never been written, so a
      // device in an addon subfolder is not duplicated at the root.
      var targetPath = FindExistingPath( device.Device )
          ?? Path.Combine( FolderPath, $"{device.Device}.json" );
      var backupPath = targetPath + ".bak";
      var tempPath = targetPath + ".tmp";

      if( File.Exists( targetPath ) )
        File.Copy( targetPath, backupPath, overwrite: true );

      var json = JsonSerializer.Serialize( CotiDeviceDto.FromShared( device ), WriteOptions );
      File.WriteAllText( tempPath, json );
      File.Move( tempPath, targetPath, overwrite: true );
    }
    catch( Exception ex )
    {
      error = ex.Message;
      return false;
    }

    Reload();
    return true;
  }

  private List<CotiParsedFile> ReadParsedFiles()
  {
    var files = new List<CotiParsedFile>();

    if( !Directory.Exists( FolderPath ) )
    {
      logger.Warning(
          $"[COTI] Device folder {FolderPath} does not exist - no devices will load" );
      return files;
    }

    // Recursive, so an addon can be a folder rather than loose files - the convention SAIN uses
    // for presets, and the one that lets a player install by extracting an archive over their mod
    // folder and uninstall by deleting one directory.
    foreach( var path in Directory.GetFiles( FolderPath, "*.json", SearchOption.AllDirectories ) )
    {
      if( path.EndsWith( ".bak", StringComparison.OrdinalIgnoreCase ) )
        continue;

      if( IsInWorkingFolder( path ) )
        continue;

      files.Add( ParseFile( path ) );
    }

    return files;
  }

  /// <summary>
  /// Whether a file sits under a folder named "_..." or ".", which by convention means not live.
  /// The read is recursive, so this keeps parked or reorganised device files out of the load.
  /// </summary>
  private bool IsInWorkingFolder( string path )
  {
    var dir = Path.GetDirectoryName( path );
    var root = Path.GetFullPath( FolderPath ).TrimEnd( Path.DirectorySeparatorChar );

    while( !string.IsNullOrEmpty( dir ) )
    {
      var full = Path.GetFullPath( dir ).TrimEnd( Path.DirectorySeparatorChar );
      if( string.Equals( full, root, StringComparison.OrdinalIgnoreCase ) )
        return false;

      var name = Path.GetFileName( full );
      if( name.StartsWith( "_", StringComparison.Ordinal ) || name.StartsWith( ".", StringComparison.Ordinal ) )
        return true;

      dir = Path.GetDirectoryName( full );
    }

    return false;
  }

  /// <summary>
  /// Where a device's file already is, or null. A publish rewrites that file in place so a device
  /// in a subfolder does not gain a second file at the top level claiming the same host.
  /// </summary>
  private string? FindExistingPath( string device )
  {
    if( !Directory.Exists( FolderPath ) )
      return null;

    var wanted = $"{device}.json";

    foreach( var path in Directory.GetFiles( FolderPath, "*.json", SearchOption.AllDirectories ) )
    {
      if( IsInWorkingFolder( path ) )
        continue;

      if( string.Equals( Path.GetFileName( path ), wanted, StringComparison.OrdinalIgnoreCase ) )
        return path;
    }

    return null;
  }

  private static CotiParsedFile ParseFile( string path )
  {
    var parsed = new CotiParsedFile { Path = path };

    try
    {
      var json = File.ReadAllText( path );
      var dto = JsonSerializer.Deserialize<CotiDeviceDto>( json );

      if( dto == null )
      {
        parsed.ParseError = "file is empty or not a JSON object";
        return parsed;
      }

      // Nullable annotations are compile-time only, so System.Text.Json assigns null over the
      // "= new()" initialiser for an explicit "hosts": null / "mask": null / "mount": null.
      // ToShared() would substitute a default and the file would load looking valid, so the
      // offending member is reported here, where the file path is known.
      if( dto.Hosts == null )
      {
        parsed.ParseError = "\"hosts\" is null";
        return parsed;
      }

      if( dto.Mask == null )
      {
        parsed.ParseError = "\"mask\" is null";
        return parsed;
      }

      if( dto.Mount == null )
      {
        parsed.ParseError = "\"mount\" is null";
        return parsed;
      }

      parsed.Device = dto.ToShared();
    }
    catch( Exception ex )
    {
      // A throw here becomes a ParseError on this file alone, never an escape - one malformed
      // file must not take the whole mod down. CotiDeviceMerge turns this into a warning naming
      // the file and this message.
      parsed.ParseError = ex.Message;
    }

    return parsed;
  }

}
