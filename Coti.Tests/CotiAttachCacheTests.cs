using Coti.Client;
using Xunit;

// The probe counts its own calls rather than the cache exposing a counter, so nothing here exists
// in the production type only for a test to read.
public class CotiAttachCacheTests
{
    private sealed class Host
    {
        public readonly bool Attached;

        public Host( bool attached = true )
        {
            Attached = attached;
        }

        // Equal to every other Host: the cache keys on reference identity, and this makes an
        // Equals-based cache fail the tests below.
        public override bool Equals( object obj ) => obj is Host;

        public override int GetHashCode() => 1;
    }

    private static CotiAttachCache<Host> Cache( out int[] calls )
    {
        var counter = new int[1];
        calls = counter;
        return new CotiAttachCache<Host>( host =>
        {
            counter[0]++;
            return host != null && host.Attached;
        } );
    }

    [Fact]
    public void TheFirstReadProbes()
    {
        int[] calls;
        var cache = Cache( out calls );

        Assert.True( cache.Read( new Host() ) );
        Assert.Equal( 1, calls[0] );
    }

    [Fact]
    public void ASecondReadOfTheSameHostDoesNotProbeAgain()
    {
        int[] calls;
        var cache = Cache( out calls );
        var host = new Host();

        cache.Read( host );
        cache.Read( host );
        cache.Read( host );

        Assert.Equal( 1, calls[0] );
    }

    [Fact]
    public void TheCachedValueIsReturnedOnASecondRead()
    {
        int[] calls;
        var cache = Cache( out calls );
        var host = new Host( attached: true );

        Assert.True( cache.Read( host ) );
        Assert.True( cache.Read( host ) );
    }

    [Fact]
    public void InvalidateForcesTheNextReadToProbe()
    {
        int[] calls;
        var cache = Cache( out calls );
        var host = new Host();

        cache.Read( host );
        cache.Invalidate();
        cache.Read( host );

        Assert.Equal( 2, calls[0] );
    }

    [Fact]
    public void InvalidateOnlyCostsOneExtraProbe()
    {
        int[] calls;
        var cache = Cache( out calls );
        var host = new Host();

        cache.Read( host );
        cache.Invalidate();
        cache.Read( host );
        cache.Read( host );

        Assert.Equal( 2, calls[0] );
    }

    [Fact]
    public void ADifferentHostInstanceProbesEvenWithoutInvalidate()
    {
        int[] calls;
        var cache = Cache( out calls );

        // Equal by Equals, different by reference - the inventory handing back a rebuilt item must
        // re-probe, and this is the case a value-equality cache would silently miss.
        cache.Read( new Host() );
        cache.Read( new Host() );

        Assert.Equal( 2, calls[0] );
    }

    [Fact]
    public void ANullHostIsCachedRatherThanProbedEveryRead()
    {
        int[] calls;
        var cache = Cache( out calls );

        Assert.False( cache.Read( null ) );
        Assert.False( cache.Read( null ) );

        Assert.Equal( 1, calls[0] );
    }

    [Fact]
    public void GoingFromAHostToNoHostProbes()
    {
        int[] calls;
        var cache = Cache( out calls );

        cache.Read( new Host() );
        Assert.False( cache.Read( null ) );

        Assert.Equal( 2, calls[0] );
    }

    [Fact]
    public void InvalidatingBeforeAnyReadStillProbesExactlyOnce()
    {
        int[] calls;
        var cache = Cache( out calls );

        cache.Invalidate();
        cache.Read( new Host() );

        Assert.Equal( 1, calls[0] );
    }

    [Fact]
    public void AHostWithNothingAttachedReadsFalseAndStaysCached()
    {
        int[] calls;
        var cache = Cache( out calls );
        var host = new Host( attached: false );

        Assert.False( cache.Read( host ) );
        Assert.False( cache.Read( host ) );

        Assert.Equal( 1, calls[0] );
    }
}
