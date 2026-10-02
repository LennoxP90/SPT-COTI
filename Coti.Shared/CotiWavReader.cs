using System.Text;

namespace Coti.Shared
{
  public sealed class CotiWav
  {
    public int Channels { get; set; }
    public int SampleRate { get; set; }

    /// <summary>
    /// Interleaved, scaled to -1..1, in the layout AudioClip.SetData expects.
    /// </summary>
    public float[] Samples { get; set; } = new float[0];

    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
  }

  /// <summary>
  /// Reads 16-bit PCM RIFF/WAVE, mono or stereo. Anything else is refused with a reason.
  /// </summary>
  public static class CotiWavReader
  {
    private const int PcmFormat = 1;

    public static bool TryRead( byte[] data, out CotiWav wav, out string? error )
    {
      wav = null!;

      if( data == null || data.Length < 12 || !HasTag( data, 0, "RIFF" ) || !HasTag( data, 8, "WAVE" ) )
      {
        error = "not a RIFF/WAVE file";
        return false;
      }

      var format = 0;
      var channels = 0;
      var rate = 0;
      var bits = 0;
      var haveFormat = false;
      var position = 12;

      while( position + 8 <= data.Length )
      {
        var id = Encoding.ASCII.GetString( data, position, 4 );
        var size = ReadInt32( data, position + 4 );
        var body = position + 8;

        if( size < 0 || body + size > data.Length )
        {
          error = $"chunk '{id}' runs past the end of the file";
          return false;
        }

        if( id == "fmt " )
        {
          if( size < 16 )
          {
            error = "fmt chunk is too short";
            return false;
          }

          format = ReadUInt16( data, body );
          channels = ReadUInt16( data, body + 2 );
          rate = ReadInt32( data, body + 4 );
          bits = ReadUInt16( data, body + 14 );
          haveFormat = true;
        }
        else if( id == "data" )
        {
          return TryDecode( data, body, size, haveFormat, format, channels, rate, bits, out wav, out error );
        }

        position = body + size + ( size & 1 );
      }

      error = "no data chunk";
      return false;
    }

    private static bool TryDecode(
        byte[] data, int start, int size, bool haveFormat, int format, int channels, int rate, int bits,
        out CotiWav wav, out string? error )
    {
      wav = null!;
      error = Validate( haveFormat, format, channels, rate, bits );
      if( error != null )
        return false;

      var count = size / 2;
      if( count == 0 || count % channels != 0 )
      {
        error = "no samples, or a partial frame";
        return false;
      }

      var samples = new float[count];
      for( var i = 0; i < count; i++ )
      {
        var offset = start + i * 2;
        samples[i] = (short)( data[offset] | ( data[offset + 1] << 8 ) ) / 32768f;
      }

      wav = new CotiWav { Channels = channels, SampleRate = rate, Samples = samples };
      return true;
    }

    private static string? Validate( bool haveFormat, int format, int channels, int rate, int bits )
    {
      if( !haveFormat )
        return "data chunk comes before the fmt chunk";
      if( format != PcmFormat )
        return $"format {format} is not PCM";
      if( bits != 16 )
        return $"{bits}-bit samples; only 16-bit is supported";
      if( channels != 1 && channels != 2 )
        return $"{channels} channels; only mono and stereo are supported";
      if( rate <= 0 )
        return $"sample rate {rate} is not valid";
      return null;
    }

    private static bool HasTag( byte[] data, int offset, string tag )
    {
      return Encoding.ASCII.GetString( data, offset, 4 ) == tag;
    }

    private static int ReadUInt16( byte[] data, int offset )
    {
      return data[offset] | ( data[offset + 1] << 8 );
    }

    private static int ReadInt32( byte[] data, int offset )
    {
      return data[offset] | ( data[offset + 1] << 8 ) | ( data[offset + 2] << 16 ) | ( data[offset + 3] << 24 );
    }
  }
}
