using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Generates the mask at the compositing camera's resolution, with the circle defined in pixel
  /// space so it is a true circle on any aspect ratio.
  /// </summary>
  public static class MaskGenerator
  {
    private static Texture2D _mask;
    private static int _generatedWidth = -1;
    private static int _generatedHeight = -1;
    private static string _generatedHostKey;

    private static float _generatedCenterX;
    private static float _generatedCenterY;
    private static float _generatedRadius;
    private static float _generatedFeather;

    private static bool _loggedDegenerateRadius;

    /// <summary>
    /// Returns the mask for this host, regenerating only when the resolution, host or mask
    /// geometry changes, since building a multi-megapixel texture is expensive.
    ///
    /// Returns null, logging once, when the host's maskRadius is zero or negative: that is a
    /// misconfiguration. The caller (CotiState.Update) treats null as "COTI inactive this frame".
    /// </summary>
    /// <param name="width">
    /// Target width in pixels. This must be the compositing camera's pixelWidth, not Screen.width:
    /// EFT renders at a scaled resolution, and a mask generated at screen size and composited onto
    /// the camera's smaller target draws a second, scaled copy of the circle.
    /// </param>
    /// <param name="height">Target height in pixels; the camera's pixelHeight, same reasoning.</param>
    public static Texture2D GetOrCreate(
        string hostTemplateId, CotiNvgHostConfig host, string maskLabel, int width, int height )
    {
      if( width <= 0 || height <= 0 )
        return null;

      if( host.MaskRadius <= 0f )
      {
        if( !_loggedDegenerateRadius )
        {
          Plugin.Log.LogError(
              $"[COTI] Host '{maskLabel}' has a non-positive maskRadius ({host.MaskRadius}) - " +
              "COTI inactive rather than generating a degenerate mask" );
          _loggedDegenerateRadius = true;
        }
        return null;
      }

      if( _mask != null
          && width == _generatedWidth
          && height == _generatedHeight
          && hostTemplateId == _generatedHostKey
          && host.MaskCenterX == _generatedCenterX
          && host.MaskCenterY == _generatedCenterY
          && host.MaskRadius == _generatedRadius
          && host.MaskFeather == _generatedFeather )
      {
        return _mask;
      }

      Release();

      _mask = Build( host, width, height );
      _generatedWidth = width;
      _generatedHeight = height;
      _generatedHostKey = hostTemplateId;
      _generatedCenterX = host.MaskCenterX;
      _generatedCenterY = host.MaskCenterY;
      _generatedRadius = host.MaskRadius;
      _generatedFeather = host.MaskFeather;

      Plugin.Log.LogInfo( $"[COTI] Generated mask '{maskLabel}' at {width}x{height}" );
      return _mask;
    }

    private static Texture2D Build( CotiNvgHostConfig host, int width, int height )
    {
      var tex = new Texture2D( width, height, TextureFormat.ARGB32, false )
      {
        wrapMode = TextureWrapMode.Clamp,
        name = "CotiGeneratedMask",
      };

      var centerX = host.MaskCenterX * width;
      var centerY = host.MaskCenterY * height;
      var radius = host.MaskRadius * height;
      var feather = host.MaskFeather * height;

      var pixels = new Color32[width * height];

      for( var y = 0; y < height; y++ )
      {
        var dy = ( y + 0.5f ) - centerY;
        var rowOffset = y * width;

        for( var x = 0; x < width; x++ )
        {
          var dx = ( x + 0.5f ) - centerX;
          var distance = Mathf.Sqrt( dx * dx + dy * dy );

          var coverage = MaskGeometry.ComputeCoverage( distance, radius, feather );

          // Identical value in every channel: whichever channel Custom/MaskShader's
          // _OverlayTex sampling reads (.r, .a, or luminance) sees the same mask.
          var value = (byte)Mathf.Clamp( Mathf.RoundToInt( coverage * 255f ), 0, 255 );
          pixels[rowOffset + x] = new Color32( value, value, value, value );
        }
      }

      tex.SetPixels32( pixels );
      tex.Apply( false, false );
      return tex;
    }

    public static void Release()
    {
      if( _mask == null )
        return;

      Object.Destroy( _mask );
      _mask = null;
      _generatedWidth = -1;
      _generatedHeight = -1;
      _generatedHostKey = null;
    }
  }
}
