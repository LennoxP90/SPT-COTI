#nullable enable
using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// Describes a publish outcome. Three states, because "ok" stays true when the file was written
  /// but no host could be fitted - so success and "wrote it, fitted nothing" must read differently.
  /// </summary>
  public static class CotiPublishReport
  {
    public static string Describe( bool ok, string? error, IReadOnlyList<string>? unfitHosts )
    {
      if( !ok )
        return $"Publish failed: {( string.IsNullOrEmpty( error ) ? "no reason given" : error )}";

      if( unfitHosts != null && unfitHosts.Count > 0 )
        return $"Published, but {unfitHosts.Count} host(s) could not be fitted: {string.Join( ", ", unfitHosts )}";

      return "Published";
    }
  }
}
