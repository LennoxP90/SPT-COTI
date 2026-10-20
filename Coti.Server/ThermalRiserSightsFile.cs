// config/riser-sights.json. Must stay dependency-free - no SPTarkov type of any kind - because
// Coti.Tests source-links this file with no SPT reference.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coti.Server;

/// <summary>
/// The sights the riser rule accepts that must not go on the riser, each with the reason. Read once at
/// PostLoad + 25 by ThermalRiserCompatibility, written by the ECOTI Riser sights page.
/// </summary>
public class ThermalRiserSightsFile
{
  public const string FileName = "riser-sights.json";

  /// <summary>The scope rings' deny list, same format.</summary>
  public const string ScopeRingsFileName = "scope-rings.json";

  [JsonPropertyName( "deny" )]
  public List<DeniedSight> Deny { get; set; } = new();

  // Case-insensitive: System.Text.Json matches names exactly by default, so a hand-typed "Deny" would read as an empty
  // list and deny nothing without a word.
  private static readonly JsonSerializerOptions ReadOptions = new()
  {
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
  };

  // Relaxed escaping so a note's apostrophes and quotes stay readable in the file.
  private static readonly JsonSerializerOptions WriteOptions = new()
  {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  /// <summary>
  /// The file, or null with the reason when the text is not a riser-sights.json. An entry whose id is not 24 hex
  /// digits is dropped and named in <paramref name="error"/> while the rest still loads, so one typo does not undo
  /// every other denial. Ids come back lower case.
  /// </summary>
  public static ThermalRiserSightsFile? Parse( string json, out string error )
  {
    error = string.Empty;
    ThermalRiserSightsFile? file;
    try
    {
      file = JsonSerializer.Deserialize<ThermalRiserSightsFile>( json, ReadOptions );
    }
    catch( JsonException ex )
    {
      error = ex.Message;
      return null;
    }

    // "deny": null assigns null over the initialiser.
    if( file?.Deny is null )
    {
      error = "no \"deny\" list";
      return null;
    }

    var bad = file.Deny.Where( d => !IsTemplateId( d?.Id ) ).Select( d => string.IsNullOrEmpty( d?.Id ) ? "(no id)" : d.Id ).ToList();
    file.Deny = file.Deny.Where( d => IsTemplateId( d?.Id ) ).ToList();
    foreach( var entry in file.Deny )
      entry.Id = entry.Id.ToLowerInvariant();

    if( bad.Count > 0 )
      error = $"skipped {bad.Count} entries whose id is not 24 hex digits: {string.Join( ", ", bad )}";
    return file;
  }

  public string Serialize() => JsonSerializer.Serialize( this, WriteOptions );

  public static bool IsTemplateId( string? id ) => id is { Length: 24 } && id.All( Uri.IsHexDigit );
}

public class DeniedSight
{
  [JsonPropertyName( "id" )]
  public string Id { get; set; } = string.Empty;

  /// <summary>Why it is denied. The file's only comments: a save from the page drops // comments.</summary>
  [JsonPropertyName( "note" )]
  public string? Note { get; set; }
}
