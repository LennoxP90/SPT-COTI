using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;

namespace Coti.Server;

/// <summary>
/// config/config.json. Singleton, not the default Transient, or every consumer re-reads the file.
/// An unreadable file falls back to the defaults below rather than taking the mod down with it.
/// </summary>
[Injectable( InjectionType.Singleton )]
public class CotiServerConfig
{
  private readonly ISptLogger<CotiServerConfig> logger;
  private readonly ModHelper modHelper;

  public CotiTraderSettings Trader { get; } = new();
  public CotiLootSettings Loot { get; } = new();
  public CotiHostEditorSettings HostEditor { get; } = new();
  public CotiFleaSettings Flea { get; } = new();

  /// <summary>
  /// config/config.json, absolute. Empty when the mod folder could not be resolved.
  /// </summary>
  public string FilePath { get; } = string.Empty;

  public CotiServerConfig( ISptLogger<CotiServerConfig> logger, ModHelper modHelper )
  {
    this.logger = logger;
    this.modHelper = modHelper;

    try
    {
      // Explicit assembly: GetJsonDataFromModFile resolves it via GetCallingAssembly, which the JIT
      // can change under you.
      FilePath = Path.Combine(
          modHelper.GetAbsolutePathToModFolder( typeof( CotiServerConfig ).Assembly ), "config", "config.json" );
    }
    catch( Exception ex )
    {
      logger.Error( "[COTI] Could not locate the mod folder - using built-in defaults.", ex );
      return;
    }

    Apply( ReadFromDisk() );
  }

  /// <summary>
  /// config.json as it is on disk, with every section present. Never throws: an unreadable or
  /// malformed file yields the live settings.
  /// </summary>
  public CotiConfigFile ReadFromDisk()
  {
    if( string.IsNullOrEmpty( FilePath ) )
      return ToFile();

    try
    {
      // ModHelper, not System.Text.Json directly: JsonUtil skips comments, which a config.json
      // may carry.
      var file = modHelper.GetJsonDataFromFile<CotiConfigFile>(
          Path.GetDirectoryName( FilePath )!, Path.GetFileName( FilePath ) );

      // An absent key leaves the CotiConfigFile initialiser intact; an explicit "trader": null
      // assigns null over it.
      return new CotiConfigFile
      {
        Trader = file?.Trader ?? new CotiTraderSettings(),
        Loot = file?.Loot ?? new CotiLootSettings(),
        HostEditor = file?.HostEditor ?? new CotiHostEditorSettings(),
        Flea = file?.Flea ?? new CotiFleaSettings(),
      };
    }
    catch( Exception ex )
    {
      logger.Error( "[COTI] Could not read config/config.json - keeping the current settings.", ex );
      return ToFile();
    }
  }

  /// <summary>
  /// Copies edited settings onto the live ones. Values, not references, so the caller keeps its
  /// own CotiConfigFile. A null section is left as it is, matching how an absent key is treated.
  /// </summary>
  public void Apply( CotiConfigFile file )
  {
    if( file is null )
      return;

    if( file.Trader is not null )
    {
      Trader.LoyaltyLevel = file.Trader.LoyaltyLevel;
      Trader.PriceUsd = file.Trader.PriceUsd;
      Trader.BuyLimit = file.Trader.BuyLimit;
    }

    if( file.Loot is not null )
    {
      Loot.Enabled = file.Loot.Enabled;
      Loot.WeightFraction = file.Loot.WeightFraction;
    }

    if( file.HostEditor is not null )
      HostEditor.AutoDiscover = file.HostEditor.AutoDiscover;

    if( file.Flea is not null )
      Flea.PlayerSellable = file.Flea.PlayerSellable;
  }

  /// <summary>
  /// Writes config.json. Does nothing when the mod folder never resolved.
  /// </summary>
  public async Task<bool> SaveToDiskAsync( CotiConfigFile file, CancellationToken cancellationToken = default )
  {
    if( string.IsNullOrEmpty( FilePath ) )
      return false;

    await File.WriteAllTextAsync(
        FilePath, JsonSerializer.Serialize( file, WriteOptions ), cancellationToken );

    return true;
  }

  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

  /// <summary>
  /// The current settings in the shape config.json is written in.
  /// </summary>
  public CotiConfigFile ToFile()
  {
    return new CotiConfigFile
    {
      Trader = Trader,
      Loot = Loot,
      HostEditor = HostEditor,
      Flea = Flea
    };
  }
}

/// <summary>
/// Explicit names throughout: JsonUtil matches case-sensitively with no naming policy, so a
/// mismatched name binds a default and reports nothing.
/// </summary>
public class CotiConfigFile
{
  [JsonPropertyName( "trader" )]
  public CotiTraderSettings Trader { get; set; } = new();

  [JsonPropertyName( "loot" )]
  public CotiLootSettings Loot { get; set; } = new();

  [JsonPropertyName( "hostEditor" )]
  public CotiHostEditorSettings HostEditor { get; set; } = new();

  [JsonPropertyName( "flea" )]
  public CotiFleaSettings Flea { get; set; } = new();
}

public class CotiTraderSettings
{
  [JsonPropertyName( "loyaltyLevel" )]
  public int LoyaltyLevel { get; set; } = 4;

  [JsonPropertyName( "priceUsd" )]
  public int PriceUsd { get; set; } = 2000;

  /// <summary>
  /// Purchases per profile before the trader stops offering it.
  /// </summary>
  [JsonPropertyName( "buyLimit" )]
  public int BuyLimit { get; set; } = 3;
}

public class CotiLootSettings
{
  [JsonPropertyName( "enabled" )]
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Spawn weight relative to the night vision at each spot. 0 disables it.
  /// </summary>
  [JsonPropertyName( "weightFraction" )]
  public double WeightFraction { get; set; } = 0.25;
}

public class CotiHostEditorSettings
{
  /// <summary>
  /// When true (the default), CotiHostDiscovery stubs a Tuned:false device for every night
  /// vision host the item table declares that no device file already covers. False restores the
  /// pre-2.0.0 behaviour of supporting exactly the shipped set.
  /// </summary>
  [JsonPropertyName( "autoDiscover" )]
  public bool AutoDiscover { get; set; } = true;
}

public class CotiFleaSettings
{
  /// <summary>
  /// Sets CanSellOnRagfair. False keeps the flea to Peacekeeper's offer alone; true also lets
  /// SPT generate simulated player offers.
  /// </summary>
  [JsonPropertyName( "playerSellable" )]
  public bool PlayerSellable { get; set; } = false;
}
