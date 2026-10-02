namespace Coti.Shared
{
  /// <summary>
  /// Places a message image inside the COTI's circle, in the same UV space as the generated mask:
  /// x across the width, y up from the bottom, radius as a fraction of screen height.
  /// </summary>
  public static class CotiDisplayLayout
  {
    /// <summary>
    /// The message's width as a share of the circle's diameter, measured off reference footage.
    /// </summary>
    public const float TextWidthOfDiameter = 0.31f;

    /// <summary>
    /// The scale and offset for Graphics.Blit(image, target, scale, offset) that draws the image
    /// centred on the circle. False when the geometry cannot place it.
    /// </summary>
    public static bool TryBlitTransform(
        float centerX, float centerY, float radius, float screenAspect, float imageAspect,
        out float scaleX, out float scaleY, out float offsetX, out float offsetY )
    {
      scaleX = scaleY = offsetX = offsetY = 0f;

      if( !( radius > 0f ) || !( screenAspect > 0f ) || !( imageAspect > 0f ) )
        return false;

      var widthInScreenHeights = TextWidthOfDiameter * 2f * radius;
      var width = widthInScreenHeights / screenAspect;
      var height = widthInScreenHeights / imageAspect;

      scaleX = 1f / width;
      scaleY = 1f / height;
      offsetX = -( centerX - width / 2f ) * scaleX;
      offsetY = -( centerY - height / 2f ) * scaleY;
      return true;
    }
  }
}
