// The 4.0 and 4.1 servers expose the same types from different namespaces, and renamed the two
// load-order constants this mod uses. Aliasing them here keeps every other file free of #if.
// The table types are a fourth case: 4.1 injects them directly, 4.0 has no such types at all
// (everything hangs off DatabaseServer.GetTables()) - so only the field TYPE is aliased here;
// the constructors that populate it still differ per version at the call site.
//
// ProfileHelper is a fifth case, added for CotiDeviceRoutes: same class name, same members
// (GetPmcProfile(MongoId) returning PmcData?), but 4.0 declares it directly under
// SPTarkov.Server.Core.Helpers (already aliased above for ModHelper, so 4.0 needs nothing extra)
// while 4.1 moved it one level deeper, to SPTarkov.Server.Core.Helpers.Profile.
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
    // 4.0 has no Preload. It is NOT PreSptModLoader, despite both sitting at the numeric
    // value 100000 - in 4.0 DatabaseImporter runs INSIDE the OnLoad pipeline at
    // OnLoadOrder.Database (200000), so anything at 100000 runs before the database is
    // imported. 4.1's Preload has no such import step ahead of it; the database is already
    // loaded there. The role match is PostDBModLoader - after the database, same as 4.1's
    // Preload. Each version's own TestMod confirms this: 4.0 registers at
    // PostDBModLoader + 1, 4.1 at Preload + 1. CotiItemFactory clones a donor template out
    // of the database, so getting this wrong compiles clean, loads without error, and
    // silently never registers the item.
    public const int Preload = SPTarkov.Server.Core.DI.OnLoadOrder.PostDBModLoader;
    // 4.0 has no PostLoad; PostSptModLoader is the last stage (1100000), same role as 4.1's
    // PostLoad.
    public const int PostLoad = SPTarkov.Server.Core.DI.OnLoadOrder.PostSptModLoader;
#else
    public const int Preload = SPTarkov.Server.Core.DI.OnLoadOrder.Preload;
    public const int PostLoad = SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad;
#endif
}
