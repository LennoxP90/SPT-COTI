using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The inspect button's whole decision, as three states rather than a bool. A bool can only
  /// ever express two of them, and the button's real behaviour has three: no button on a
  /// non-host, a disabled button on a host with an empty mod_coti slot, and an enabled button
  /// once the slot is filled. The original two-valued ShouldBeInteractable modelled only the last
  /// two, which is exactly why a non-host got a permanently-disabled button instead of none.
  /// </summary>
  public enum CotiInspectGate
  {
    /// <summary>Not a known NVG host at all - no button should be created.</summary>
    NoButton,

    /// <summary>A known host, but the mod_coti slot is empty - button shown, disabled.</summary>
    Disabled,

    /// <summary>A known host with a filled mod_coti slot - button shown, enabled.</summary>
    Enabled,
  }

  /// <summary>
  /// A slot reduced to an id and whether it is filled - nothing else. This is what lets
  /// HasFilledSlot below be pure and over primitives even though the real caller only ever has
  /// EFT.InventoryLogic.Slot objects to offer it.
  /// </summary>
  public readonly struct CotiSlotSnapshot
  {
    public readonly string Id;
    public readonly bool Filled;

    public CotiSlotSnapshot( string id, bool filled )
    {
      Id = id;
      Filled = filled;
    }
  }

  /// <summary>
  /// Pure and over primitives on purpose, same reasoning as CotiActivation.ShouldBeActive: the
  /// panel that actually calls this cannot be tested at all, so the decision itself has to live
  /// somewhere a test can reach. Source-linked into Coti.Tests the same way CotiMaskResolver.cs
  /// and CotiActivation.cs already are.
  /// </summary>
  public static class CotiInspectGateResolver
  {
    public static bool HasFilledSlot( IEnumerable<CotiSlotSnapshot> slots, string slotId )
    {
      foreach( var slot in slots )
      {
        if( slot.Id == slotId )
          return slot.Filled;
      }

      return false;
    }

    /// <summary>
    /// The button's gate: absent for a non-host, disabled for a host with an empty slot, enabled
    /// for a host carrying a COTI.
    /// </summary>
    public static CotiInspectGate Resolve( bool isKnownHost, bool cotiSlotFilled )
    {
      if( !isKnownHost )
        return CotiInspectGate.NoButton;

      return cotiSlotFilled ? CotiInspectGate.Enabled : CotiInspectGate.Disabled;
    }
  }
}
