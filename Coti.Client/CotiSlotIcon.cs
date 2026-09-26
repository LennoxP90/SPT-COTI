using System.IO;
using System.Reflection;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  public static class CotiSlotIcon
  {
    /// <summary>
    /// Derived from CotiIds.ModSlotName: the game keys slot icons by slot name, which is also the
    /// transform the mount patch creates. If the two drifted, the slot would silently show no icon.
    /// </summary>
    private const string CacheKey = "Slots/" + CotiIds.ModSlotName;

    /// <summary>
    /// A 1x1 slot, matching ItemViewFactory.GetCellPixelSize(1,1) = 1 * 63 + 1. The COTI occupies
    /// exactly one cell, so this is a constant rather than something to derive.
    /// </summary>
    private const int SlotPixelSize = 64;
    private const string ResourceName = "Coti.Client.Assets.slot_mod_coti.png";

    private static Sprite _sprite;

    public static void Install()
    {
      if( _sprite != null )
        return;

      var bytes = ReadEmbedded();
      if( bytes == null )
        return;

      // Size is irrelevant here - LoadImage replaces the texture's dimensions with the PNG's.
      var texture = new Texture2D( 2, 2, TextureFormat.RGBA32, mipChain: false )
      {
        // The icon is drawn at roughly its native size in a small slot; clamping avoids the
        // edge bleed that wrapping would give a sprite with transparent margins.
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
      };

      if( !texture.LoadImage( bytes ) )
      {
        Plugin.Log.LogWarning( "[COTI] Slot icon PNG could not be decoded - the empty slot will have no background" );
        Object.Destroy( texture );
        return;
      }

      // Resampled to the cell size: ModSlotView draws this background at the sprite's native size,
      // so a larger image would overflow the slot.
      Object.DontDestroyOnLoad( texture );
      _sprite = CotiSpriteScaler.Get( "slot", texture, SlotPixelSize, SlotPixelSize );

      if( _sprite == null )
      {
        Plugin.Log.LogWarning( "[COTI] Slot icon could not be resampled - the empty slot will have no background" );
        return;
      }

      EftCompat.CacheSprite( CacheKey, _sprite );

      Plugin.Log.LogInfo( $"[COTI] Slot icon installed ({SlotPixelSize}x{SlotPixelSize} from a {texture.width}px master)" );
    }

    private static byte[] ReadEmbedded()
    {
      var assembly = Assembly.GetExecutingAssembly();

      using( var stream = assembly.GetManifestResourceStream( ResourceName ) )
      {
        if( stream == null )
        {
          Plugin.Log.LogWarning(
              $"[COTI] Embedded resource '{ResourceName}' is missing - available: " +
              string.Join( ", ", assembly.GetManifestResourceNames() ) );
          return null;
        }

        using( var memory = new MemoryStream() )
        {
          stream.CopyTo( memory );
          return memory.ToArray();
        }
      }
    }
  }
}
