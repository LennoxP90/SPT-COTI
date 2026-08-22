using Coti.Shared;
using System.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Coti.Server;

/// <summary>
/// ICotiItemView over the live template table. IDs that are not a well-formed MongoId are
/// treated as "does not exist" rather than thrown on - a hand-authored device file can put
/// anything in "id", and the resolver already has a path for "not installed".
///
/// One copy, shared by CotiDeviceStore (host resolution) and CotiHostDiscovery (classification) -
/// two adapters over the same item table would be a second source of truth that could drift.
/// </summary>
public sealed class CotiTemplateItemView : ICotiItemView
{
  private readonly CotiTemplateTable table;

  public CotiTemplateItemView( CotiTemplateTable table )
  {
    this.table = table;
  }

  public bool Exists( string id ) => TryGet( id, out _ );

  public string? PrefabPath( string id ) =>
      TryGet( id, out var item ) ? item!.Properties?.Prefab?.Path : null;

  public string? ParentOf( string id ) =>
      TryGet( id, out var item ) ? (string) item!.Parent : null;

  public IEnumerable<string> AllIds() => table.Items.Keys.Select( id => (string) id );

  private bool TryGet( string id, out TemplateItem? item )
  {
    item = null;

    if( string.IsNullOrEmpty( id ) || !MongoId.IsValidMongoId( id ) )
      return false;

    return table.Items.TryGetValue( new MongoId( id ), out item );
  }
}
