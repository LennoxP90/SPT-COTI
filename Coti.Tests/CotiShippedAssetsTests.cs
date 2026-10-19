using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Coti.Shared;
using Xunit;

// The click and the two messages ship as loose files, and the client runs silent or textless
// when one is missing or unreadable. These pin that the committed files are the ones intended.
public class CotiShippedAssetsTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo( AppContext.BaseDirectory );
        while( directory != null && !File.Exists( Path.Combine( directory.FullName, "SPT-COTI.slnx" ) ) )
            directory = directory.Parent;

        Assert.NotNull( directory );
        return directory!.FullName;
    }

    [Fact]
    public void CalibrationClickIsAShortMonoClip()
    {
        var path = Path.Combine( RepoRoot(), "sounds", "coti_calibration_click.wav" );

        Assert.True( CotiWavReader.TryRead( File.ReadAllBytes( path ), out var wav, out var error ), error );
        Assert.Equal( 1, wav.Channels );
        Assert.Equal( 48000, wav.SampleRate );
        Assert.InRange( wav.FrameCount, 1400, 1700 );

        var peak = wav.Samples.Max( sample => Math.Abs( sample ) );
        Assert.InRange( peak, 0.5f, 0.75f );
        Assert.Equal( 0f, wav.Samples[0], 3 );
        Assert.Equal( 0f, wav.Samples[^1], 3 );
    }

    [Theory]
    [InlineData( "coti_text_initializing.png" )]
    [InlineData( "coti_text_power_off.png" )]
    [InlineData( "coti_text_mode_outline.png" )]
    [InlineData( "coti_text_mode_full.png" )]
    public void MessageImagesArePngs( string file )
    {
        var bytes = File.ReadAllBytes( Path.Combine( RepoRoot(), "textures", file ) );

        Assert.Equal( new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take( 4 ).ToArray() );
    }

    // Cropped to their letters with a 2 px pad: each copy on screen is sized from the image's own texels,
    // so a stray border would push the copies on neighbouring tubes into each other.
    [Theory]
    [InlineData( "coti_text_initializing.png", 530, 96 )]
    [InlineData( "coti_text_power_off.png", 535, 86 )]
    [InlineData( "coti_text_mode_outline.png", 337, 86 )]
    [InlineData( "coti_text_mode_full.png", 176, 86 )]
    public void MessageImagesAreCroppedToTheirLetters( string file, int width, int height )
    {
        var bytes = File.ReadAllBytes( Path.Combine( RepoRoot(), "textures", file ) );

        // The IHDR chunk: width and height, big-endian, at bytes 16 and 20.
        Assert.Equal( width, BinaryPrimitives.ReadInt32BigEndian( bytes.AsSpan( 16, 4 ) ) );
        Assert.Equal( height, BinaryPrimitives.ReadInt32BigEndian( bytes.AsSpan( 20, 4 ) ) );
    }

    // The riser's sight text draws Nunito from the coti_shaders bundle; the OFL requires its licence to travel
    // with it, and the loose TTF, which the game cannot draw, no longer ships.
    [Fact]
    public void TheSightFontShipsWithItsLicence()
    {
        Assert.False( File.Exists( Path.Combine( RepoRoot(), "fonts", "Nunito-Bold.ttf" ) ) );
        Assert.True( File.Exists( Path.Combine( RepoRoot(), "bundles", "coti_shaders" ) ) );

        var licence = File.ReadAllText( Path.Combine( RepoRoot(), "fonts", "OFL.txt" ) );
        Assert.Contains( "The Nunito Project Authors", licence );
        Assert.Contains( "SIL OPEN FONT LICENSE Version 1.1", licence );
    }

    // Without a readable lamps.json no lamp shows heat at all.
    [Fact]
    public void LampListReadsAndNamesThePickedLamps()
    {
        var heated = Coti.Client.CotiLampFile.Parse( File.ReadAllText( Path.Combine( RepoRoot(), "lamps.json" ) ), out var error );

        Assert.True( heated != null, error );
        Assert.Contains( "Searchlight_01_B_source_on", heated );
        Assert.Contains( "Searchlight_02_source_on", heated );
        Assert.Contains( "Searchlight_03_source_on", heated );
        Assert.All( heated, type => Assert.Equal( type, CotiLampHeat.TypeOf( type ) ) );
    }

    // Without a readable riser-sights.json the server denies nothing, and the eight sights that failed the fit check go
    // back on the riser.
    [Fact]
    public void RiserDenyListReadsAndHoldsTheFitCheckDenials()
    {
        var json = File.ReadAllText( Path.Combine( RepoRoot(), "Coti.Server", "config", Coti.Server.ThermalRiserSightsFile.FileName ) );
        var file = Coti.Server.ThermalRiserSightsFile.Parse( json, out var error );

        Assert.True( file != null, error );
        Assert.Equal( string.Empty, error );
        Assert.Equal(
            new[]
            {
                "f849f458cb4ad62c5d8b96e1", "86f1db244188717106a25806", "6a16dc0b82cfbdc3ab09f58a", "68611c8d57834d1471676982",
                "68611d271b5181217dee6c31", "68611d79d2f8c2caf35ddd39", "696958420d2309811a0fd1d5", "682be7bf16812803987c3715",
            },
            file!.Deny.Select( d => d.Id ) );
        Assert.All( file.Deny, d => Assert.False( string.IsNullOrWhiteSpace( d.Note ) ) );
    }
}
