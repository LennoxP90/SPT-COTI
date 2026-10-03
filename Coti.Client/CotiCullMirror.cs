using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Keeps a thermal camera's per-layer cull distances matched to the camera it looks through (see
  /// <see cref="CotiCullRange"/>), capped at the thermal's range. Re-read every half second rather than every frame:
  /// reading <c>layerCullDistances</c> allocates, and EFT only changes them with the graphics settings.
  /// </summary>
  internal sealed class CotiCullMirror
  {
    private const int RefreshFrames = 30;

    private int _frame = int.MinValue;
    private float _range = float.NaN;
    private float _farClip = float.NaN;

    internal void Apply( Camera thermal, Camera eye, float range )
    {
      var frame = Time.frameCount;
      if( frame - _frame < RefreshFrames && range == _range && eye.farClipPlane == _farClip )
        return;

      thermal.layerCullSpherical = eye.layerCullSpherical;
      thermal.layerCullDistances = CotiCullRange.Distances( eye.layerCullDistances, eye.farClipPlane, range );

      _frame = frame;
      _range = range;
      _farClip = eye.farClipPlane;
    }

    /// <summary>Applies on the next call, for a new camera.</summary>
    internal void Reset()
    {
      _frame = int.MinValue;
      _range = float.NaN;
    }
  }
}
