namespace Coti.Client
{
  public static class MaskGeometry
  {
    public static float ComputeCoverage( float distance, float radius, float feather )
    {
      if( feather <= 0f )
      {
        return distance <= radius ? 1f : 0f;
      }

      var inner = radius - feather * 0.5f;
      var outer = radius + feather * 0.5f;

      if( distance <= inner )
        return 1f;
      if( distance >= outer )
        return 0f;

      return ( outer - distance ) / ( outer - inner );
    }
  }
}
