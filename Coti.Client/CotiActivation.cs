namespace Coti.Client
{
  public static class CotiActivation
  {
    /// <summary>
    /// The overlay is injected into a tube image, so every condition is ANDed: a powered COTI on
    /// unpowered goggles still shows nothing. The headless has no camera effects at all.
    /// </summary>
    public static bool ShouldBeActive(
        bool isHeadless, bool cotiAttached, bool hostNvgOn, bool cotiPoweredOn )
    {
      if( isHeadless )
        return false;
      return cotiAttached && hostNvgOn && cotiPoweredOn;
    }

    /// <summary>
    /// The device's own display (its boot and shutdown messages) is seen through the tube exactly
    /// like the thermal, so the same conditions apply.
    /// </summary>
    public static bool ShouldShowDisplay(
        bool isHeadless, bool cotiAttached, bool hostNvgOn, bool displayLit )
    {
      if( isHeadless )
        return false;
      return cotiAttached && hostNvgOn && displayLit;
    }

    /// <summary>
    /// The sensor's own click, so it needs a COTI attached and the mod switched on. It does not need
    /// the tube: the device clicks whether or not it is being looked through.
    /// </summary>
    public static bool ShouldClick( bool isHeadless, bool clickNow, bool cotiAttached, bool enabled )
    {
      if( isHeadless )
        return false;
      return clickNow && cotiAttached && enabled;
    }
  }
}
