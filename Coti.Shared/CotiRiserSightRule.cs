using System;
using System.Collections.Generic;
using System.Linq;

namespace Coti.Shared
{
  /// <summary>
  /// Which of the donor riser's peers the thermal riser's mod_scope accepts: red dots, ring-free scopes (scopes that are
  /// peers clamp on the rail with their own base) and red dot mounts. A red dot mount's mod_scope slots hold at least one
  /// red dot and nothing but red dots and mounts, and every mount it holds, at any depth, passes the same test. On top
  /// of that, nothing an accepted item can hold, at any depth, through its own mod_scope* and mod_mount* slots may
  /// magnify, so no chain of mounts carries a scope onto the riser and a scope's piggyback slot takes only 1x sights.
  /// The item itself may magnify (the HHS-1, the scopes). A loop of mounts is checked once per mount and never refuses
  /// on its own. Generic over the id type so the server passes MongoIds and the tests pass strings.
  /// </summary>
  public static class CotiRiserSightRule
  {
    /// <summary>
    /// A rear sight slot: mod_sight_rear, its numbered copies (C11's mod_sight_rear_000 and _001) and the
    /// mod_rear_sight some mods use instead. Everything such a slot accepts conflicts with the riser.
    /// </summary>
    public static bool IsRearSightSlot( string? name )
    {
      return name != null && ( name.StartsWith( "mod_sight_rear", StringComparison.OrdinalIgnoreCase ) ||
                               name.StartsWith( "mod_rear_sight", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>The slots the holder check walks: mod_scope* and mod_mount*.</summary>
    public static bool IsHolderSlot( string? name )
    {
      return name != null && ( name.StartsWith( "mod_scope", StringComparison.OrdinalIgnoreCase ) ||
                               name.StartsWith( "mod_mount", StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Magnifies, judged by the optic and not its class, since the HHS-1 and the red dot and magnifier combos are red
    /// dot class: any zoom above 1, or a sightModType of hybrid or optic.
    /// </summary>
    public static bool Magnifies( IEnumerable<IEnumerable<double>>? zooms, string? sightModType )
    {
      return zooms?.Any( z => z?.Any( x => x > 1 ) == true ) == true ||
             string.Equals( sightModType, "hybrid", StringComparison.OrdinalIgnoreCase ) ||
             string.Equals( sightModType, "optic", StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// A scope slot outside the LaRue set that also takes the riser: named mod_scope*, on an item that is neither a
    /// sight (so not the riser, a scope's piggyback slot or a red dot with slots) nor anything the riser accepts (no
    /// riser on its own mounts), and accepting at least one item the riser accepts. Every accepted item is a rail peer,
    /// so a dovetail-only or other non-Picatinny slot accepts none of them and is left out.
    /// </summary>
    public static bool IsExtraHostSlot<T>( string? slotName, T owner, IEnumerable<T> filter, Func<T, bool> isSight,
        ICollection<T> accepted )
    {
      return slotName != null && slotName.StartsWith( "mod_scope", StringComparison.OrdinalIgnoreCase ) &&
             !isSight( owner ) && !accepted.Contains( owner ) && filter.Any( accepted.Contains );
    }

    /// <param name="peers">Every id sharing a slot filter with the donor riser.</param>
    /// <param name="excluded">Rear sights and denied ids; checked on the peer itself only.</param>
    /// <param name="isScope">AssaultScope or OpticScope class: accepted as a scope.</param>
    /// <param name="magnifies">Refuses any item that can hold it; the server adds the scope classes to <see cref="Magnifies(IEnumerable{IEnumerable{double}}, string)"/>.</param>
    /// <param name="scopeHeld">Every id the item's mod_scope* slot filters accept: the red dot mount test.</param>
    /// <param name="held">Every id the item's mod_scope* and mod_mount* slot filters accept: the holder check.</param>
    public static HashSet<T> Accept<T>( IEnumerable<T> peers, Func<T, bool> excluded, Func<T, bool> isRedDot,
        Func<T, bool> isMount, Func<T, bool> isScope, Func<T, bool> magnifies, Func<T, IEnumerable<T>> scopeHeld,
        Func<T, IEnumerable<T>> held )
    {
      return new HashSet<T>( peers.Where( id => !excluded( id ) &&
                                                ( isRedDot( id ) || isScope( id ) || IsRedDotMount( id ) ) &&
                                                !CarriesMagnified( id ) ) );

      bool IsRedDotMount( T id )
      {
        if( !isMount( id ) ) return false;
        var seen = new HashSet<T> { id };
        var todo = new Stack<T>();
        todo.Push( id );
        while( todo.Count > 0 )
        {
          var h = scopeHeld( todo.Pop() ).ToList();
          if( !h.Any( isRedDot ) ) return false;
          foreach( var x in h )
          {
            if( isRedDot( x ) ) continue;
            if( !isMount( x ) ) return false;
            if( seen.Add( x ) ) todo.Push( x );
          }
        }
        return true;
      }

      // Starts from what the item holds, not the item, so a scope or the HHS-1 is not refused for magnifying itself.
      bool CarriesMagnified( T id )
      {
        var seen = new HashSet<T> { id };
        var todo = new Stack<T>();
        todo.Push( id );
        while( todo.Count > 0 )
        {
          foreach( var x in held( todo.Pop() ) )
          {
            if( !seen.Add( x ) ) continue;
            if( magnifies( x ) ) return true;
            todo.Push( x );
          }
        }
        return false;
      }
    }
  }
}
