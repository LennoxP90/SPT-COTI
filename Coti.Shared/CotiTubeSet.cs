using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// A set of COTI slots as the bits of an int, so the per-frame tube state allocates nothing. Bit i is the i-th slot in
  /// auto-pick order (mod_coti, mod_coti_1, mod_coti_3, mod_coti_0) on every layout, so the inventory probe builds a set
  /// without knowing the device's layout.
  /// </summary>
  public static class CotiTubeSet
  {
    private static readonly string[] Order = { CotiIds.ModSlotName, CotiTubes.Slot1, CotiTubes.Slot3, CotiTubes.Slot0 };

    /// <summary>The slot's bit, 0 for anything that is not a COTI slot.</summary>
    public static int Bit( string? slotName )
    {
      for( var i = 0; i < Order.Length; i++ )
      {
        if( Order[i] == slotName )
          return 1 << i;
      }

      return 0;
    }

    /// <summary>
    /// A set for a log line: the layout's tubes left to right by number ("1,2", "center" on a mono), mod_coti on a
    /// device file without a layout, "none" when empty.
    /// </summary>
    public static string Describe( int slots, CotiLayout? layout )
    {
      var names = new List<string>();
      if( layout == null )
      {
        if( ( slots & Bit( CotiIds.ModSlotName ) ) != 0 )
          names.Add( CotiIds.ModSlotName );
      }
      else
      {
        foreach( var tube in layout.Tubes )
        {
          if( ( slots & Bit( CotiTubes.SlotName( tube.Label, layout ) ) ) != 0 )
            names.Add( tube.Label.Substring( "tube_".Length ) );
        }
      }

      return names.Count == 0 ? "none" : string.Join( ",", names );
    }
  }
}
