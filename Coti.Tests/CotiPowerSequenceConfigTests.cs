using Coti.Client;
using Coti.Shared;
using Xunit;

public class CotiPowerSequenceConfigTests
{
    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var config = new CotiConfig().PowerSequence;

        Assert.True( config.Enabled );
        Assert.Equal( 1.2f, config.InitializingSeconds );
        Assert.Equal( 0.3f, config.WarmingSeconds );
        Assert.Equal( 1.5f, config.PowerOffSeconds );
        Assert.Equal( 1f, config.ClickVolume );
    }

    [Fact]
    public void CopiesItsDurationsIntoTheSequenceTimings()
    {
        var config = new CotiPowerSequenceConfig { InitializingSeconds = 2f, WarmingSeconds = 0.5f, PowerOffSeconds = 3f };
        var timings = new CotiPowerTimings();

        config.CopyTo( timings );

        Assert.Equal( 2.0, timings.InitializingSeconds );
        Assert.Equal( 0.5, timings.WarmingSeconds );
        Assert.Equal( 3.0, timings.PowerOffSeconds );
    }
}
