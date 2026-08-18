using Coti.Client;
using Xunit;

// ComputeCoverage draws the feathered edge of the overlay's circular mask. Its failure
// mode is subtle - a wrong falloff still looks plausible in raid - so the boundaries and
// the midpoint are pinned rather than eyeballed.
public class MaskGeometryTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData( 9f, 1f )]
    [InlineData( 10f, 1f )]    // exactly on the radius, inclusive
    [InlineData( 10.1f, 0f )]
    public void ZeroFeatherGivesAHardEdge( float distance, float expected )
    {
        Assert.Equal( expected, MaskGeometry.ComputeCoverage( distance, 10f, 0f ), Tolerance );
    }

    [Fact]
    public void NegativeFeatherIsTreatedAsAHardEdge()
    {
        Assert.Equal( 1f, MaskGeometry.ComputeCoverage( 9f, 10f, -1f ), Tolerance );
        Assert.Equal( 0f, MaskGeometry.ComputeCoverage( 11f, 10f, -1f ), Tolerance );
    }

    // radius 10, feather 4 -> inner 8, outer 12
    [Theory]
    [InlineData( 0f, 1f )]
    [InlineData( 8f, 1f )]      // on the inner boundary
    [InlineData( 9f, 0.75f )]
    [InlineData( 10f, 0.5f )]   // the nominal radius sits mid-falloff
    [InlineData( 11f, 0.25f )]
    [InlineData( 12f, 0f )]     // on the outer boundary
    [InlineData( 20f, 0f )]
    public void FeatheredEdgeFallsOffLinearly( float distance, float expected )
    {
        Assert.Equal( expected, MaskGeometry.ComputeCoverage( distance, 10f, 4f ), Tolerance );
    }

    [Fact]
    public void CoverageIsAlwaysHalfAtTheNominalRadius()
    {
        // The feather is symmetric about the radius, so this holds for any width.
        foreach( var feather in new[] { 0.5f, 2f, 4f, 9f } )
            Assert.Equal( 0.5f, MaskGeometry.ComputeCoverage( 10f, 10f, feather ), Tolerance );
    }

    [Fact]
    public void CoverageNeverIncreasesWithDistance()
    {
        var previous = 1f;
        for( var distance = 0f; distance <= 20f; distance += 0.25f )
        {
            var coverage = MaskGeometry.ComputeCoverage( distance, 10f, 4f );
            Assert.True( coverage <= previous + Tolerance,
                $"coverage rose at distance {distance}: {previous} -> {coverage}" );
            previous = coverage;
        }
    }

    [Fact]
    public void CoverageStaysWithinZeroAndOne()
    {
        foreach( var radius in new[] { 0f, 1f, 10f } )
        foreach( var feather in new[] { 0f, 1f, 5f } )
        for( var distance = 0f; distance <= 25f; distance += 0.5f )
            Assert.InRange( MaskGeometry.ComputeCoverage( distance, radius, feather ), 0f, 1f );
    }

    [Fact]
    public void AFeatherWiderThanTheRadiusNeverReachesFullCoverage()
    {
        // radius 2, feather 10 -> inner -3, so no distance is ever inside the inner
        // boundary and the mask tops out below 1 at its own centre. This documents the
        // behaviour rather than endorsing it.
        Assert.Equal( 0.7f, MaskGeometry.ComputeCoverage( 0f, 2f, 10f ), Tolerance );
    }
}
