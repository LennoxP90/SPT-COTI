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

  public CotiServerConfig( ISptLogger<CotiServerConfig> logger, ModHelper modHelper )
  {
    try
    {
      // Explicit assembly: GetJsonDataFromModFile resolves it via GetCallingAssembly, which the JIT
      // can change under you.
      var modFolder = modHelper.GetAbsolutePathToModFolder( typeof( CotiServerConfig ).Assembly );
      var file = modHelper.GetJsonDataFromFile<CotiConfigFile>( Path.Combine( modFolder, "config" ), "config.json" );

      Trader = file.Trader;
      Loot = file.Loot;
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
