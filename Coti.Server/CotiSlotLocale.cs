using Coti.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Coti.Server;

/// <summary>
/// Names the COTI slots, so an empty one reads "ECOTI", "ECOTI L", "ECOTI OR" or "ECOTI OL" rather
/// than "MOD_COTI_1". EFT labels a slot with Name.Localized().ToUpper(), which falls back to the raw
/// key, and these slots are custom so they have no entry of their own. Registered for every
/// installed language, since the fallback would show through on any locale left out.
/// </summary>
[Injectable( TypePriority = CotiLoadOrder.PostLoad + 30 )]
public class CotiSlotLocale : IOnLoad
{
  private readonly ISptLogger<CotiSlotLocale> logger;

#if SPT40
  private readonly DatabaseServer databaseServer;
  // GetTables() throws until DatabaseImporter has run, and DI builds this object long
  // before that - so the table is resolved on use, inside OnLoad, never in the constructor.
  private CotiLocaleTable locales => databaseServer.GetTables().Locales;

  public CotiSlotLocale( ISptLogger<CotiSlotLocale> logger, DatabaseServer databaseServer )
  {
    this.logger = logger;
    this.databaseServer = databaseServer;
  }
#else
  private readonly CotiLocaleTable locales;

  public CotiSlotLocale( ISptLogger<CotiSlotLocale> logger, CotiLocaleTable locales )
  {
    this.logger = logger;
    this.locales = locales;
  }
#endif

  // The interface member differs between versions; the work does not.
#if SPT40
  public Task OnLoad() => LoadAsync( CancellationToken.None );
#else
  public Task OnLoadAsync( CancellationToken cancellationToken ) => LoadAsync( cancellationToken );
#endif

  private Task LoadAsync( CancellationToken cancellationToken )
  {
    var added = 0;

    foreach( var language in locales.Languages )
    {
      if( !locales.Global.TryGetValue( language.Key, out var lazyLoad ) )
      {
        continue;
      }

      // A transformer, not a direct write: the locale data is lazily loaded, so anything written
      // now would be replaced when the real file is read. This is the same mechanism
      // CustomItemService uses to register an item's own name.
      lazyLoad.AddTransformer( localeData =>
      {
        // Runs inside SPT's lazy read, where a throw surfaces far from its cause.
        if( localeData is null )
          return localeData;

        // The quad's slots are every COTI slot there is.
        foreach( var slotName in CotiTubes.SlotNames( CotiLayouts.Quad ) )
          localeData[slotName] = CotiTubes.SlotDisplayName( slotName, null );

        return localeData;
      } );

      added++;
    }

    logger.Success( $"[COTI] Slot names registered in {added} locale(s)" );

    return Task.CompletedTask;
  }
}
