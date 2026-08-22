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
  public CotiTraderSettings Trader { get; private set; } = new();
  public CotiLootSettings Loot { get; private set; } = new();
  public CotiHostEditorSettings HostEditor { get; private set; } = new();

  public CotiServerConfig( ISptLogger<CotiServerConfig> logger, ModHelper modHelper )
  {
    try
    {
      // Explicit assembly: GetJsonDataFromModFile resolves it via GetCallingAssembly, which the JIT
      // can change under you.
      var modFolder = modHelper.GetAbsolutePathToModFolder( typeof( CotiServerConfig ).Assembly );
      var file = modHelper.GetJsonDataFromFile<CotiConfigFile>( Path.Combine( modFolder, "config" ), "config.json" );

      // ?? on each: an ABSENT key leaves the CotiConfigFile initialiser intact, but an explicit
      // "trader": null in a hand-edited file makes System.Text.Json assign null straight over it,
      // and every reader here dereferences without checking - hostEditor.autoDiscover would then
      // take the whole mod down at load. Same trap the device files were already hardened against,
      // and a player trying to switch a section off by nulling it is the likely way in.
      Trader = file.Trader ?? Trader;
      Loot = file.Loot ?? Loot;
      HostEditor = file.HostEditor ?? HostEditor;
    }
    catch( Exception ex )
    {
      logger.Error( "[COTI] Could not read config/config.json - using built-in defaults.", ex );
    }
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
