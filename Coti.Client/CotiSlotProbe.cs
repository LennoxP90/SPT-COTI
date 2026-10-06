using System.Collections.Generic;
using Coti.Shared;
using EFT.InventoryLogic;

namespace Coti.Client
{
  /// <summary>
  /// Whether an item's COTI slots are filled. Keys on Slot.ID: the server writes Slot.Name,
  /// which serialises as _name and arrives on the client as ID.
  /// </summary>
  internal static class CotiSlotProbe
  {
    /// <summary>Whether any of hostItem's COTI slots has something in it.</summary>
    public static bool IsCotiAttached( Item hostItem )
    {
      return CotiInspectGateResolver.HasFilledCotiSlot( SlotSnapshots( hostItem ) );
    }

    /// <summary>The COTI slots on hostItem with something in them, as CotiTubeSet bits.</summary>
    public static int FilledSlots( Item hostItem )
    {
      var slots = 0;
      foreach( var slot in SlotSnapshots( hostItem ) )
      {
        if( slot.Filled )
          slots |= CotiTubeSet.Bit( slot.Id );
      }

      return slots;
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
