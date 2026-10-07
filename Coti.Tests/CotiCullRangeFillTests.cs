using Coti.Shared;
using Xunit;

// The non-allocating form gives the same distances, and a refill fully replaces the previous range's.
public class CotiCullRangeFillTests
{
    [Fact]
    public void FillingABufferMatchesTheAllocatingForm()
    {
        var eye = new[] { 1000f, 0f, 3000f, 400f };
        var into = new float[4];

        CotiCullRange.Distances( eye, 10000f, 600f, into );

        Assert.Equal( CotiCullRange.Distances( eye, 10000f, 600f ), into );
    }

    [Fact]
    public void ARefillWithNoRangeRestoresTheEyesDistances()
    {
        var eye = new[] { 1000f, 0f, 3000f };
        var into = new float[3];

        CotiCullRange.Distances( eye, 10000f, 600f, into );
        CotiCullRange.Distances( eye, 10000f, 0f, into );

        Assert.Equal( new[] { 1000f, 0f, 3000f }, into );
    }
}
