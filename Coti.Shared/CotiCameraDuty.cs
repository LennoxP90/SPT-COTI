namespace Coti.Shared
{
  /// <summary>Which device draws the magnified view: at most one, so there is only ever one magnified camera.</summary>
  public enum CotiMagnifiedOwner
  {
    None,
    Goggle,
    Riser,
  }

  /// <summary>
  /// Which thermal camera draws this frame. A thermal sight is its own picture, so neither does then. Behind a magnified
  /// sight the magnified camera draws the lens, and the 1x camera still draws for the goggle COTI, whose image is cut
  /// where the scope covers it; the thermal riser needs no 1x there, since its red dot window is not in view.
  /// </summary>
  public static class CotiCameraDuty
  {
    public static bool OneX( bool gogglesActive, bool riserEnabled, bool thermalSightAimed, bool magnifiedSightAimed )
    {
      return !thermalSightAimed && ( gogglesActive || ( riserEnabled && !magnifiedSightAimed ) );
    }

    public static bool Magnified( bool active, bool thermalSightAimed, bool magnifiedSightAimed )
    {
      return active && !thermalSightAimed && magnifiedSightAimed;
    }

    /// <summary>
    /// The one device that draws the magnified view: a powered riser or scope ring under the aimed optic, from its boot
    /// to the end of its Power Off message. The goggle COTI never draws inside a scope; its own view goes on around the
    /// scope, which is cut out of it, so only the device on the scope's mount heats the scope.
    /// </summary>
    public static CotiMagnifiedOwner MagnifiedOwner( bool riserPoweredUnderOptic )
    {
      return riserPoweredUnderOptic ? CotiMagnifiedOwner.Riser : CotiMagnifiedOwner.None;
    }
  }
}
