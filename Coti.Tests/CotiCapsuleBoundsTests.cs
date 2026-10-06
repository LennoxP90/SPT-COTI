using System;
using Coti.Shared;
using Xunit;

// A body's bounding sphere must hold every capsule whole, or the mirror skips a body a ray could have hit.
public class CotiCapsuleBoundsTests
{
    [Fact]
    public void TheSphereHoldsEveryCapsuleWhole()
    {
        // Two capsules: a 1 m vertical one at the origin and a short one off to the side.
        var ends = new float[] { 0, 0, 0, 0, 1, 0, 0.5f, 0.5f, 0, 0.8f, 0.5f, 0 };
        var radii = new float[] { 0.1f, 0.2f };
        var s = CotiCapsuleBounds.Sphere( ends, radii, 2 );

        for( var c = 0; c < 2; c++ )
            for( var e = 0; e < 2; e++ )
            {
                var i = c * 6 + e * 3;
                var dx = ends[i] - s.X; var dy = ends[i + 1] - s.Y; var dz = ends[i + 2] - s.Z;
                Assert.True( Math.Sqrt( dx * dx + dy * dy + dz * dz ) + radii[c] <= s.Radius + 1e-5 );
            }
    }

    [Fact]
    public void OneCapsuleGetsASphereNoBiggerThanItNeeds()
    {
        var s = CotiCapsuleBounds.Sphere( new float[] { 0, 0, 0, 0, 2, 0 }, new float[] { 0.5f }, 1 );
        Assert.Equal( 1f, s.Y, 4 );
        Assert.Equal( 1.5f, s.Radius, 4 );
    }

    [Fact]
    public void ABodyHas36CapsulesAndTenBodiesFit()
    {
        Assert.Equal( 36, CotiCapsuleBounds.CapsulesPerBody );
        Assert.Equal( 10, CotiCapsuleBounds.MaxBodies );
    }
}
