using System.Collections.Generic;
using System.Linq;

namespace Coti.Shared
{
  public class CotiParsedFile
  {
    /// <summary>
    /// Path this was read from. Always set by the caller that scanned the directory, and used in
    /// every warning message.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    public CotiDeviceFile? Device { get; set; }
    public string? ParseError { get; set; }
  }

  public class CotiMergeResult
  {
    public List<CotiDeviceFile> Devices { get; } = new List<CotiDeviceFile>();

    /// <summary>Something a human should act on.</summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>
    /// Expected outcomes worth recording but not warning about, the same split CotiResolveResult
    /// draws. A device whose optional host mod is not installed belongs here: it happens on every
    /// healthy install, and a warning would make one look broken.
    /// </summary>
    public List<string> Notes { get; } = new List<string>();
  }

  /// <summary>
  /// Folds parsed device files into one table. Pure, so the precedence rules are testable -
  /// the server half only does IO and calls this.
  ///
  /// Ordering is by path, not by enumeration order: two filesystems that list a directory
  /// differently must produce the same table, or a duplicate resolves one way on one machine
  /// and the other way on the next.
  /// </summary>
  public static class CotiDeviceMerge
  {
    /// <param name="loadedModGuids">
    /// Loaded mod guids, or null to skip the check. A device whose <c>requires</c> is unmet is
    /// dropped here, before any host is claimed, so it cannot win a host and then be discarded,
    /// leaving the host covered by nothing.
    /// </param>
    public static CotiMergeResult Merge( IEnumerable<CotiParsedFile> files,
        IEnumerable<string>? loadedModGuids = null )
    {
      var result = new CotiMergeResult();
      var byDevice = new Dictionary<string, string>();
      var byHostId = new Dictionary<string, string>();

      var loaded = loadedModGuids == null
          ? null
          : new HashSet<string>( loadedModGuids, System.StringComparer.OrdinalIgnoreCase );

      // Tuned first, then path. Auto-discovery writes stubs beside addon files, so path alone would
      // let an untuned stub keep a host an addon should take over.
      var ordered = files
          .OrderByDescending( f => f.Device != null && f.Device.Tuned )
          .ThenBy( f => f.Path, System.StringComparer.OrdinalIgnoreCase );

      foreach( var file in ordered )
      {
        if( file.Device == null )
        {
          result.Warnings.Add( $"{file.Path}: could not be read ({file.ParseError}) - skipped" );
          continue;
        }

        var d = file.Device;

        if( d.Schema != CotiDeviceFile.CurrentSchema )
        {
          result.Warnings.Add(
              $"{file.Path}: schema {d.Schema} is not {CotiDeviceFile.CurrentSchema} - skipped rather " +
              "than bound to defaults" );
          continue;
        }

        if( string.IsNullOrWhiteSpace( d.Device ) || string.IsNullOrWhiteSpace( d.DisplayName ) )
        {
          result.Warnings.Add( $"{file.Path}: device or displayName is blank - skipped" );
          continue;
        }

        if( loaded != null && !string.IsNullOrWhiteSpace( d.Requires ) && !loaded.Contains( d.Requires ) )
        {
          result.Notes.Add(
              $"[{d.Device}] requires mod \"{d.Requires}\", which is not loaded - skipped" );
          continue;
        }

        if( d.Mask == null || d.Mask.Radius <= 0f )
        {
          result.Warnings.Add(
              $"{file.Path}: mask radius is missing or non-positive, which generates no mask at " +
              "all - skipped" );
          continue;
        }

        // Empty counts as missing. A device with no host entries can never mount anything, so it
        // is named here rather than vanishing at resolve time. A published "hosts": null or
        // "hosts": [null] arrives this way, since CotiDeviceDto.ToShared substitutes rather than
        // throwing on the ungated publish route.
        if( d.Hosts == null || d.Hosts.Count == 0 || d.Mount == null )
        {
          result.Warnings.Add(
              $"{file.Path}: hosts is null or empty, or mount is null - skipped" );
          continue;
        }

        if( byDevice.TryGetValue( d.Device, out var owner ) )
        {
          result.Warnings.Add( $"{file.Path}: device \"{d.Device}\" already defined by {owner} - skipped" );
          continue;
        }

        string? clashingId = null;
        string? clashingOwner = null;

        foreach( var host in d.Hosts )
        {
          // A null entry in "hosts" (hand-edited "hosts": [null]) skips itself rather than taking
          // the whole file down.
          if( host?.Id is null )
            continue;

          if( byHostId.TryGetValue( host.Id, out var existingOwner ) )
          {
            clashingId = host.Id;
            clashingOwner = existingOwner;
            break;
          }
        }

        if( clashingId != null )
        {
          // An untuned stub losing to a tuned device is the expected outcome of installing an
          // addon over an auto-discovered guess, so it is a note: the stub file can be deleted.
          if( !d.Tuned )
          {
            result.Notes.Add(
                $"{file.Path}: host {clashingId} is covered by {clashingOwner}, which is tuned - " +
                "this auto-generated stub is superseded and can be deleted." );
          }
          else
          {
            result.Warnings.Add(
                $"{file.Path}: host {clashingId} already belongs to {clashingOwner} - skipped. " +
                "One host cannot have two poses." );
          }

          continue;
        }

        byDevice[d.Device] = file.Path;

        foreach( var host in d.Hosts )
        {
          if( host?.Id is not null )
            byHostId[host.Id] = file.Path;
        }

        // Last, so it only sees a file that is kept. It falls back towards a single COTI and never skips the file.
        CotiTubeValidation.Normalise( d, file.Path, result.Warnings );

        result.Devices.Add( d );
      }

      return result;
    }
  }
}
