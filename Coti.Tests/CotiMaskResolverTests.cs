using System.Collections.Generic;
using Coti.Client;
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
        // Guards the server/client agreement: the shared host table and the client's mask
        // names must not drift apart.
        var config = new CotiConfig();
        config.NvgHosts = new Dictionary<string, CotiNvgHostConfig>();
        foreach( var host in Coti.Shared.CotiNvgHosts.All )
            config.NvgHosts[ host.TemplateId ] = new CotiNvgHostConfig { MaskName = host.MaskName };

        foreach( var host in Coti.Shared.CotiNvgHosts.All )
            Assert.Equal( host.MaskName, CotiMaskResolver.ResolveMaskName( config, host.TemplateId ) );
    }
}
