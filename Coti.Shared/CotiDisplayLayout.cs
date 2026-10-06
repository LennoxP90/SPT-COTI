using System;

namespace Coti.Shared
{
  /// <summary>
  /// A message copy's rectangle on screen, in viewport units: x across the width, y up from the bottom.
  /// </summary>
  public readonly struct CotiTextRect
  {
    public CotiTextRect( float x, float y, float width, float height )
    {
      X = x;
      Y = y;
      Width = width;
      Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
  }

  /// <summary>
  /// Where a copy sits in its circle: which side of it is anchored, that edge's distance from the circle's centre in
  /// radii (for Center, the copy's middle's offset, positive right), and a vertical offset in radii, positive up.
  /// </summary>
  public readonly struct CotiTextPlacement : IEquatable<CotiTextPlacement>
  {
    public CotiTextPlacement( CotiTubeSide align, float edge, float y )
    {
      Align = align;
      Edge = edge;
      Y = y;
    }

    public CotiTubeSide Align { get; }
    public float Edge { get; }
    public float Y { get; }

    public bool Equals( CotiTextPlacement other ) => Align == other.Align && Edge == other.Edge && Y == other.Y;
    public override bool Equals( object? obj ) => obj is CotiTextPlacement other && Equals( other );
    public override int GetHashCode() => ( (int)Align * 397 ^ Edge.GetHashCode() ) * 397 ^ Y.GetHashCode();
  }

  /// <summary>
  /// Places one copy of a message image in an open circle. By the rule a left tube's copy grows inward from an outer
  /// edge left of the circle's centre, a right tube's from one right of it, and a centre tube's sits centred, so copies
  /// on neighbouring tubes never touch. A device file's text block moves it from there.
  /// </summary>
  public static class CotiDisplayLayout
  {
    /// <summary>
    /// One image texel in screen heights, per unit of circle radius: a 631 px wide image spans 0.31 of
    /// the circle's diameter, the size measured off reference footage.
    /// </summary>
    public const float ScreenHeightsPerTexel = 0.62f / 631f;

    /// <summary>Where a left or right copy's outer edge sits, as a share of the radius from the centre.</summary>
    public const float OuterEdge = 0.6f;

    /// <summary>The closest any edge of a copy may come to the screen's edge, in screen heights.</summary>
    public const float ScreenMargin = 0.02f;

    /// <summary>
    /// A tube's placement: each field the block sets, else the rule's (align from the tube's side, the edge for that
    /// align, no vertical offset). Null label is a file without tubes, which centres.
    /// </summary>
    public static CotiTextPlacement Resolve( CotiTextBlock? block, string? label )
    {
      if( block?.Align == null || !CotiTextBlock.TryParseAlign( block.Align, out var align ) )
        align = CotiTubes.SideOf( label );

      return new CotiTextPlacement( align, block?.Edge ?? DefaultEdge( align ), block?.Y ?? 0f );
    }

    public static float DefaultEdge( CotiTubeSide align ) => align == CotiTubeSide.Center ? 0f : OuterEdge;

    /// <summary>
    /// The block as a device file should carry it: only the fields that differ from the rule, or null when none does.
    /// Places exactly as the block does.
    /// </summary>
    public static CotiTextBlock? Trim( CotiTextBlock? block, string? label )
    {
      var placement = Resolve( block, label );
      var rule = Resolve( null, label );

      var trimmed = new CotiTextBlock
      {
        Align = placement.Align == rule.Align ? null : CotiTextBlock.AlignName( placement.Align ),
        Edge = placement.Edge == DefaultEdge( placement.Align ) ? (float?)null : placement.Edge,
        Y = placement.Y == 0f ? (float?)null : placement.Y,
      };

      return trimmed.Align == null && trimmed.Edge == null && trimmed.Y == null ? null : trimmed;
    }

    /// <summary>
    /// The copy's rectangle for a circle and placement, held ScreenMargin inside the screen. False when the geometry
    /// cannot place it. The mount editor's textRect and placementOf (Coti.Server.Web/wwwroot/js/cotiViewer.js) repeat
    /// these steps for its drag; keep them in step.
    /// </summary>
    public static bool TryRect( CotiCircle circle, CotiTextPlacement placement, float screenAspect,
                                int imageWidth, int imageHeight, out CotiTextRect rect )
    {
      rect = default;

      if( !( circle.Radius > 0f ) || !( screenAspect > 0f ) || imageWidth <= 0 || imageHeight <= 0 )
        return false;

      // Screen heights from the left edge, back to viewport units at the end.
      var texel = ScreenHeightsPerTexel * circle.Radius;
      var width = imageWidth * texel;
      var height = imageHeight * texel;
      var centreX = circle.U * screenAspect;

      float left;
      switch( placement.Align )
      {
        case CotiTubeSide.Left:
          left = centreX - placement.Edge * circle.Radius;
          break;
        case CotiTubeSide.Right:
          left = centreX + placement.Edge * circle.Radius - width;
          break;
        default:
          left = centreX + placement.Edge * circle.Radius - width / 2f;
          break;
      }

      left = Math.Min( Math.Max( left, ScreenMargin ), screenAspect - ScreenMargin - width );
      var bottom = Math.Min( Math.Max( circle.V - height / 2f + placement.Y * circle.Radius, ScreenMargin ),
          1f - ScreenMargin - height );

      rect = new CotiTextRect( left / screenAspect, bottom, width / screenAspect, height );
      return true;
    }
  }
}
