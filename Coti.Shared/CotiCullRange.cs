using System;

namespace Coti.Shared
{
  /// <summary>
  /// Per-layer cull distances for a thermal camera. EFT sets the eye's far clip to 10 km and culls every layer at the
  /// player's Overall Visibility (400 to 3000 m) through <c>Camera.layerCullDistances</c>; a thermal camera that only
  /// copied the far clip drew out to 10 km. These are the eye's own distances, capped at the thermal's range when one
  /// is set. Unity reads 0 as "the far clip plane", which is kept unless a range caps it.
  /// </summary>
  public static class CotiCullRange
  {
    public static float[] Distances( float[] eye, float farClip, float range )
    {
      if( eye == null )
        return Array.Empty<float>();

      var result = new float[eye.Length];
      for( var i = 0; i < eye.Length; i++ )
      {
        var distance = eye[i];
        if( range > 0f )
        {
          var effective = distance > 0f ? distance : farClip;
          distance = effective < range ? effective : range;
        }
        result[i] = distance;
      }
      return result;
    }
  }
}
