using System;
using System.Collections.Generic;

namespace Coti.Shared
{
  public enum CotiTubeSide { Center, Left, Right }

  /// <summary>
  /// Tube labels, tube_0 to tube_3 left to right (tube_center on a mono), and the COTI slot each one gets. The home tube
  /// keeps mod_coti, the single-tube slot, so every COTI already fitted in a profile stays where it is. Its
  /// label stays "ECOTI" because a slot's locale key is global per slot name and the mono's only tube shares it.
  /// </summary>
  public static class CotiTubes
  {
    public const string Tube0 = "tube_0";       // outer left
    public const string Tube1 = "tube_1";       // inner left
    public const string Tube2 = "tube_2";       // inner right, home on quad, dual and pvs5a
    public const string Tube3 = "tube_3";       // outer right
    public const string Center = "tube_center"; // mono, home

    public const string Slot0 = "mod_coti_0";
    public const string Slot1 = "mod_coti_1";
    public const string Slot3 = "mod_coti_3";

    // After the home tube: the second COTI completes the centre pair, then the outer tubes, right before left.
    private static readonly string[] PickOrder = { Tube1, Tube3, Tube0 };

    public static string SlotName( string label, CotiLayout layout )
    {
      if( label == layout.Home )
        return CotiIds.ModSlotName;

      if( layout.Find( label ) != null )
      {
        switch( label )
        {
          case Tube0: return Slot0;
          case Tube1: return Slot1;
          case Tube3: return Slot3;
        }
      }

      throw new ArgumentException( $"\"{label}\" has no slot on the {layout.Name} layout", nameof( label ) );
    }

    /// <summary>The layout's slots in auto-pick order. A v1 device (null layout) has mod_coti only.</summary>
    public static IReadOnlyList<string> SlotNames( CotiLayout? layout )
    {
      var names = new List<string> { CotiIds.ModSlotName };
      if( layout == null )
        return names;

      foreach( var label in PickOrder )
      {
        if( layout.Find( label ) != null )
          names.Add( SlotName( label, layout ) );
      }

      return names;
    }

    public static string? LabelOfSlot( string? slotName, CotiLayout? layout )
    {
      if( layout == null )
        return null;

      foreach( var tube in layout.Tubes )
      {
        if( SlotName( tube.Label, layout ) == slotName )
          return tube.Label;
      }

      return null;
    }

    public static bool IsCotiSlot( string? slotName )
    {
      return slotName == CotiIds.ModSlotName || slotName == Slot0 || slotName == Slot1 || slotName == Slot3;
    }

    public static string SlotDisplayName( string slotName )
    {
      switch( slotName )
      {
        case CotiIds.ModSlotName: return "ECOTI";
        case Slot1: return "ECOTI L";
        case Slot3: return "ECOTI OR";
        case Slot0: return "ECOTI OL";
        default: throw new ArgumentException( $"\"{slotName}\" is not a COTI slot", nameof( slotName ) );
      }
    }

    /// <summary>The mount editor's name for a tube: its label, where it sits, and whether it is home ("tube_2 (R, home)").</summary>
    public static string PanelName( string label, CotiLayout layout )
    {
      string? place = null;

      switch( label )
      {
        case Tube0: place = "OL"; break;
        case Tube1: place = "L"; break;
        case Tube2: place = "R"; break;
        case Tube3: place = "OR"; break;
      }

      // One tube needs no home mark.
      if( label == layout.Home && layout.Tubes.Count > 1 )
        place = place == null ? "home" : place + ", home";

      return place == null ? label : $"{label} ({place})";
    }

    public static CotiTubeSide SideOf( string? label )
    {
      return label == Tube0 || label == Tube1 ? CotiTubeSide.Left
          : label == Tube2 || label == Tube3 ? CotiTubeSide.Right
          : CotiTubeSide.Center;
    }

    public static string? PartnerOf( string label )
    {
      switch( label )
      {
        case Tube0: return Tube3;
        case Tube1: return Tube2;
        case Tube2: return Tube1;
        case Tube3: return Tube0;
        default: return null;
      }
    }
  }
}
