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
}
