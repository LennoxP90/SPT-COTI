using System.IO;
using System.Text;
using Coti.Shared;
using Xunit;

// The click is loaded from a file a player can replace, so anything that is not 16-bit PCM must
// be refused with a reason rather than played as noise.
public class CotiWavReaderTests
{
    private static byte[] BuildWav(
        short[] samples, short channels = 1, int rate = 48000, short bits = 16, short format = 1,
        bool oddChunkFirst = false, int truncateBy = 0 )
    {
        var data = new MemoryStream();
        var body = new BinaryWriter( data );
        foreach( var sample in samples )
            body.Write( sample );

        var file = new MemoryStream();
        var writer = new BinaryWriter( file );
        writer.Write( Encoding.ASCII.GetBytes( "RIFF" ) );
        writer.Write( 0 );
        writer.Write( Encoding.ASCII.GetBytes( "WAVE" ) );

        if( oddChunkFirst )
        {
            writer.Write( Encoding.ASCII.GetBytes( "LIST" ) );
            writer.Write( 3 );
            writer.Write( new byte[] { 1, 2, 3, 0 } );
        }

        writer.Write( Encoding.ASCII.GetBytes( "fmt " ) );
        writer.Write( 16 );
        writer.Write( format );
        writer.Write( channels );
        writer.Write( rate );
        writer.Write( rate * channels * bits / 8 );
        writer.Write( (short)( channels * bits / 8 ) );
        writer.Write( bits );

        writer.Write( Encoding.ASCII.GetBytes( "data" ) );
        writer.Write( (int)data.Length );
        writer.Write( data.ToArray() );

        var bytes = file.ToArray();
        return truncateBy == 0 ? bytes : bytes[..( bytes.Length - truncateBy )];
    }

    [Fact]
    public void ReadsMonoSixteenBitAsScaledFloats()
    {
        var bytes = BuildWav( new short[] { 0, 16384, -32768, 32767 } );

        Assert.True( CotiWavReader.TryRead( bytes, out var wav, out var error ), error );
        Assert.Equal( 1, wav.Channels );
        Assert.Equal( 48000, wav.SampleRate );
        Assert.Equal( 4, wav.FrameCount );
        Assert.Equal( 0f, wav.Samples[0] );
        Assert.Equal( 0.5f, wav.Samples[1], 5 );
        Assert.Equal( -1f, wav.Samples[2], 5 );
        Assert.Equal( 0.99997f, wav.Samples[3], 4 );
    }

    [Fact]
    public void KeepsStereoInterleaved()
    {
        var bytes = BuildWav( new short[] { 16384, -16384, 0, 32767 }, channels: 2, rate: 44100 );

        Assert.True( CotiWavReader.TryRead( bytes, out var wav, out var error ), error );
        Assert.Equal( 2, wav.Channels );
        Assert.Equal( 44100, wav.SampleRate );
        Assert.Equal( 2, wav.FrameCount );
        Assert.Equal( -0.5f, wav.Samples[1], 5 );
    }

    [Fact]
    public void SkipsAnUnknownChunkIncludingItsPadByte()
    {
        var bytes = BuildWav( new short[] { 16384 }, oddChunkFirst: true );

        Assert.True( CotiWavReader.TryRead( bytes, out var wav, out var error ), error );
        Assert.Equal( 0.5f, wav.Samples[0], 5 );
    }

    [Fact]
    public void RefusesEightBitSamples()
    {
        Assert.False( CotiWavReader.TryRead( BuildWav( new short[] { 1 }, bits: 8 ), out _, out var error ) );
        Assert.Contains( "16-bit", error );
    }

    [Fact]
    public void RefusesFloatSamples()
    {
        Assert.False( CotiWavReader.TryRead( BuildWav( new short[] { 1 }, format: 3 ), out _, out var error ) );
        Assert.Contains( "PCM", error );
    }

    [Fact]
    public void RefusesMoreThanTwoChannels()
    {
        Assert.False( CotiWavReader.TryRead( BuildWav( new short[] { 1, 2, 3 }, channels: 3 ), out _, out var error ) );
        Assert.Contains( "channel", error );
    }

    [Fact]
    public void RefusesATruncatedDataChunk()
    {
        Assert.False( CotiWavReader.TryRead( BuildWav( new short[] { 1, 2, 3, 4 }, truncateBy: 3 ), out _, out var error ) );
        Assert.Contains( "past the end", error );
    }

    [Fact]
    public void RefusesAnEmptyDataChunk()
    {
        Assert.False( CotiWavReader.TryRead( BuildWav( new short[0] ), out _, out var error ) );
        Assert.Contains( "no samples", error );
    }

    [Fact]
    public void RefusesAFileWithNoDataChunk()
    {
        var bytes = BuildWav( new short[] { 1 } );
        var fmtOnly = bytes[..( bytes.Length - 10 )];

        Assert.False( CotiWavReader.TryRead( fmtOnly, out _, out var error ) );
        Assert.NotNull( error );
    }

    [Fact]
    public void RefusesSomethingThatIsNotAWav()
    {
        Assert.False( CotiWavReader.TryRead( Encoding.ASCII.GetBytes( "ID3 this is an mp3" ), out _, out var error ) );
        Assert.Contains( "RIFF", error );
    }

    [Fact]
    public void RefusesNull()
    {
        Assert.False( CotiWavReader.TryRead( null!, out var wav, out var error ) );
        Assert.Null( wav );
        Assert.NotNull( error );
    }
}
