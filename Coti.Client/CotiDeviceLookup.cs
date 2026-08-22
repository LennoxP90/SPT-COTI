using System.Collections.Generic;
using Coti.Shared;

namespace Coti.Client
{
  /// <summary>
  /// Finds which device file declares a given host id. Shared by both editors so there is one
  /// answer. Tolerates nulls throughout, because the table arrives over the wire.
  ///
  /// Pure and Unity-free, so it is source-linked into Coti.Tests.
  /// </summary>
  public static class CotiDeviceLookup
  {
    /// <summary>
    /// The device declaring <paramref name="hostId"/>, or null. Tolerates nulls throughout the
    /// list because it reads a table that arrived over the wire.
    /// </summary>
    public static CotiDeviceFile? ByHostId( IEnumerable<CotiDeviceFile?>? devices, string? hostId )
    {
      if( devices == null || string.IsNullOrEmpty( hostId ) )
        return null;

      foreach( var device in devices )
      {
        if( device?.Hosts == null )
          continue;

        foreach( var hostRef in device.Hosts )
        {
          if( hostRef?.Id == hostId )
            return device;
        }
      }

      return null;
    }
  }
}
