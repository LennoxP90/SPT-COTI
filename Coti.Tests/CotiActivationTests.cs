using Coti.Client;
using Xunit;

// ShouldBeActive decides whether the overlay renders at all, and a wrong answer is invisible at
// build time, so every input combination is pinned.
public class CotiActivationTests
{
    [Theory]
    [InlineData( true, true, true )]
    [InlineData( true, true, false )]
    [InlineData( true, false, true )]
    [InlineData( true, false, false )]
    [InlineData( false, true, true )]
    [InlineData( false, true, false )]
    [InlineData( false, false, true )]
    [InlineData( false, false, false )]
    public void HeadlessIsNeverActive( bool attached, bool hostOn, bool poweredOn )
    {
        // The headless has no camera effects, so it short-circuits ahead of everything else.
        Assert.False( CotiActivation.ShouldBeActive( true, attached, hostOn, poweredOn ) );
    }

    [Fact]
    public void ActiveOnlyWhenAttachedAndHostOnAndPowered()
    {
        Assert.True( CotiActivation.ShouldBeActive( false, true, true, true ) );
    }

    [Theory]
    [InlineData( false, true, true )]   // not attached
    [InlineData( true, false, true )]   // host goggles off
    [InlineData( true, true, false )]   // COTI itself powered off
    [InlineData( false, false, true )]
    [InlineData( false, true, false )]
    [InlineData( true, false, false )]
    [InlineData( false, false, false )]
    public void AnyMissingConditionDisablesIt( bool attached, bool hostOn, bool poweredOn )
    {
        // Every condition is ANDed: a powered COTI on unpowered goggles still shows nothing.
        Assert.False( CotiActivation.ShouldBeActive( false, attached, hostOn, poweredOn ) );
    }

    [Fact]
    public void DisplayShowsOnlyWhenAttachedTubeLitAndDisplayLit()
    {
        Assert.True( CotiActivation.ShouldShowDisplay( false, true, true, true ) );
    }

    [Theory]
    [InlineData( false, true, true )]
    [InlineData( true, false, true )]
    [InlineData( true, true, false )]
    public void AnyMissingConditionHidesTheDisplay( bool attached, bool hostOn, bool lit )
    {
        // Seen through the tube like the thermal is, so a raised goggle hides the message.
        Assert.False( CotiActivation.ShouldShowDisplay( false, attached, hostOn, lit ) );
    }

    [Fact]
    public void HeadlessNeverShowsTheDisplay()
    {
        Assert.False( CotiActivation.ShouldShowDisplay( true, true, true, true ) );
    }
}
