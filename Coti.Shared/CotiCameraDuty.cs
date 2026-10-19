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
    /// Whether the magnified path runs at all. Magnify With Optic decides it for the goggle COTI. A thermal riser with a
    /// sight on it, on the weapon in hand, turns it on by itself, since the riser has no other way to show heat through a
    /// magnifier. Whether anything draws is up to <see cref="MagnifiedOwner"/> and that device's power.
    /// </summary>
    public static bool MagnifyEnabled( bool magnifyWithOptic, bool riserPresent )
    {
      return magnifyWithOptic || riserPresent;
    }

    /// <summary>
    /// The one device that draws the magnified view. A powered riser under the aimed optic owns it, from its boot to
    /// the end of its Power Off message, so the goggle COTI never grabs the lens while the riser's text shows. Otherwise
    /// the goggle COTI, if it renders and Magnify With Optic is on.
    /// </summary>
    public static CotiMagnifiedOwner MagnifiedOwner( bool riserPoweredUnderOptic, bool gogglesActive, bool magnifyWithOptic )
    {
      if( riserPoweredUnderOptic )
        return CotiMagnifiedOwner.Riser;

      return gogglesActive && magnifyWithOptic ? CotiMagnifiedOwner.Goggle : CotiMagnifiedOwner.None;
    }
  }
}
