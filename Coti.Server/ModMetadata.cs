using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Coti.Server;

public record ModMetadata : AbstractModMetadata
{
  public override string ModGuid { get; init; } = "com.lennoxp90.coti.server";
  public override string Name { get; init; } = "ECOTI";
  public override string Author { get; init; } = "LennoxP90";
  public override List<string>? Contributors { get; init; }
  public override SemanticVersioning.Version Version { get; init; } = new( "1.0.0" );
  public override SemanticVersioning.Range SptVersion { get; init; } = new( "~4.0.0" );
  public override List<string>? Incompatibilities { get; init; }
  public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
  public override string? Url { get; init; } = "https://github.com/LennoxP90/SPT-COTI";
  public override bool? IsBundleMod { get; init; } = true;
  public override string License { get; init; } = "MIT";
}
