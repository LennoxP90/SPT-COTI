using Coti.Shared;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Coti.Server;

// 4.0 requires deriving an abstract record; 4.1 requires implementing an interface. The two types
// do not coexist in either version, so this cannot be unified - see the backport design doc.
// Version and SptVersion genuinely differ per SPT generation and stay split below; everything
// else is identity that must not drift between the two branches, so it is hoisted here once.
internal static class ModMetadataFields
{
  public const string ModGuid = "com.lennoxp90.coti.server";
  public const string Name = "ECOTI";
  public const string Author = "LennoxP90";
  public const string Url = "https://github.com/LennoxP90/SPT-COTI";
  public const string License = "MIT";
}

#if SPT40
public record ModMetadata : AbstractModMetadata
{
  public override string ModGuid { get; init; } = ModMetadataFields.ModGuid;
  public override string Name { get; init; } = ModMetadataFields.Name;
  public override string Author { get; init; } = ModMetadataFields.Author;
  public override List<string>? Contributors { get; init; }
  public override SemanticVersioning.Version Version { get; init; } = new( CotiVersion.Current );
  public override SemanticVersioning.Range SptVersion { get; init; } = new( "~4.0.0" );
  public override List<string>? Incompatibilities { get; init; }
  public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
  public override string? Url { get; init; } = ModMetadataFields.Url;
  public override bool? IsBundleMod { get; init; } = true;
  public override string License { get; init; } = ModMetadataFields.License;
}
#else
public record ModMetadata : IModMetadata
{
  public string ModGuid { get; init; } = ModMetadataFields.ModGuid;
  public string Name { get; init; } = ModMetadataFields.Name;
  public string Author { get; init; } = ModMetadataFields.Author;
  public List<string>? Contributors { get; init; }
  public SemanticVersioning.Version Version { get; init; } = new( CotiVersion.Current );
  public SemanticVersioning.Range SptVersion { get; init; } = new( "~4.1.0" );
  public bool HasPrepatcher { get; init; } = false;
  public List<string>? Incompatibilities { get; init; }
  public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
  public string? Url { get; init; } = ModMetadataFields.Url;
  public string License { get; init; } = ModMetadataFields.License;
}
#endif
