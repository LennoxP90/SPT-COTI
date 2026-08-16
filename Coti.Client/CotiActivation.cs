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
  }
}
