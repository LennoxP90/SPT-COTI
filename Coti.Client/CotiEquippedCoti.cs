using EFT.InventoryLogic;

namespace Coti.Client
{
  /// <summary>
  /// Which COTI slots of the equipped night vision device hold a COTI, held across frames and refreshed
  /// by <see cref="Patches.CotiInventoryChangePatch"/>.
  ///
  /// Scoped to the per-frame path rather than folded into CotiSlotProbe: the pose editor probes
  /// whatever item is open in the inventory, which is usually a different one, and a single shared
  /// cache would re-probe on every alternating read.
  /// </summary>
  internal static class CotiEquippedCoti
  {
    private static readonly CotiAttachCache<Item> Cache =
        new CotiAttachCache<Item>( CotiSlotProbe.FilledSlots );

    /// <summary>The filled COTI slots, as CotiTubeSet bits.</summary>
    internal static int FilledSlots( Item hostItem )
    {
      return Cache.Read( hostItem );
    }

    internal static void Invalidate()
    {
      Cache.Invalidate();
    }
  }
}
