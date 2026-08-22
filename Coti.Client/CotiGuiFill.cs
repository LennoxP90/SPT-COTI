using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Flat opaque rectangles, because the game's GUI skin draws a window background that is
  /// effectively transparent and IMGUI has no switch for it. One tinted 1x1 texture.
  /// </summary>
  public static class CotiGuiFill
  {
    private static Texture2D _pixel;

    /// <summary>
    /// The window body colour. Deliberately near-opaque rather than fully so: a sliver of the
    /// scene behind still reads as "this is an overlay", which matters when the window covers most
    /// of the inspect screen.
    /// </summary>
    public static readonly Color WindowBody = new Color( 0.11f, 0.11f, 0.12f, 0.97f );

    public static void Rect( Rect area, Color colour )
    {
      if( _pixel == null )
      {
        _pixel = new Texture2D( 1, 1, TextureFormat.ARGB32, mipChain: false );
        _pixel.SetPixel( 0, 0, Color.white );
        _pixel.Apply();
        _pixel.hideFlags = HideFlags.HideAndDontSave;
      }

      var previous = GUI.color;
      GUI.color = colour;
      GUI.DrawTexture( area, _pixel );
      GUI.color = previous;
    }

    /// <summary>
    /// Paints the window body and re-draws its title.
    ///
    /// The title has to be redrawn because GUI.Window renders its own before invoking the callback,
    /// so anything the callback paints over the full rect covers it. Painting only below the title
    /// bar instead would leave a transparent strip across the top, which is the same complaint in
    /// a thinner shape.
    /// </summary>
    public static void Window( float width, float height, string title )
    {
      Rect( new Rect( 0f, 0f, width, height ), WindowBody );

      // Centred by measuring rather than by setting an alignment: TextAnchor and FontStyle live
      // in UnityEngine.TextRenderingModule, which this project does not reference, and adding a
      // Unity module reference for a centred label is not worth the resolution risk.
      var size = GUI.skin.label.CalcSize( new GUIContent( title ) );
      GUI.Label( new Rect( ( width - size.x ) * 0.5f, 1f, size.x, 18f ), title );
    }
  }
}
