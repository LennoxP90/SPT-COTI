using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Coti.Client
{
  /// <summary>
  /// lamps.json, beside the plugin: the lamp types the thermal image shows hot, as {"heated": ["Searchlight_03_source_on",
  /// ...]}, each named by its lamp type (CotiLampHeat.TypeOf), // comments allowed. Matched
  /// without regard to case. Every lamp not listed stays cold, lit or not.
  /// </summary>
  internal static class CotiLampFile
  {
    internal const string FileName = "lamps.json";

    /// <summary>The listed types, or null with the reason when the text is not a lamps.json.</summary>
    internal static HashSet<string> Parse( string json, out string error )
    {
      error = null;
      try
      {
        if( !( JObject.Parse( json )["heated"] is JArray list ) )
        {
          error = "no \"heated\" list";
          return null;
        }
        var types = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach( var item in list )
          if( item.Type == JTokenType.String && !string.IsNullOrWhiteSpace( (string)item ) )
            types.Add( ( (string)item ).Trim() );
        return types;
      }
      catch( Exception ex )
      {
        error = ex.Message;
        return null;
      }
    }
  }
}
