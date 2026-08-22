using System;
using System.Collections.Generic;
// Enumerable.Contains's comparer overload below. Coti.Client sets no ImplicitUsings, so dropping
// this compiles here and fails there.
using System.Linq;

namespace Coti.Shared
{
  /// <summary>
  /// A host that resolved, carrying both the id it resolved to and the entry as authored.
  /// <c>Id</c> and <c>Declared.Id</c> differ exactly on hosts recovered by prefab.
  /// </summary>
  public class CotiResolvedHost
  {
    public CotiResolvedHost( string id, CotiDeviceFile device, CotiHostRef declared )
    {
      Id = id;
      Device = device;
      Declared = declared;
    }

    /// <summary>The id the item table really has. Also this entry's key in ByHostId.</summary>
    public string Id { get; }

    public CotiDeviceFile Device { get; }

    /// <summary>
    /// The <c>hosts</c> entry as authored. Its own Id is the STALE one when a prefab fallback
    /// recovered this host, so never key anything on it - it is here for Prefab and Label.
    /// </summary>
    public CotiHostRef Declared { get; }
  }

  public class CotiResolveResult
  {
    public Dictionary<string, CotiResolvedHost> ByHostId { get; } =
        new Dictionary<string, CotiResolvedHost>();

    /// <summary>
    /// Every device that resolved at least one host, with its hosts EXACTLY as authored. This is
    /// the shape the file has, and nothing may rewrite it: a recovered id is never written back
    /// to an addon author's file behind their back.
    /// </summary>
    public List<CotiDeviceFile> Devices { get; } = new List<CotiDeviceFile>();

    /// <summary>
    /// The devices as they should go over the wire: each host's id replaced by whatever it actually
    /// resolved to, so a client keys on the id the server fitted rather than the one declared.
    /// Prefab and label are carried across unchanged.
    /// </summary>
    public List<CotiDeviceFile> ResolvedDevices { get; } = new List<CotiDeviceFile>();

    /// <summary>Something a human should act on.</summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>Normal, expected outcomes. A supported host from an uninstalled mod is one.</summary>
    public List<string> Notes { get; } = new List<string>();
  }

  public static class CotiHostResolver
  {
    public static CotiResolveResult Resolve(
        CotiMergeResult merged, ICotiItemView items, ISet<string> loadedModGuids )
    {
      var result = new CotiResolveResult();
      Dictionary<string, List<string>>? prefabIndex = null;

      // Every id already placed on the wire, across ALL devices. The client keys one dictionary on
      // these, so the same id must never be emitted twice: whichever device came last would own
      // the pose and mask on the client while the server had fitted the slot for the other one -
      // the very confusion the occupancy guard below exists to prevent, leaking through the wire
      // instead of the table. It can only arise from a refused host, since CotiDeviceMerge already
      // rejects a declared id another file owns and ByHostId cannot hold one twice.
      var wireIds = new HashSet<string>();

      foreach( var device in merged.Devices )
      {
        if( !string.IsNullOrWhiteSpace( device.Requires )
            && !loadedModGuids.Contains( device.Requires, StringComparer.OrdinalIgnoreCase ) )
        {
          result.Warnings.Add(
              $"[{device.Device}] requires mod \"{device.Requires}\", which is not loaded - skipped" );
          continue;
        }

        // Merge guards this for a file off disk, but Resolve is public, so a device with no Hosts must
        // not throw here.
        if( device.Hosts == null )
        {
          result.Notes.Add( $"[{device.Device}] has no hosts - skipped" );
          continue;
        }

        var resolvedAny = false;
        var wireHosts = new List<CotiHostRef>();

        foreach( var host in device.Hosts )
        {
          // A hand-authored "hosts": [null] deserialises to a list containing a null entry, and
          // a host with no id at all carries nothing to resolve by. Skip it rather than let the
          // next line NRE and take the whole Resolve call down over one malformed entry.
          if( host?.Id is null )
            continue;

          var resolvedId = ResolveOne( result, items, ref prefabIndex, device, host );

          if( resolvedId != null )
            resolvedAny = true;

          AddWireHost( wireHosts, wireIds, host, resolvedId ?? host.Id );
        }

        if( resolvedAny )
        {
          result.Devices.Add( device );
          result.ResolvedDevices.Add( WithHosts( device, wireHosts ) );
        }
      }

      return result;
    }

    /// <summary>
    /// One declared host entry, resolved to the id the item table really has - or null when it
    /// resolved to nothing. Every diagnostic for that entry is raised here, so the caller only has
    /// to decide what to put on the wire.
    /// </summary>
    private static string? ResolveOne(
        CotiResolveResult result, ICotiItemView items,
        ref Dictionary<string, List<string>>? prefabIndex, CotiDeviceFile device, CotiHostRef host )
    {
      var hostLabel = string.IsNullOrEmpty( host.Id ) ? "(no id)" : host.Id;

      if( !string.IsNullOrEmpty( host.Id ) && items.Exists( host.Id ) )
        return Claim( result, device, host, host.Id );

      if( string.IsNullOrEmpty( host.Prefab ) )
      {
        result.Notes.Add( $"[{device.Device}] host {hostLabel} not installed - skipped" );
        return null;
      }

      // Built on FIRST miss only, so a healthy install never pays for it.
      prefabIndex ??= BuildPrefabIndex( items );

      if( !prefabIndex.TryGetValue( host.Prefab, out var matches ) )
      {
        result.Notes.Add(
            $"[{device.Device}] host {hostLabel} not installed and no item uses prefab " +
            $"\"{host.Prefab}\" - skipped" );
        return null;
      }

      if( matches.Count > 1 )
      {
        result.Warnings.Add(
            $"[{device.Device}] host {hostLabel} is gone and prefab \"{host.Prefab}\" is " +
            $"ambiguous across {matches.Count} items - skipped" );
        return null;
      }

      var claimed = Claim( result, device, host, matches[0] );

      if( claimed != null )
      {
        result.Warnings.Add(
            $"[{device.Device}] host id {hostLabel} is gone; matched by prefab \"{host.Prefab}\" " +
            $"to {matches[0]}. Update the device file." );
      }

      return claimed;
    }

    /// <summary>
    /// Claims a host id for a device, or refuses if another device already has it. Without the
    /// occupancy check a device could silently take another's host and mount it with the wrong pose.
    /// </summary>
    private static string? Claim(
        CotiResolveResult result, CotiDeviceFile device, CotiHostRef declared, string key )
    {
      if( result.ByHostId.TryGetValue( key, out var owner ) )
      {
        if( owner.Device != device )
        {
          result.Warnings.Add(
              $"[{device.Device}] host {key} already resolved to \"{owner.Device.Device}\" - skipped" );
        }

        return null;
      }

      result.ByHostId[key] = new CotiResolvedHost( key, device, declared );
      return key;
    }

    /// <summary>
    /// Prefab and Label come from the declared entry; only the id changes. An id already on the
    /// wire is dropped rather than repeated, whether it was this device that placed it (a host
    /// declared both directly and by a stale id whose prefab resolves back to it) or another
    /// device that won it - see wireIds at the top of Resolve.
    /// </summary>
    private static void AddWireHost(
        List<CotiHostRef> wireHosts, HashSet<string> wireIds, CotiHostRef declared, string id )
    {
      if( !wireIds.Add( id ) )
        return;

      wireHosts.Add( new CotiHostRef { Id = id, Prefab = declared.Prefab, Label = declared.Label } );
    }

    private static CotiDeviceFile WithHosts( CotiDeviceFile device, List<CotiHostRef> hosts )
    {
      return new CotiDeviceFile
      {
        Schema = device.Schema,
        Device = device.Device,
        DisplayName = device.DisplayName,
        Requires = device.Requires,
        Tuned = device.Tuned,
        Hosts = hosts,
        Mask = device.Mask,
        Mount = device.Mount,
      };
    }

    /// <summary>
    /// Null and empty prefab paths are EXCLUDED. Upstream annotates the field as possibly an
    /// object, an empty string or a string, and every prefab-less item in the database would
    /// otherwise collide into one bucket.
    /// </summary>
    private static Dictionary<string, List<string>> BuildPrefabIndex( ICotiItemView items )
    {
      var index = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );

      foreach( var id in items.AllIds() )
      {
        var path = items.PrefabPath( id );
        if( string.IsNullOrEmpty( path ) )
          continue;

        if( !index.TryGetValue( path, out var list ) )
          index[path] = list = new List<string>();
        list.Add( id );
      }

      return index;
    }
  }
}
