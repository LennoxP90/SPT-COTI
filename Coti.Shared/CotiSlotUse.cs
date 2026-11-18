using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>One item of a saved item list, reduced to what CotiSlotUse reads.</summary>
  public readonly struct CotiSavedItem
  {
    public CotiSavedItem( string? id, string? template, string? parentId, string? slotId )
    {
      Id = id;
      Template = template;
      ParentId = parentId;
      SlotId = slotId;
    }

    public string? Id { get; }
    public string? Template { get; }
    public string? ParentId { get; }
    public string? SlotId { get; }
  }

  /// <summary>
  /// Which night vision templates hold something in a COTI slot in a saved item list (an inventory, a mail message, an
  /// insurance return, a build). A host that loses its COTI slots while one is still occupied breaks the profile that
  /// holds it, so the server keeps those slots even with no device file.
  /// </summary>
  public static class CotiSlotUse
  {
    /// <summary>Adds to <paramref name="into"/> the template of every item that holds something in a COTI slot.</summary>
    public static void CollectHostsInUse( IReadOnlyList<CotiSavedItem> items, ISet<string> into )
    {
      Dictionary<string, string>? templateById = null;

      foreach( var item in items )
      {
        if( !CotiTubes.IsCotiSlot( item.SlotId ) || item.ParentId == null )
          continue;

        // Parent ids point within the same list, so the lookup is built only for a list that has a COTI slot in use.
        if( templateById == null )
        {
          templateById = new Dictionary<string, string>();
          foreach( var other in items )
          {
            if( other.Id != null && other.Template != null )
              templateById[other.Id] = other.Template;
          }
        }

        if( templateById.TryGetValue( item.ParentId, out var hostTemplate ) )
          into.Add( hostTemplate );
      }
    }
  }
}
