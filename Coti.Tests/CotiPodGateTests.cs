using System;
using Coti.Shared;
using Xunit;

// Which tubes close when one pod is flipped up. Pinned with C11 True North's own pod rotations at d2cb4ee
// (nvg/chimera/chimera_config.cs, nvg/dtnvs/dtnvs_config.cs): each pod's down ("On") and up ("Off").
public class CotiPodGateTests
{
    private const float Tol = 1e-5f;

    private static readonly CotiQuat ChimeraDown = new CotiQuat( -0.284139991f, -1.14295787e-07f, 3.38721087e-08f, 0.958782852f );
    private static readonly CotiQuat ChimeraLeftUp = new CotiQuat( -0.200030774f, 0.680940151f, -0.201800182f, 0.674970269f );
    private static readonly CotiQuat ChimeraRightUp = new CotiQuat( -0.199279293f, -0.683443785f, 0.202542245f, 0.672435164f );
    private static readonly CotiQuat DtnvsDown = new CotiQuat( -0.707106829f, 0f, 0f, 0.707106709f );
    private static readonly CotiQuat DtnvsLeftUp = new CotiQuat( -0.358683348f, -0.609381855f, 0.609381974f, 0.358683318f );
    private static readonly CotiQuat DtnvsRightUp = new CotiQuat( -0.358683348f, 0.609381855f, -0.609381974f, 0.358683318f );

    // What the device files declare (spec section 8): -33 degrees about X on the Chimera, -90 on the DTNVS.
    private static CotiPodBlock ChimeraPod() => new CotiPodBlock { DownX = -33f };
    private static CotiPodBlock DtnvsPod() => new CotiPodBlock { DownX = -90f };

    [Fact]
    public void DownIsUnitysEulerZThenXThenY()
    {
        // Quaternion.Euler(10, 20, 30) in Unity.
        var q = CotiPodGate.Down( new CotiPodBlock { DownX = 10f, DownY = 20f, DownZ = 30f } );

        Assert.Equal( 0.1276794f, q.X, Tol );
        Assert.Equal( 0.1448781f, q.Y, Tol );
        Assert.Equal( 0.2392983f, q.Z, Tol );
        Assert.Equal( 0.9515485f, q.W, Tol );
    }

    [Fact]
    public void TheDeclaredDownsAreC11sOwn()
    {
        Assert.InRange( CotiPodGate.AngleDegrees( CotiPodGate.Down( ChimeraPod() ), ChimeraDown ), 0f, 0.1f );
        Assert.InRange( CotiPodGate.AngleDegrees( CotiPodGate.Down( DtnvsPod() ), DtnvsDown ), 0f, 0.1f );
    }

    [Fact]
    public void C11sPodsSwingFarPastTheGate()
    {
        Assert.Equal( 90.50f, CotiPodGate.AngleDegrees( ChimeraDown, ChimeraLeftUp ), 0.05f );
        Assert.Equal( 90.93f, CotiPodGate.AngleDegrees( ChimeraDown, ChimeraRightUp ), 0.05f );
        Assert.Equal( 119.04f, CotiPodGate.AngleDegrees( DtnvsDown, DtnvsLeftUp ), 0.05f );
        Assert.Equal( 119.04f, CotiPodGate.AngleDegrees( DtnvsDown, DtnvsRightUp ), 0.05f );
    }

    [Fact]
    public void ADeployedPodIsDown()
    {
        Assert.False( CotiPodGate.IsUp( ChimeraDown, ChimeraPod() ) );
        Assert.False( CotiPodGate.IsUp( DtnvsDown, DtnvsPod() ) );
    }

    [Fact]
    public void AStowedPodIsUp()
    {
        Assert.True( CotiPodGate.IsUp( ChimeraLeftUp, ChimeraPod() ) );
        Assert.True( CotiPodGate.IsUp( ChimeraRightUp, ChimeraPod() ) );
        Assert.True( CotiPodGate.IsUp( DtnvsLeftUp, DtnvsPod() ) );
        Assert.True( CotiPodGate.IsUp( DtnvsRightUp, DtnvsPod() ) );
    }

    [Fact]
    public void TheQuaternionsSignDoesNotMatter()
    {
        var flipped = new CotiQuat( -ChimeraDown.X, -ChimeraDown.Y, -ChimeraDown.Z, -ChimeraDown.W );

        Assert.False( CotiPodGate.IsUp( flipped, ChimeraPod() ) );
    }

    // C11 swings a pod by lerping between its two rotations: 45 degrees falls a little past halfway on the Chimera.
    [Theory]
    [InlineData( 0.45f, false )]   // 40.5 degrees from down
    [InlineData( 0.55f, true )]    // 50.0 degrees
    public void ThePodTurnsUpAt45Degrees( float t, bool up )
    {
        Assert.Equal( up, CotiPodGate.IsUp( Lerp( ChimeraDown, ChimeraLeftUp, t ), ChimeraPod() ) );
    }

    // Unity's Quaternion.Lerp: componentwise, then normalised.
    private static CotiQuat Lerp( CotiQuat a, CotiQuat b, float t )
    {
        var x = a.X + ( b.X - a.X ) * t;
        var y = a.Y + ( b.Y - a.Y ) * t;
        var z = a.Z + ( b.Z - a.Z ) * t;
        var w = a.W + ( b.W - a.W ) * t;
        var n = (float)Math.Sqrt( x * x + y * y + z * z + w * w );
        return new CotiQuat( x / n, y / n, z / n, w / n );
    }
}
