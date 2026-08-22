using System.Collections.Generic;
using Coti.Shared;
using EFT.InventoryLogic;

namespace Coti.Client
{
  /// <summary>
  /// Whether an item's mod_coti slot is filled. Keys on Slot.ID: the server writes Slot.Name,
  /// which serialises as _name and arrives on the client as ID.
  /// </summary>
  internal static class CotiSlotProbe
  {
    /// <summary>Whether hostItem carries a mod_coti slot with something in it.</summary>
    public static bool IsCotiAttached( Item hostItem )
    {
      return HasFilledSlot( hostItem, CotiIds.ModSlotName );
    }

    public static bool HasFilledSlot( Item hostItem, string slotId )
    {
      return CotiInspectGateResolver.HasFilledSlot( SlotSnapshots( hostItem ), slotId );
    }

    /// <summary>
    /// Every slot on the item reduced to an id and whether it is filled. A non-compound item, or
    /// one with no Slots collection at all, yields nothing rather than throwing - "not equipped"
    /// and "equipped but not a host" are both ordinary states here, reached every frame in the
    /// menu.
    /// </summary>
    public static IEnumerable<CotiSlotSnapshot> SlotSnapshots( Item hostItem )
    {
      var slots = ( hostItem as CompoundItem )?.Slots;
      if( slots == null )
        yield break;

      foreach( var slot in slots )
      {
        if( slot != null )
          yield return new CotiSlotSnapshot( slot.ID, slot.ContainedItem != null );
      }
    }
  }
}
