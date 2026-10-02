using System;

namespace Coti.Shared
{
  /// <summary>
  /// EFT's BetterAudio.PlayNonspatial applies its volume twice - SetBaseVolume sets the source's
  /// volume, then PlayOneShot scales by it again - so what is heard is the square of what is passed.
  /// Sending the square root keeps Click Volume linear.
  /// </summary>
  public static class CotiClickVolume
  {
    public static float ForPlayNonspatial( float setting )
    {
      if( !( setting > 0f ) )
        return 0f;

      return (float)Math.Sqrt( setting < 1f ? setting : 1f );
    }
  }
}
