namespace Coti.Shared
{
  /// <summary>A device a key's feedback shows on: the goggle COTI's image or the thermal riser's sight.</summary>
  public enum CotiDevice
  {
    None,
    Goggle,
    Riser,
  }

  /// <summary>Where the riser's messages draw: its red dot window, or the magnifier's lens while that is aimed.</summary>
  public enum CotiRiserTextLens
  {
    RedDotWindow,
    MagnifiedLens,
  }

  /// <summary>
  /// Routing for the keys and their on-screen feedback. Each device has its own power key, so power needs no routing;
  /// the one shared mode does, since its name should show only where the player is looking.
  /// </summary>
  public static class CotiDeviceKeys
  {
    /// <summary>
    /// Where the mode name shows on Mode Toggle: the riser's sight while the player aims through it with the riser on;
    /// else the goggle image while it is lit; else the riser's red dot window while the riser is on, at the hip too;
    /// else nowhere (the mode still changes).
    /// </summary>
    public static CotiDevice ModeNameOn( bool aimedThroughRiser, bool riserOn, bool goggleOn )
    {
      if( aimedThroughRiser && riserOn )
        return CotiDevice.Riser;

      if( goggleOn )
        return CotiDevice.Goggle;

      return riserOn ? CotiDevice.Riser : CotiDevice.None;
    }

    /// <summary>
    /// The riser's text goes on the magnified lens while a magnified optic on the riser is aimed, else in the red dot
    /// window: one lens, so the text is never in both views.
    /// </summary>
    public static CotiRiserTextLens RiserTextLens( bool magnifiedOpticOnRiserAimed )
    {
      return magnifiedOpticOnRiserAimed ? CotiRiserTextLens.MagnifiedLens : CotiRiserTextLens.RedDotWindow;
    }
  }
}
