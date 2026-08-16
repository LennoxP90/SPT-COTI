using Coti.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Coti.Server;

[Injectable( TypePriority = OnLoadOrder.Preload )]
public class CotiItemFactory(
    ISptLogger<CotiItemFactory> logger,
    CustomItemService customItemService ) : IOnLoad
{
  public const string CotiTplId = CotiIds.TplId;

  /// <summary>
  /// mount_all_custom_top_rail_13, a vanilla 1x1 Mount-class item.
  /// </summary>
  private const string DonorTplId = "55d48a634bdc2d8b2f8b456a";

  /// <summary>
  /// The Mount item class node - the donor's own parent.
  /// </summary>
  private const string MountItemClassId = "55818b224bdc2dde698b456f";

  /// <summary>
  /// The night-vision handbook category, not the donor's inherited Mounts one. Filing it under
  /// Mounts is what made Peacekeeper refuse to buy it back.
  /// </summary>
  public const string HandbookParentId = "5b5f749986f774094242f199";

  /// <summary>
  /// The device's 3D model as an SPT bundle key. Must match bundles.json and the AssetBundle's
  /// name in the Unity build exactly; a mismatch silently keeps the donor's model with no error
  /// either side.
  /// </summary>
  public const string ModelBundleKey = "coti/nvg_coti_clip_on_thermal.bundle";

  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    var result = customItemService.CreateItemFromClone( new NewItemFromCloneDetails
    {
      ItemTplToClone = new MongoId( DonorTplId ),
      NewId = new MongoId( CotiTplId ),
      ParentId = new MongoId( MountItemClassId ),
      NewItemName = "anpas29b_coti_clip_on_thermal_imager",
      HandbookParentId = HandbookParentId,
      HandbookPriceRoubles = 250000,
      FleaPriceRoubles = 250000,
      AddToHandbook = true,
      AddToFleaPriceDb = true,
      AddToWeaponShelf = false,
      OverrideProperties = new TemplateItemProperties
      {
        Name = "AN/PAS-29B ECOTI enhanced clip-on thermal imager",
        ShortName = "ECOTI",
        Description = "Enhanced clip-on thermal imager by Safran Defense & Space (Optics 1). Uncooled LWIR microbolometer, 640x480 at 17 um pixel pitch, 8-12 um sensitivity, 1x optical unity magnification, 30 degree circular field of view at f/1.15. Clips to the objective of a PVS-14 style night vision device and injects an outline-mode thermal overlay into the tube. Runs 3.5 hours on a single CR123A.",
        Weight = 0.108,

        // Explicit, not inherited. The donor is a rail mount and carries -1, which showed up
        // on the COTI as an ergonomics penalty it had no reason to have. Set deliberately to
        // 0 - if the device should cost handling, that is a balance decision to make on
        // purpose rather than a leftover of which item happened to be cloned.
        Ergonomics = 0,

        // The donor is RaidModdable false / ToolModdable true, so the clip-on inherited "needs
        // a multitool and cannot come off in raid". A clip-on is meant to come on and off.
        RaidModdable = true,
        ToolModdable = false,

        // The donor is a common rail mount, and its rarity, XP and handling sound came with it.
        RarityPvE = "Superrare",
        ExamineExperience = 10,
        LootExperience = 15,
        ItemSound = "gear_goggles",

        // No slots, so nothing to merge with.
        MergesWithChildren = false,

        CanSellOnRagfair = false,
        Width = 1,
        Height = 1,
        // Empty, not null: CustomItemService only overwrites a property when the override is
        // non-null, so null here would keep the donor's mod_scope slot and let a player
        // attach a scope to the thermal imager.
        Slots = new List<Slot>(),

        Prefab = new Prefab { Path = ModelBundleKey, Rcid = "" }
      },
      Locales = new Dictionary<string, LocaleDetails>
      {
        ["en"] = new LocaleDetails
        {
          Name = "AN/PAS-29B ECOTI enhanced clip-on thermal imager",
          ShortName = "ECOTI",
          Description = "Enhanced clip-on thermal imager by Safran Defense & Space (Optics 1). Uncooled LWIR microbolometer, 640x480 at 17 um pixel pitch, 8-12 um sensitivity, 1x optical unity magnification, 30 degree circular field of view at f/1.15. Clips to the objective of a PVS-14 style night vision device and injects an outline-mode thermal overlay into the tube. Runs 3.5 hours on a single CR123A."
        }
      }
    } );

    if( !result.Success )
    {
      // The slot filter, the assort and the loot entries all point at this id.
      logger.Error(
          $"[COTI] Item {CotiTplId} was NOT created: {string.Join( "; ", result.Errors ?? [] )}. " +
          "The slot, trader offer and loot entries that follow will reference a missing template." );

      return Task.CompletedTask;
    }

    logger.Success( $"[COTI] Item created, id {CotiTplId}" );

    return Task.CompletedTask;
  }
}
