using System;
using System.Collections.Generic;
using System.Linq;

namespace Coti.Shared
{
  /// <summary>
  /// What the bot strip does with the thermal risers on a generated bot. Each riser stays when
  /// <c>keep</c> says so. Otherwise it goes, and the first item it held moves down into the riser's own
  /// slot when that slot takes it, so the bot keeps its sight; anything else under the riser goes with
  /// it, with everything under that. Pure, over plain strings, so the server passes its items and the
  /// tests pass made-up ones.
  /// </summary>
  public static class CotiRiserBotRule
  {
    /// <param name="items">Every inventory item: id, parent id, slot id, template id.</param>
    /// <param name="keep">Asked once per riser, in item order.</param>
    /// <param name="slotAccepts">Whether slot (2) of an item with template (1) takes template (3).</param>
    /// <returns>Ids to remove, and moves to apply in order: id, new parent id, new slot id.</returns>
    public static (HashSet<string> Remove, List<(string Id, string ParentId, string SlotId)> Moves) Plan(
        IReadOnlyList<(string Id, string? ParentId, string? SlotId, string Tpl)> items,
        Func<bool> keep,
        Func<string, string, string, bool> slotAccepts )
    {
      var remove = new HashSet<string>();
      var moves = new List<(string Id, string ParentId, string SlotId)>();

      // Where each item is now, so a move made for one riser is seen by the next.
      var at = new Dictionary<string, (string? ParentId, string? SlotId)>();
      var tplOf = new Dictionary<string, string>();
      foreach( var item in items )
      {
        at[item.Id] = ( item.ParentId, item.SlotId );
        tplOf[item.Id] = item.Tpl;
      }

      foreach( var riser in items.Where( i => CotiIds.IsRiser( i.Tpl ) ) )
      {
        // Already gone under another riser.
        if( remove.Contains( riser.Id ) || keep() )
          continue;

        remove.Add( riser.Id );
        var ( parentId, slotId ) = at[riser.Id];
        var moved = false;

        foreach( var child in ChildrenOf( riser.Id ) )
        {
          if( !moved && parentId != null && slotId != null && tplOf.TryGetValue( parentId, out var parentTpl ) &&
              slotAccepts( parentTpl, slotId, tplOf[child] ) )
          {
            moves.Add( ( child, parentId, slotId ) );
            at[child] = ( parentId, slotId );
            moved = true;
          }
          else
          {
            RemoveTree( child );
          }
        }
      }

      return ( remove, moves );

      List<string> ChildrenOf( string id ) =>
          at.Where( e => e.Value.ParentId == id && !remove.Contains( e.Key ) ).Select( e => e.Key ).ToList();

      void RemoveTree( string id )
      {
        var todo = new Stack<string>();
        todo.Push( id );
        while( todo.Count > 0 )
        {
          var next = todo.Pop();
          if( !remove.Add( next ) ) continue;
          foreach( var child in ChildrenOf( next ) )
            todo.Push( child );
        }
      }
    }
  }
}
