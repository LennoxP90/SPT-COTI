using EFT.CameraControl;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The magnified optic EFT is rendering this frame, read from the game's own optic manager.
  ///
  /// Measured in a raid: an optic camera is live only while aiming a magnified sight, it renders to
  /// its own 1024x1024 target which the game draws onto the lens, variable zoom moves fieldOfView on
  /// ONE reused camera, and its parent is null - it sits at the scope, not at the eye. That last
  /// point is why callers must take the transform from here rather than assuming the eye.
  ///
  /// Asking the manager rather than scanning <c>Camera.allCameras</c> also hands over
  /// <see cref="OpticSight.LensRenderer"/>, which no renderer search can find: the lens texture is
  /// published with <c>Shader.SetGlobalTexture</c>, on no material and no property block.
  ///
  /// Presence is NOT the 1x test - <see cref="CotiOpticFusion.ShouldMagnify"/> is, by ratio.
  /// </summary>
  internal static class CotiOpticCamera
  {
    /// <summary>
    /// The optic for this frame, or an absent view. Never cached: the camera comes and goes with
    /// aiming and its fieldOfView moves under it as the player works a variable scope's zoom.
    /// </summary>
    internal static CotiOpticView Read()
    {
      Camera camera;
      OpticSight sight;

      return EftCompat.TryGetOptic( out camera, out sight )
          ? new CotiOpticView( camera, sight )
          : default( CotiOpticView );
    }
  }

  /// <summary>
  /// One frame's answer about the optic: the camera to match, and the lens to mask against.
  /// </summary>
  internal struct CotiOpticView
  {
    private readonly Camera _camera;
    private readonly OpticSight _sight;

    internal CotiOpticView( Camera camera, OpticSight sight )
    {
      _camera = camera;
      _sight = sight;
    }

    /// <summary>
    /// True when there is an optic worth matching to. The camera's active state is part of it: the
    /// manager deactivates it when the weapon comes down, and activates it for a single prewarm
    /// frame with no sight attached.
    /// </summary>
    internal bool Present => _camera != null && _sight != null && _camera.isActiveAndEnabled;

    /// <summary>
    /// The camera to copy field of view and world transform from, per frame.
    /// </summary>
    internal Camera Camera => Present ? _camera : null;

    /// <summary>
    /// The optic's field of view, or 0 when there is no optic - the value
    /// <see cref="CotiOpticFusion"/> treats as nothing to match.
    /// </summary>
    internal float FieldOfView => Present ? _camera.fieldOfView : 0f;

    /// <summary>
    /// The lens the game draws the optic's picture onto, and the mask source for the composite.
    /// </summary>
    internal Renderer Lens => Present ? _sight.LensRenderer : null;
  }
}
