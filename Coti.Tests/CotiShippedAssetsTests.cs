using System;
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
    public void MessageImagesArePngs( string file )
    {
        var bytes = File.ReadAllBytes( Path.Combine( RepoRoot(), "textures", file ) );

        Assert.Equal( new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take( 4 ).ToArray() );
    }
}
