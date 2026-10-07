using System;

namespace Coti.Shared
{
  /// <summary>
  /// Whether a block still holds the heat COTI wants in it, so an unchanged surface is not written again. EFT replaces a
  /// material's whole block when its own temperature changes (HotObject), which reads back as 0, never a COTI value (in
  /// Kelvin). Within a hundredth of a degree counts as holding, so the air's slow drift does not rewrite every block.
  /// </summary>
  public static class CotiHeatRewrite
  {
    public const float ToleranceDegrees = 0.01f;

    public static bool Holds( float written, float wanted )
    {
      return Math.Abs( written - wanted ) <= ToleranceDegrees;
    }
  }
}
