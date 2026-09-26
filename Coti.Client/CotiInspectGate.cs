using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The inspect button's whole decision, as three states: no button on a non-host, a disabled
  /// button on a host with an empty mod_coti slot, and an enabled button once the slot is filled.
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
  /// A slot reduced to an id and whether it is filled, so HasFilledSlot below can be pure even
  /// though the real caller holds EFT.InventoryLogic.Slot objects.
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
  /// Pure and over primitives, like CotiActivation.ShouldBeActive: the panel that calls this
  /// cannot be tested, so the decision lives where a test can reach it. Source-linked into
  /// Coti.Tests like CotiMaskResolver.cs and CotiActivation.cs.
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
