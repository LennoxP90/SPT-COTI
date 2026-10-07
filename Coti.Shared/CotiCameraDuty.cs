namespace Coti.Shared
{
  /// <summary>
  /// Which thermal camera draws this frame. Never both: a thermal sight is its own picture, and behind a magnified sight
  /// the magnified camera's picture replaces the 1x one, which the 1x overlay then does not draw.
  /// </summary>
  public static class CotiCameraDuty
  {
    public static bool OneX( bool active, bool thermalSightAimed, bool magnifiedSightAimed )
    {
      return active && !thermalSightAimed && !magnifiedSightAimed;
    }

    public static bool Magnified( bool active, bool thermalSightAimed, bool magnifiedSightAimed )
    {
      return active && !thermalSightAimed && magnifiedSightAimed;
    }
  }
}
