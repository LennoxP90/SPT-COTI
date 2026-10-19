namespace Coti.Shared
{
  /// <summary>What the riser's light state needs this frame to match its power sequence.</summary>
  public enum CotiLightRequest
  {
    None,
    On,
    Off,
  }

  /// <summary>
  /// The thermal riser's light state, which is its LED and what Fika carries to the other players, follows its power
  /// sequence: lit from the first frame of the boot to the last frame of Power Off, dark only once the shutdown has
  /// ended. The sequence leads and the light follows, so nothing in the shutdown waits on the light.
  /// </summary>
  public static class CotiRiserLight
  {
    public static bool Lit( CotiPowerPhase phase ) => phase != CotiPowerPhase.Off;

    /// <summary>The change the light needs, if any, to match <paramref name="phase"/>.</summary>
    public static CotiLightRequest Request( CotiPowerPhase phase, bool lightOn )
    {
      var lit = Lit( phase );
      if( lit == lightOn )
        return CotiLightRequest.None;

      return lit ? CotiLightRequest.On : CotiLightRequest.Off;
    }

    /// <summary>
    /// A riser leaving the hands (holster, weapon swap, drop) in the middle of its shutdown ends the shutdown there and
    /// goes dark, so the LED never stays lit on a weapon nobody holds. Anywhere else in the sequence it keeps its state:
    /// a riser on stays on, as a flashlight does.
    /// </summary>
    public static bool EndsOnLeavingHands( CotiPowerPhase phase ) => phase == CotiPowerPhase.PoweringOff;
  }
}
