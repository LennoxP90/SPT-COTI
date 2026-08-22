using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Coti.Client;
using Newtonsoft.Json;
using Xunit;

// ResolveMaskName has five separate routes to the fallback. A missed branch does not
// throw - it silently draws the wrong mask shape, which is easy to mistake for a
// misconfigured host.
public class CotiMaskResolverTests
{
    private const string HostId = "57235b6f24597759bf5a30f1";  // PVS-14

    private static CotiConfig ConfigWith( string hostId, string maskName )
    {
        var config = new CotiConfig();
        config.NvgHosts = new Dictionary<string, CotiNvgHostConfig>
        {
            { hostId, new CotiNvgHostConfig { MaskName = maskName } },
        };
        return config;
    }

    [Fact]
    public void ReturnsTheConfiguredMaskForAKnownHost()
    {
        Assert.Equal( "pvs14", CotiMaskResolver.ResolveMaskName( ConfigWith( HostId, "pvs14" ), HostId ) );
    }

    [Fact]
    public void FallsBackWhenConfigIsNull()
    {
        Assert.Equal( CotiConfig.FallbackMaskName, CotiMaskResolver.ResolveMaskName( null, HostId ) );
    }

    [Fact]
    public void FallsBackWhenHostIdIsNull()
    {
        Assert.Equal( CotiConfig.FallbackMaskName,
            CotiMaskResolver.ResolveMaskName( ConfigWith( HostId, "pvs14" ), null ) );
    }

    [Fact]
    public void FallsBackWhenHostTableIsNull()
    {
        var config = new CotiConfig();
        config.NvgHosts = null;
        Assert.Equal( CotiConfig.FallbackMaskName, CotiMaskResolver.ResolveMaskName( config, HostId ) );
    }

    [Fact]
    public void FallsBackForAnUnknownHost()
    {
        Assert.Equal( CotiConfig.FallbackMaskName,
            CotiMaskResolver.ResolveMaskName( ConfigWith( HostId, "pvs14" ), "ffffffffffffffffffffffff" ) );
    }

    [Fact]
    public void FallsBackWhenTheHostEntryIsNull()
    {
        var config = new CotiConfig();
        config.NvgHosts = new Dictionary<string, CotiNvgHostConfig> { { HostId, null } };
        Assert.Equal( CotiConfig.FallbackMaskName, CotiMaskResolver.ResolveMaskName( config, HostId ) );
    }

    [Theory]
    [InlineData( null )]
    [InlineData( "" )]
    public void FallsBackWhenTheMaskNameIsMissing( string maskName )
    {
        Assert.Equal( CotiConfig.FallbackMaskName,
            CotiMaskResolver.ResolveMaskName( ConfigWith( HostId, maskName ), HostId ) );
    }

    [Fact]
    public void EveryShippedHostResolvesToItsOwnMask()
    {
        // Guards the server/client agreement: the shipped device files and the client's mask
        // resolution must not drift apart. Reads the same embedded hosts/*.json files
        // CotiHostTableClient falls back to in production, rather than the deleted
        // Coti.Shared.CotiNvgHosts table.
        var config = new CotiConfig();
        config.NvgHosts = new Dictionary<string, CotiNvgHostConfig>();

        foreach( var device in ReadShippedDevices() )
            foreach( var host in device.Hosts )
                config.NvgHosts[ host.Id! ] = new CotiNvgHostConfig { MaskName = device.Device! };

        foreach( var device in ReadShippedDevices() )
            foreach( var host in device.Hosts )
                Assert.Equal( device.Device, CotiMaskResolver.ResolveMaskName( config, host.Id! ) );
    }

    private static List<CotiDeviceDto> ReadShippedDevices()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var result = new List<CotiDeviceDto>();

        // By ".Hosts." and the ".json" suffix rather than the full name - same convention
        // CotiShippedDevicesTests uses, for the same reason: a rename of the link path or the
        // root namespace fails as "no hosts" instead of "resource missing".
        foreach( var name in assembly.GetManifestResourceNames() )
        {
            if( !name.Contains( ".Hosts." ) || !name.EndsWith( ".json" ) )
                continue;

            using var stream = assembly.GetManifestResourceStream( name )!;
            using var reader = new System.IO.StreamReader( stream );
            result.Add( JsonConvert.DeserializeObject<CotiDeviceDto>( reader.ReadToEnd() )! );
        }

        Assert.True( result.Count >= 5, "expected at least 5 shipped device files, found " + result.Count );
        return result;
    }
}
