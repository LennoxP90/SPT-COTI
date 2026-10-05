using System.Collections.Generic;
using Coti.Shared;
using Xunit;

// A lamp's heat comes from where its glow is, not from where its light components were placed.
public class CotiLampHeatTests
{
    [Theory]
    [InlineData( "Streetlight_lamp_06_A_source_on (12)", "Streetlight_lamp_06_A_source_on" )]
    [InlineData( "Searchlight_03_source_on(Clone)", "Searchlight_03_source_on" )]
    [InlineData( "Searchlight_03_source_on (3)(Clone)", "Searchlight_03_source_on" )]
    [InlineData( "Lamp_SummerHotel_destroyable (1) (2)", "Lamp_SummerHotel_destroyable" )]
    [InlineData( "lamp03_destroyable", "lamp03_destroyable" )]      // digits in the name stay
    [InlineData( "Chandelier_04", "Chandelier_04" )]
    [InlineData( "", "" )]
    [InlineData( null, "" )]
    public void ALampsTypeIsItsNameWithoutCopyMarks( string name, string type ) => Assert.Equal( type, CotiLampHeat.TypeOf( name ) );

    [Fact]
    public void TheLampFileListsTypesWithCommentsAllowedAndCaseIgnored()
    {
        var heated = Coti.Client.CotiLampFile.Parse( "// lamps\n{ \"heated\": [ \"Searchlight_03_source_on\", \"\", 3 ] } // end", out var error );

        Assert.Null( error );
        Assert.Single( heated );
        Assert.Contains( "searchlight_03_SOURCE_on", heated );
    }

    [Theory]
    [InlineData( "{ \"lamps\": [] }" )]
    [InlineData( "{ \"heated\": \"Searchlight_03_source_on\" }" )]
    [InlineData( "not json" )]
    public void ALampFileItCannotReadGivesNoListAndSaysWhy( string json )
    {
        Assert.Null( Coti.Client.CotiLampFile.Parse( json, out var error ) );
        Assert.False( string.IsNullOrEmpty( error ) );
    }

    [Fact]
    public void TwoHeadsAreTwoSpotsEachAtItsOwnMiddle()
    {
        var points = new List<float>();
        Blob( points, -0.4f, 1.8f, 0f, 0.1f );
        Blob( points, 0.4f, 1.8f, 0f, 0.1f );

        var spots = CotiLampHeat.Spots( points );

        Assert.Equal( 2, spots.Count );
        Assert.Contains( spots, s => Near( s.X, -0.4f ) && Near( s.Y, 1.8f ) );
        Assert.Contains( spots, s => Near( s.X, 0.4f ) && Near( s.Y, 1.8f ) );
        Assert.All( spots, s => Assert.InRange( s.Radius, 0.09f, 0.15f ) );
    }

    [Fact]
    public void ATubeIsOneSpotAsLongAsItIs()
    {
        var points = new List<float>();
        for( var i = 0; i <= 24; i++ )
            points.AddRange( new[] { -0.6f + 0.05f * i, 2.5f, 0f } );

        var spot = Assert.Single( CotiLampHeat.Spots( points ) );

        Assert.True( Near( spot.X, 0f ) );
        Assert.InRange( spot.Radius, 0.55f, 0.65f );
    }

    [Fact]
    public void AtMostMaxBulbsLargestFirst()
    {
        var points = new List<float>();
        for( var i = 0; i < 10; i++ )
            for( var n = 0; n <= i; n++ )   // spot i has i + 1 points
                points.AddRange( new[] { i * 1f, 0f, 0.01f * n } );

        var spots = CotiLampHeat.Spots( points );

        Assert.Equal( CotiLampHeat.MaxBulbs, spots.Count );
        Assert.Equal( 10, spots[0].Points );
        Assert.Equal( 3, spots[CotiLampHeat.MaxBulbs - 1].Points );
    }

    [Fact]
    public void SpotsFarFromTheOriginStillGroup()
    {
        var points = new List<float>();
        Blob( points, -1200.3f, 505.9f, -4300.7f, 0.05f );

        var spot = Assert.Single( CotiLampHeat.Spots( points ) );

        Assert.True( Near( spot.X, -1200.3f ) && Near( spot.Z, -4300.7f ) );
    }

    [Fact]
    public void NoPointsNoSpots() => Assert.Empty( CotiLampHeat.Spots( new List<float>() ) );

    [Theory]
    [InlineData( 0f, 0.1f )]      // a single glowing texel
    [InlineData( 0.2f, 0.5f )]    // a floodlight's lens
    [InlineData( 0.6f, 1f )]      // a long tube, held to the largest
    public void CubesScaleWithTheSpotWithinBounds( float radius, float expected )
    {
        Assert.Equal( expected, CotiLampHeat.CubeMetres( radius ), 3 );
    }

    // Points on a sphere's surface around a middle, as a lens's glowing texels would be.
    private static void Blob( List<float> points, float x, float y, float z, float radius )
    {
        for( var a = 0; a < 12; a++ )
            for( var b = 1; b < 6; b++ )
            {
                var theta = a * System.Math.PI / 6;
                var phi = b * System.Math.PI / 6;
                points.Add( x + radius * (float)( System.Math.Sin( phi ) * System.Math.Cos( theta ) ) );
                points.Add( y + radius * (float)System.Math.Cos( phi ) );
                points.Add( z + radius * (float)( System.Math.Sin( phi ) * System.Math.Sin( theta ) ) );
            }
    }

    private static bool Near( float a, float b ) => System.Math.Abs( a - b ) < 0.02f;
}
