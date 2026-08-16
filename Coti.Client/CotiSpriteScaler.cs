using System.Collections.Generic;
using UnityEngine;

namespace Coti.Client
{
  public static class CotiSpriteScaler
  {
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public static Sprite Get( string key, Texture2D master, int width, int height )
    {
      if( master == null || width <= 0 || height <= 0 )
        return null;

      var cacheKey = key + ":" + width + "x" + height;

      if( Cache.TryGetValue( cacheKey, out var cached ) && cached != null )
        return cached;

      var scaled = Resample( master, width, height );
      var sprite = Sprite.Create( scaled, new Rect( 0f, 0f, width, height ), new Vector2( 0.5f, 0.5f ) );
      sprite.name = cacheKey;

      // Both must survive scene loads, and the sprite alone does not keep its texture alive.
      Object.DontDestroyOnLoad( scaled );
      Object.DontDestroyOnLoad( sprite );

      Cache[cacheKey] = sprite;
      return sprite;
    }

    private static Texture2D Resample( Texture2D master, int width, int height )
    {
      // sRGB: these are colour images, not data. Getting this wrong washes the icon out.
      var temporary = RenderTexture.GetTemporary(
          width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB );

      var previousFilter = master.filterMode;
      master.filterMode = FilterMode.Bilinear;

      var previousActive = RenderTexture.active;

      try
      {
        Graphics.Blit( master, temporary );
        RenderTexture.active = temporary;

        var result = new Texture2D( width, height, TextureFormat.RGBA32, mipChain: false )
        {
          wrapMode = TextureWrapMode.Clamp,
          filterMode = FilterMode.Bilinear,
        };

        result.ReadPixels( new Rect( 0f, 0f, width, height ), 0, 0 );
        result.Apply( updateMipmaps: false );
        return result;
      }
      finally
      {
        // Restore rather than null: something else may have had a target bound, and leaving
        // RenderTexture.active changed is the kind of thing that breaks an unrelated system.
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary( temporary );
        master.filterMode = previousFilter;
      }
    }
  }
}
