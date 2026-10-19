using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// A bot takes the first light on its weapon as its flashlight (BotLight.FindLight, on every weapon change). The
  /// thermal riser is a tactical device, so a bot whose riser comes before its light would switch the riser's LED
  /// instead. After the search, a riser is swapped for the first light that is not one, or for no light at all.
  ///
  /// Enabled on the Fika headless too, which runs the bots in co-op.
  /// </summary>
  public class BotRiserLightPatch : ModulePatch
  {
    private static readonly AccessTools.FieldRef<BotLight, LightComponent> LightMod =
      AccessTools.FieldRefAccess<BotLight, LightComponent>( "_lightMod" );
    private static readonly AccessTools.FieldRef<BotLight, bool> HaveLight =
      AccessTools.FieldRefAccess<BotLight, bool>( "_haveLight" );
    private static readonly AccessTools.FieldRef<BotData, BotOwner> Owner =
      AccessTools.FieldRefAccess<BotData, BotOwner>( "_owner" );

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( BotLight ), nameof( BotLight.FindLight ) );
    }

    [PatchPostfix]
    private static void Postfix( BotLight __instance )
    {
      CotiPatchGuard.Run( "BotRiserLightPatch", () => SkipRiser( __instance ) );
    }

    private static void SkipRiser( BotLight botLight )
    {
      var chosen = LightMod( botLight );
      if( chosen?.Item == null || !ThermalRiserState.IsRiser( chosen.Item.TemplateId ) )
        return;

      LightComponent other = null;
      var weapon = Owner( botLight )?.WeaponManager?.CurrentWeapon;
      if( weapon != null )
      {
        foreach( var mod in weapon.Mods )
        {
          var light = mod.GetItemComponent<LightComponent>();
          if( light != null && !ThermalRiserState.IsRiser( mod.TemplateId ) )
          {
            other = light;
            break;
          }
        }
      }

      LightMod( botLight ) = other;
      HaveLight( botLight ) = other != null;
    }
  }
}
