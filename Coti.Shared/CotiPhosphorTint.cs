namespace Coti.Shared
{
  /// <summary>
  /// Turns a tube's phosphor colour into the two tints the overlay shader paints heat with.
  /// Plain floats, not UnityEngine.Color, so the tests compile against it.
  /// </summary>
  public static class CotiPhosphorTint
  {
    /// <summary>
    /// Below this a colour counts as unset. Tinting to it would paint the heat black.
    /// </summary>
    public const float MinimumBrightness = 0.01f;

    /// <summary>
    /// How far the hot end is pulled toward white. It is BRIGHTNESS that reads as heat, so a green
    /// blob on a green image would not register as one.
    /// </summary>
    public const float HotWhiteMix = 0.7f;

    /// <summary>
    /// The phosphor's hue with brightness divided out, so a dim tube does not also dim the heat.
    /// False when it is too dark to carry a hue - leave the shader's defaults alone, do not
    /// substitute.
    /// </summary>
    public static bool TryHue(
        float red, float green, float blue,
        out float hueRed, out float hueGreen, out float hueBlue )
    {
      hueRed = 0f;
      hueGreen = 0f;
      hueBlue = 0f;

      if( red + green + blue <= MinimumBrightness )
        return false;

      var peak = red;
      if( green > peak ) peak = green;
      if( blue > peak ) peak = blue;

      // Reachable on its own: a sum above the floor can still be spread across three channels that
      // are each below it, and dividing by a peak that small would blow the hue past 1.
      if( peak <= MinimumBrightness )
        return false;

      hueRed = red / peak;
      hueGreen = green / peak;
      hueBlue = blue / peak;
      return true;
    }

    /// <summary>
    /// How far through its flash the tube is: current channel sum over settled channel sum.
    ///
    /// The clamp is load-bearing. EFT drives CurrentColor as 1 - 2 * value with value running past
    /// 0.5, so it swings NEGATIVE mid-flash - measured at -1.628 against a settled 1.596.
    /// </summary>
    public static float Fade( float currentSum, float configuredSum )
    {
      // A configured colour of zero makes the ratio meaningless. Treat the tube as settled rather
      // than dividing by it and hiding the overlay for the rest of the raid.
      if( configuredSum <= 0.001f )
        return 1f;

      var ratio = currentSum / configuredSum;

      if( ratio < 0f )
        return 0f;
      if( ratio > 1f )
        return 1f;

      return ratio;
    }

    /// <summary>
    /// The hot end, <see cref="HotWhiteMix"/> of the way from the hue to white.
    /// </summary>
    public static void Hot(
        float hueRed, float hueGreen, float hueBlue,
        out float hotRed, out float hotGreen, out float hotBlue )
    {
      hotRed = hueRed + ( 1f - hueRed ) * HotWhiteMix;
      hotGreen = hueGreen + ( 1f - hueGreen ) * HotWhiteMix;
      hotBlue = hueBlue + ( 1f - hueBlue ) * HotWhiteMix;
    }
  }
}
