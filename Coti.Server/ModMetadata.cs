using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Coti.Server;

public record ModMetadata : IModMetadata
{
  public string ModGuid { get; init; } = "com.lennoxp90.coti.server";
  public string Name { get; init; } = "ECOTI";
  public string Author { get; init; } = "LennoxP90";
  public List<string>? Contributors { get; init; }
  public SemanticVersioning.Version Version { get; init; } = new( "1.0.0" );
  public SemanticVersioning.Range SptVersion { get; init; } = new( "~4.1.0" );
  public bool HasPrepatcher { get; init; } = false;
  public List<string>? Incompatibilities { get; init; }
  public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
  public string? Url { get; init; } = "https://github.com/LennoxP90/SPT-COTI";
  public string License { get; init; } = "MIT";
}
