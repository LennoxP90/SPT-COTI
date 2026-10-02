using Coti.Client;
using Coti.Shared;
using Xunit;

// EFT's PlayNonspatial applies its volume twice (SetBaseVolume, then PlayOneShot), so the level
// heard is the square of what is passed. Click Volume is meant to be linear.
public class CotiClickTests
{
    [Theory]
    [InlineData( 1f )]
    [InlineData( 0.5f )]
    [InlineData( 0.25f )]
    [InlineData( 0f )]
    public void VolumeSentToEftSquaresBackToTheSetting( float setting )
    {
        var sent = CotiClickVolume.ForPlayNonspatial( setting );
        Assert.Equal( setting, sent * sent, 5 );
    }

    [Theory]
    [InlineData( -1f, 0f )]
    [InlineData( 2f, 1f )]
    [InlineData( float.NaN, 0f )]
    public void VolumeOutsideTheRangeIsClamped( float setting, float expected )
    {
        Assert.Equal( expected, CotiClickVolume.ForPlayNonspatial( setting ), 5 );
    }

    [Fact]
    public void ClicksWhenTheSensorComesOnWithADeviceAttached()
    {
        Assert.True( CotiActivation.ShouldClick( false, true, true, true ) );
    }

    [Theory]
    [InlineData( false, true, true )]   // no sensor start this frame
    [InlineData( true, false, true )]   // no COTI attached: CTRL+N must be silent
    [InlineData( true, true, false )]   // mod switched off
    public void AnyMissingConditionKeepsItSilent( bool clickNow, bool attached, bool enabled )
    {
        Assert.False( CotiActivation.ShouldClick( false, clickNow, attached, enabled ) );
    }

    [Fact]
    public void HeadlessNeverClicks()
    {
        Assert.False( CotiActivation.ShouldClick( true, true, true, true ) );
    }
}

// EFT resets the audio system at startup and at every raid start (AudioUtils.ResetAudioBuffer),
// which empties any clip made with AudioClip.Create. The client rebuilds the clip whenever it
// reads empty.
public class CotiClipCacheTests
{
    [Fact]
    public void BuildsWhenThereIsNoClipYet()
    {
        Assert.True( CotiClipCache.NeedsRebuild( false, 0f ) );
    }

    [Theory]
    [InlineData( 0f )]
    [InlineData( -1f )]
    [InlineData( float.NaN )]
    public void RebuildsAClipAnAudioResetEmptied( float lengthSeconds )
    {
        Assert.True( CotiClipCache.NeedsRebuild( true, lengthSeconds ) );
    }

    [Fact]
    public void KeepsAClipThatStillHasItsData()
    {
        Assert.False( CotiClipCache.NeedsRebuild( true, 0.032f ) );
    }
}
