#if COTI_DEV
using System;
using System.IO;
using UnityEngine;

namespace Coti.Client.Dev
{
  /// <summary>
  /// Writes a render texture to a PNG and reports per-channel statistics.
  ///
  /// The channel MEANS are the point, not the picture: under a grayscale palette a real thermal
  /// render has neutral means and a lit one is colour-cast. Shared by both cameras so their output
  /// is comparable line for line.
  /// </summary>
  internal static class CotiFrameDump
  {
    internal static string Directory => Path.Combine( BepInEx.Paths.GameRootPath, "coti-dumps" );

    /// <summary>
    /// Writes <paramref name="source"/> to <c>coti-dumps/{name}-{index:d3}.png</c> and returns a
    /// one-line summary, or null when it could not.
    /// </summary>
    internal static string Dump( RenderTexture source, string name, int index )
    {
      if( source == null )
      {
        Plugin.Log.LogWarning( $"[COTI] dump \"{name}\" skipped - no render texture" );
        return null;
      }

      Texture2D readback = null;

      // RenderTexture.active is global, so leaving it pointing elsewhere surfaces as an unrelated
      // fault later.
      var previous = RenderTexture.active;

      try
      {
        readback = new Texture2D( source.width, source.height, TextureFormat.RGBA32, false );

        RenderTexture.active = source;
        readback.ReadPixels( new Rect( 0f, 0f, source.width, source.height ), 0, 0 );
        readback.Apply( false, false );
        RenderTexture.active = previous;

        var pixels = readback.GetPixels32();
        double sumR = 0, sumG = 0, sumB = 0;
        int maxR = 0, maxG = 0, maxB = 0, nonBlack = 0;

        for( var i = 0; i < pixels.Length; i++ )
        {
          var p = pixels[i];
          sumR += p.r;
          sumG += p.g;
          sumB += p.b;
          if( p.r > maxR ) maxR = p.r;
          if( p.g > maxG ) maxG = p.g;
          if( p.b > maxB ) maxB = p.b;
          if( p.r > 8 || p.g > 8 || p.b > 8 ) nonBlack++;
        }

        var count = Mathf.Max( 1, pixels.Length );

        System.IO.Directory.CreateDirectory( Directory );
        var path = Path.Combine( Directory, $"{name}-{index:d3}.png" );
        File.WriteAllBytes( path, readback.EncodeToPNG() );

        return $"{path} ({source.width}x{source.height}) " +
               $"mean R={sumR / count:F1} G={sumG / count:F1} B={sumB / count:F1} " +
               $"max R={maxR} G={maxG} B={maxB} nonBlack={100.0 * nonBlack / count:F1}%";
      }
      catch( Exception ex )
      {
        RenderTexture.active = previous;
        Plugin.Log.LogError( $"[COTI] dump \"{name}\" failed: {ex}" );
        return null;
      }
      finally
      {
        if( readback != null )
          UnityEngine.Object.Destroy( readback );
      }
    }

    /// <summary>
    /// Bumped by <see cref="RequestBatch"/>. Every countdown compares against it, so one keypress
    /// arms every camera at once.
    /// </summary>
    private static int _generation;
    private static int _manualCount;

    /// <summary>
    /// Arms a batch from a keypress rather than from config, because opening the F12 panel moves the
    /// camera away from the view being dumped.
    /// </summary>
    internal static void RequestBatch( int count )
    {
      _manualCount = count;
      _generation++;
    }

    /// <summary>
    /// Counts a batch down against the config value and the manual generation, so either can arm one
    /// and neither dumps forever.
    /// </summary>
    internal struct Countdown
    {
      private int _remaining;
      private int _lastRequest;
      private int _index;
      private int _lastGeneration;

      /// <summary>
      /// True at most <c>requested</c> times per change of <c>requested</c>. The index does not reset
      /// per batch: it names the file, so resetting would have each batch overwrite the last.
      /// </summary>
      internal bool Take( int requested, out int index )
      {
        if( _lastGeneration != _generation )
        {
          _lastGeneration = _generation;
          _remaining = _manualCount;
        }
        else if( requested != _lastRequest )
        {
          _remaining = requested;
          _lastRequest = requested;
        }

        index = _index;

        if( _remaining <= 0 )
          return false;

        _remaining--;
        _index++;
        return true;
      }

      internal void Stop()
      {
        _remaining = 0;
      }
    }
  }
}
#endif
