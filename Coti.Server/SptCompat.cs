// The 4.0 and 4.1 servers expose the same types from different namespaces, and renamed the two
// load-order constants this mod uses. Aliasing them here keeps every other file free of #if.
// The table types: 4.1 injects them directly, while 4.0 reaches them through
// DatabaseServer.GetTables(), so only the field type is aliased here and the constructors that
// populate it still differ per version at the call site.
//
// ProfileHelper has the same name and members on both, but lives in SPTarkov.Server.Core.Helpers
// on 4.0 and SPTarkov.Server.Core.Helpers.Profile on 4.1.
#if SPT40
global using SPTarkov.Server.Core.Models.Utils;           // ISptLogger
global using SPTarkov.Server.Core.Helpers;                // ModHelper, ProfileHelper
global using SPTarkov.Server.Core.Services.Mod;           // CustomItemService
global using SPTarkov.Server.Core.Servers;                // DatabaseServer
global using CotiTemplateTable = SPTarkov.Server.Core.Models.Spt.Templates.Templates;
global using CotiLocationTable = SPTarkov.Server.Core.Models.Spt.Server.Locations;
global using CotiLocaleTable   = SPTarkov.Server.Core.Models.Spt.Server.LocaleBase;
#else
global using SPTarkov.Common.Models.Logging;              // ISptLogger
global using SPTarkov.Server.Core.Helpers.Server;         // ModHelper
global using SPTarkov.Server.Core.Helpers.Profile;        // ProfileHelper
global using SPTarkov.Server.Core.Services.Modding.Custom; // CustomItemService
global using CotiTemplateTable = SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable;
global using CotiLocationTable = SPTarkov.Server.Core.Models.Spt.Tables.LocationTable;
global using CotiLocaleTable   = SPTarkov.Server.Core.Models.Spt.Tables.LocaleTable;
#endif

namespace Coti.Server;

/// <summary>
/// OnLoadOrder's members were renamed between 4.0 and 4.1. The numeric values are what the server
/// actually sorts on, so these map to the nearest equivalent stage rather than the nearest name.
/// </summary>
public static class CotiLoadOrder
{
#if SPT40
    // 4.0 has no Preload. PreSptModLoader shares its value (100000) but runs before 4.0's
    // DatabaseImporter (OnLoadOrder.Database, 200000). PostDBModLoader is the equivalent stage:
    // after the database is loaded, as 4.1's Preload is. CotiItemFactory clones a donor template
    // out of the database, so an earlier stage would silently never register the item.
    public const int Preload = SPTarkov.Server.Core.DI.OnLoadOrder.PostDBModLoader;
    // 4.0 has no PostLoad; PostSptModLoader is the last stage (1100000), same role as 4.1's
    // PostLoad.
    public const int PostLoad = SPTarkov.Server.Core.DI.OnLoadOrder.PostSptModLoader;
#else
    public const int Preload = SPTarkov.Server.Core.DI.OnLoadOrder.Preload;
    public const int PostLoad = SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad;
#endif

    // Same member in both versions, so no #if. Precedes OnLoadOrder.RagfairCallbacks, where
    // RagfairServer.Load builds every flea offer.
    public const int TraderRegistration = SPTarkov.Server.Core.DI.OnLoadOrder.TraderRegistration;
}
