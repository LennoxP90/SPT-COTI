using Comfort.Common;
using Coti.Shared;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The magnified optic EFT is rendering this frame, read from the game's own optic manager.
  ///
  /// An optic camera is live only while aiming a magnified sight. It renders to its own 1024x1024
  /// target which the game draws onto the lens, variable zoom moves fieldOfView on one reused
  /// camera, and its parent is null. It sits at the scope rather than the eye, so callers take the
  /// transform from here.
  ///
  /// Asking the manager rather than scanning <c>Camera.allCameras</c> also hands over
  /// <see cref="OpticSight.LensRenderer"/>, which no renderer search can find: the lens texture is
  /// published with <c>Shader.SetGlobalTexture</c>, on no material and no property block.
  ///
  /// Presence is not the 1x test - <see cref="CotiOpticFusion.ShouldMagnify"/> is, by ratio.
  /// </summary>
  internal static class CotiOpticCamera
  {
    /// <summary>
    /// Whether the player is looking through a thermal sight (a REAP-IR, an RS-32) this frame. COTI stands down
    /// entirely then: the sight is already a thermal picture, and COTI's heat drawn over it would be a second, offset
    /// one. Read once per frame by <see cref="Tick"/>, before the cameras and compositors.
    /// </summary>
    internal static bool ThermalSightAimed { get; private set; }

    /// <summary>
    /// Whether a magnified sight is up with the magnified path on (<see cref="MagnifyEnabled"/>), decided from the optic
    /// alone rather than from whether the magnified camera is drawing yet. The 1x overlay reads it to draw a message's
    /// text alone over the scope, with no circle glow.
    /// </summary>
    internal static bool MagnifiedSightAimed { get; private set; }

    /// <summary>
    /// Whether a magnified (non-thermal) optic is aimed, by the zoom ratio alone, whatever the magnified path's switch.
    /// The 1x overlay cuts the aimed scope's silhouette out of the goggle image while this holds.
    /// </summary>
    internal static bool MagnifiedOpticAimed { get; private set; }

    /// <summary>The aimed magnified optic sits on a thermal riser or scope ring, so the device can see through it.</summary>
    internal static bool OpticOnRiser { get; private set; }


    /// <summary>
    /// The aimed scope whose picture is the zoomed main camera, with no optic camera of its own (PiP-Disabler). A riser or
    /// ring under it draws its 1x heat through this lens, since the 1x camera already matches the zoom; else null.
    /// </summary>
    internal static OpticSight ScreenSight { get; private set; }

    /// <summary>The aimed optic's sight, while <see cref="MagnifiedOpticAimed"/> holds; else null.</summary>
    internal static OpticSight AimedSight { get; private set; }

    /// <summary>
    /// The aimed scope the goggle COTI's view is cut around: any non-thermal sight that renders a picture-in-picture
    /// image (an OpticSight with its optic camera), at any zoom, so a variable scope at its lowest setting is cut too. A red dot has no OpticSight, so the
    /// goggle's heat still shows through its window. Else null.
    /// </summary>
    internal static OpticSight CutSight { get; private set; }

    /// <summary>
    /// The magnified path's switch, read by this, the magnified camera and its compositor alike: a thermal riser or scope
    /// ring with a sight on it, on the weapon in hand. The goggle COTI never draws inside a scope.
    /// </summary>
    internal static bool MagnifyEnabled => ThermalRiserState.Present;

    /// <summary>
    /// The one device that draws the magnified view (CotiCameraDuty.MagnifiedOwner): a powered riser or ring under the
    /// aimed optic, else nothing.
    /// </summary>
    internal static CotiMagnifiedOwner MagnifiedOwner =>
        CotiCameraDuty.MagnifiedOwner( ThermalRiserState.Powered && OpticOnRiser );

    internal static void Tick()
    {
      if( Plugin.PipDisabler )
        CotiLensMeshes.Tick();
      var optic = Read();
      var present = optic.Present;
      var thermal = optic.IsThermal;
      ThermalSightAimed = thermal;
      var main = CotiFrame.Main;
      var magnified = present && !thermal && main != null
                      && CotiOpticFusion.ShouldMagnify( configEnabled: true, cotiActive: true,
                             main.fieldOfView, optic.FieldOfView );
      MagnifiedOpticAimed = magnified;
      AimedSight = magnified ? optic.Sight : null;
      // PiP-Disabler leaves the optic camera switched off at the hip as well, so only an aimed sight counts.
      var screen = Plugin.PipDisabler && Aiming() ? optic.ScreenSight : null;
      if( screen != null && screen != ScreenSight && ( Plugin.Config?.VerboseLogging ?? false ) )
        Plugin.Log.LogInfo( $"[COTI] Scope with no optic camera aimed: {screen.name}, lens " +
                            $"{( CotiLensMeshes.Hidden( screen.LensRenderer ) != null ? "emptied, drawn from its remembered mesh" : "as drawn" )}" );
      ScreenSight = screen;
      // Either way the scope's own glass sits in front of a COTI camera looking through it: the 1x under PiP-Disabler,
      // the magnified thermal camera without it.
      CotiScreenSightGlass.Sync( screen ?? AimedSight );
      // The goggle COTI never draws in a scope, whichever camera paints the scope's picture.
      CutSight = present && !thermal ? optic.Sight : ScreenSight;
      OpticOnRiser = magnified && ThermalRiserState.Present && ThermalRiserState.IsOnRiser( optic.Sight.transform );
      MagnifiedSightAimed = magnified && MagnifyEnabled && !CotiOpticThermalCamera.Broken;
    }

    private static Camera _thermalVisionCamera;
    private static ThermalVision _thermalVision;

    /// <summary>The optic camera's ThermalVision, looked up again only when EFT hands over a different camera.</summary>
    internal static ThermalVision ThermalVisionOf( Camera camera )
    {
      if( !ReferenceEquals( camera, _thermalVisionCamera ) || _thermalVision == null )
      {
        _thermalVisionCamera = camera;
        _thermalVision = camera.GetComponent<ThermalVision>();
      }

      return _thermalVision;
    }

    /// <summary>
    /// The optic for this frame, or an absent view. Never cached: the camera comes and goes with
    /// aiming and its fieldOfView moves under it as the player works a variable scope's zoom.
    /// Absent too while the player aims through a dual sight's red dot whose scope camera stays live
    /// (<see cref="CotiOpticFusion.AimedThroughOptic"/>), so every reader sees that as 1x.
    /// </summary>
    internal static CotiOpticView Read()
    {
      Camera camera;
      OpticSight sight;

      return EftCompat.TryGetOptic( out camera, out sight ) && AimedThroughOptic()
          ? new CotiOpticView( camera, sight )
          : default( CotiOpticView );
    }

    private static bool Aiming()
    {
      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      return world?.MainPlayer?.ProceduralWeaponAnimation?.IsAiming ?? false;
    }

    private static bool AimedThroughOptic()
    {
      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      var scope = world?.MainPlayer?.ProceduralWeaponAnimation?.CurrentScope;
      return CotiOpticFusion.AimedThroughOptic( scope != null, scope != null && scope.BoneRelatesToOptics );
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

    /// <summary>The optic's sight, the root of its lens and modes.</summary>
    internal OpticSight Sight => Present ? _sight : null;

    /// <summary>
    /// The aimed sight while its optic camera is switched off, as PiP-Disabler does: the main camera zooms in its place,
    /// so the scope's picture is the screen itself. A thermal sight is left out: it is its own picture either way.
    /// </summary>
    internal OpticSight ScreenSight
    {
      get
      {
        if( _sight == null || _camera == null || _camera.enabled )
          return null;

        var thermal = CotiOpticCamera.ThermalVisionOf( _camera );
        return thermal != null && thermal.enabled && thermal.On ? null : _sight;
      }
    }

    /// <summary>
    /// A thermal sight renders its picture with the optic camera's ThermalVision on; on every other sight that
    /// component is disabled.
    /// </summary>
    internal bool IsThermal
    {
      get
      {
        if( !Present )
          return false;

        var thermal = CotiOpticCamera.ThermalVisionOf( _camera );
        return thermal != null && thermal.enabled && thermal.On;
      }
    }
  }
}
