using System;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The riser's sight text written with Nunito Bold at its exact size on screen: glyphs
  /// rasterised by Unity's dynamic font at that pixel size and drawn one texel per screen pixel, so they are never scaled.
  ///
  /// The font is a dynamic Font asset in the coti_shaders bundle (OFL, licence in fonts/OFL.txt beside the DLL). A loose
  /// TTF opened with new Font(path) gets no material or texture on this Unity, so it could not be drawn. Drawn with the
  /// font's own material, the engine's built-in text shader. The glow is a ring of faint copies under the letters.
  /// Without the font the riser's sight shows no text: one warning, then nothing drawn.
  /// </summary>
  internal static class CotiSightFont
  {
    /// <summary>Each of the eight glow copies adds this much white where it covers; the letters are drawn over them.</summary>
    private const float GlowAlpha = 0.006f;

    /// <summary>The glow ring's radius as a share of the em, at least a pixel.</summary>
    private const float GlowRadiusShare = 0.06f;

    private static readonly Vector2[] GlowDirections =
    {
      new Vector2( 1f, 0f ), new Vector2( -1f, 0f ), new Vector2( 0f, 1f ), new Vector2( 0f, -1f ),
      new Vector2( 0.7071f, 0.7071f ), new Vector2( -0.7071f, 0.7071f ), new Vector2( 0.7071f, -0.7071f ), new Vector2( -0.7071f, -0.7071f ),
    };

    private static Font _font;
    private static bool _failed;

    private static bool Ensure()
    {
      if( _font != null )
        return true;
      if( _failed )
        return false;

      try
      {
        // The bundle keeps the font and its material across the raid's scene load.
        var font = CotiShaderBundle.SightFont;
        if( font == null )
          throw new InvalidOperationException( "the shader bundle has no font" );
        if( font.material == null )
          throw new InvalidOperationException( "the font has no material" );

        _font = font;
        return true;
      }
      catch( Exception ex )
      {
        _failed = true;
        _font = null;
        Plugin.Log.LogWarning( $"[COTI] Sight text font unavailable, the riser's sight shows no text: {ex.Message}" );
        return false;
      }
    }

    /// <summary>
    /// <paramref name="words"/> at <paramref name="em"/> pixels, centred on <paramref name="centre"/> (top-left origin),
    /// into the active target, whose pixel matrix the caller has loaded. Whole pixels throughout.
    /// </summary>
    internal static void Draw( string words, float em, Vector2 centre )
    {
      if( !Ensure() )
        return;

      var size = Mathf.Max( 1, Mathf.RoundToInt( em ) );
      // Asked for every draw: a rebuild of the font's texture invalidates glyphs asked for earlier.
      _font.RequestCharactersInTexture( words, size, FontStyle.Normal );

      int width = 0, top = int.MinValue, bottom = int.MaxValue;
      foreach( var c in words )
      {
        if( !_font.GetCharacterInfo( c, out var glyph, size, FontStyle.Normal ) )
          continue;

        width += glyph.advance;
        top = Math.Max( top, glyph.maxY );
        bottom = Math.Min( bottom, glyph.minY );
      }

      if( width == 0 || top < bottom )
        return;

      // The pen's start and the baseline, so the letters' box sits centred on the glass.
      var x = Mathf.Round( centre.x - width / 2f );
      var baseline = Mathf.Round( centre.y + ( top + bottom ) / 2f );
      var glow = Mathf.Max( 1f, Mathf.Round( em * GlowRadiusShare ) );

      _font.material.SetPass( 0 );
      GL.Begin( GL.QUADS );
      GL.Color( new Color( 1f, 1f, 1f, GlowAlpha ) );
      foreach( var direction in GlowDirections )
        Quads( words, size, x + Mathf.Round( direction.x * glow ), baseline + Mathf.Round( direction.y * glow ) );
      GL.Color( Color.white );
      Quads( words, size, x, baseline );
      GL.End();
    }

    private static void Quads( string words, int size, float x, float baseline )
    {
      foreach( var c in words )
      {
        if( !_font.GetCharacterInfo( c, out var glyph, size, FontStyle.Normal ) )
          continue;

        // Glyph metrics are up from the baseline; the target's y runs down.
        float left = x + glyph.minX, right = x + glyph.maxX;
        float top = baseline - glyph.maxY, bottom = baseline - glyph.minY;
        GL.TexCoord( glyph.uvTopLeft );
        GL.Vertex3( left, top, 0f );
        GL.TexCoord( glyph.uvTopRight );
        GL.Vertex3( right, top, 0f );
        GL.TexCoord( glyph.uvBottomRight );
        GL.Vertex3( right, bottom, 0f );
        GL.TexCoord( glyph.uvBottomLeft );
        GL.Vertex3( left, bottom, 0f );
        x += glyph.advance;
      }
    }
  }
}
