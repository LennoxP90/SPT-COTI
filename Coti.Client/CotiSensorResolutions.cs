namespace Coti.Client
{
  /// <summary>
  /// The sensor resolutions offered on F12 and cycled by the dev key, in one place so the two cannot
  /// drift apart.
  ///
  /// Values below 576 are a test aid: angular size in texels is <c>(size / range) x (rows / fov)</c>,
  /// so halving the rows is identical to doubling the range.
  /// </summary>
  public static class CotiSensorResolutions
  {
    public static readonly int[] All = { 288, 384, 576, 768, 1152, 1536 };

    /// <summary>
    /// The sensor's 4:3 ratio, so one control cannot leave width and height inconsistent.
    /// </summary>
    public static int WidthFor( int rows )
    {
      return rows * 4 / 3;
    }

    /// <summary>
    /// The next resolution up, wrapping at the top. A value not on the list snaps to the nearest one
    /// above it, since config can hold anything and a key that refuses to move is worse.
    /// </summary>
    public static int Next( int current )
    {
      for( var i = 0; i < All.Length; i++ )
      {
        if( All[i] > current )
          return All[i];
      }

      return All[0];
    }
  }
}
