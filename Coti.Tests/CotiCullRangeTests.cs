using Coti.Shared;
using Xunit;

// The thermal cameras cull each layer where the eye's camera does, and no further than the thermal's range.
public class CotiCullRangeTests
{
    [Fact]
    public void WithNoRangeTheEyesDistancesAreTakenAsTheyAre()
    {
        var eye = new[] { 1000f, 0f, 3000f };

        Assert.Equal( new[] { 1000f, 0f, 3000f }, CotiCullRange.Distances( eye, 10000f, 0f ) );
    }

    [Fact]
    public void ARangeCapsEveryLayer()
    {
        var eye = new[] { 1000f, 3000f, 400f };

        Assert.Equal( new[] { 600f, 600f, 400f }, CotiCullRange.Distances( eye, 10000f, 600f ) );
    }

    [Fact]
    public void ALayerOnTheFarClipIsCappedToo()
    {
        // 0 in layerCullDistances means "the far clip plane", here 10 km.
        Assert.Equal( new[] { 600f }, CotiCullRange.Distances( new[] { 0f }, 10000f, 600f ) );
    }

    [Fact]
    public void ARangeBeyondTheEyeChangesNothing()
    {
        var eye = new[] { 1000f, 0f };

        Assert.Equal( new[] { 1000f, 2000f }, CotiCullRange.Distances( eye, 2000f, 5000f ) );
    }

    [Fact]
    public void NoEyeDistancesGiveNone()
    {
        Assert.Empty( CotiCullRange.Distances( null, 10000f, 600f ) );
    }
}
