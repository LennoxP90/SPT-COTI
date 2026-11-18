using Coti.Client.Api;
using Xunit;

// CotiApi.Changed is what an addon plugin waits on, so when it fires is pinned.
public class CotiApiNotifierTests
{
    [Fact]
    public void FirstFrameFires()
    {
        Assert.True( new CotiApiNotifier().Observe( false, false, null ) );
    }

    [Fact]
    public void NothingChangedDoesNotFire()
    {
        var n = new CotiApiNotifier();
        n.Observe( true, true, "5c0696830db834001d23f5da" );
        Assert.False( n.Observe( true, true, "5c0696830db834001d23f5da" ) );
    }

    [Theory]
    [InlineData( false, true, "5c0696830db834001d23f5da" )]
    [InlineData( true, false, "5c0696830db834001d23f5da" )]
    [InlineData( true, true, "57235b6f24597759bf5a30f1" )]
    [InlineData( true, true, null )]
    public void EachFieldFires( bool active, bool attached, string? nightVision )
    {
        var n = new CotiApiNotifier();
        n.Observe( true, true, "5c0696830db834001d23f5da" );
        Assert.True( n.Observe( active, attached, nightVision ) );
    }
}
