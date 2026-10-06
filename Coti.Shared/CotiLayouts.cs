using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>One tube's COTI circle, in screen heights from the screen centre.</summary>
  public sealed class CotiLayoutTube
  {
    public CotiLayoutTube( string label, float dx, float dy, float radius )
    {
      Label = label;
      Dx = dx;
      Dy = dy;
      Radius = radius;
    }

    /// <summary>A CotiTubes label.</summary>
    public string Label { get; }

    /// <summary>Screen heights right of centre.</summary>
    public float Dx { get; }

    /// <summary>Screen heights above centre.</summary>
    public float Dy { get; }

    /// <summary>The COTI circle's radius in screen heights, already 0.709 of Borkel's hole.</summary>
    public float Radius { get; }
  }

  /// <summary>One of Borkel's hole patterns: its tubes left to right, and the home tube that keeps mod_coti.</summary>
  public sealed class CotiLayout
  {
    public CotiLayout( string name, string home, CotiLayoutTube[] tubes )
    {
      Name = name;
      Home = home;
      Tubes = tubes;
    }

    public string Name { get; }
    public string Home { get; }
    public IReadOnlyList<CotiLayoutTube> Tubes { get; }

    public CotiLayoutTube? Find( string? label )
    {
      for( var i = 0; i < Tubes.Count; i++ )
      {
        if( Tubes[i].Label == label )
          return Tubes[i];
      }

      return null;
    }
  }

  /// <summary>
  /// The four tube layouts. The circles are measured from BorkelRNVG's masks into
  /// CotiLayouts.g.cs, so the server's slots, the client's circles and the tests all compile one table.
  /// </summary>
  public static partial class CotiLayouts
  {
    public const float Feather = 0.01f;

    // A property, not a field: static initialisers in two files of one partial class run in no defined order.
    public static IReadOnlyList<CotiLayout> All => new[] { Quad, Dual, Mono, Pvs5a };

    public static bool TryGet( string? name, out CotiLayout layout )
    {
      foreach( var candidate in All )
      {
        if( candidate.Name == name )
        {
          layout = candidate;
          return true;
        }
      }

      layout = null!;
      return false;
    }
  }
}
