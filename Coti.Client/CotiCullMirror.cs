using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Keeps a thermal camera's per-layer cull distances matched to the camera it looks through (see
  /// <see cref="CotiCullRange"/>), capped at the thermal's range. The eye's distances are read every half second, on a
  /// far-clip change or for a new eye: the getter allocates, and EFT only changes them with the graphics settings. A
  /// range change (a variable scope's zoom moves it every frame) recomputes from that copy into one reused buffer.
  /// </summary>
  internal sealed class CotiCullMirror
  {
    private const int RefreshFrames = 30;

    // Unity keeps 32 layers, and the setter copies, so one buffer serves every write.
    private readonly float[] _distances = new float[32];
    private Camera _eye;
    private float[] _eyeDistances;
    private int _frame;
    private float _range = float.NaN;
    private float _farClip = float.NaN;

    internal void Apply( Camera thermal, Camera eye, float range )
    {
      var frame = Time.frameCount;
      var farClip = eye.farClipPlane;
      // !=, not ReferenceEquals: on il2cpp a camera read twice can come back as two wrappers of one native object.
      var reread = _eyeDistances == null || eye != _eye
                   || frame - _frame >= RefreshFrames || farClip != _farClip;
      if( !reread && range == _range )
        return;

      if( reread )
      {
        _eye = eye;
        _eyeDistances = eye.layerCullDistances;
        thermal.layerCullSpherical = eye.layerCullSpherical;
        _frame = frame;
        _farClip = farClip;
      }

      CotiCullRange.Distances( _eyeDistances, farClip, range, _distances );
      thermal.layerCullDistances = _distances;
      _range = range;
    }

    /// <summary>Applies on the next call, for a new camera.</summary>
    internal void Reset()
    {
      _eye = null;
      _eyeDistances = null;
      _range = float.NaN;
    }
  }
}
