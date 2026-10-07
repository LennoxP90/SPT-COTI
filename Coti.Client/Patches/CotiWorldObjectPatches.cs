using System.Reflection;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  // The thermal world's objects register as they wake, so it never scans the scene for them. Prefixes: an Awake that
  // throws leaves its object alive, and it must still be known.

  internal class CotiHotObjectAwakePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( HotObject ), nameof( HotObject.Awake ) );
    }

    [PatchPrefix]
    private static void Prefix( HotObject __instance )
    {
      CotiThermalWorld.Register( __instance );
    }
  }

  internal class CotiDeviceAwakePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( TacticalComboVisualController ), nameof( TacticalComboVisualController.Awake ) );
    }

    [PatchPrefix]
    private static void Prefix( TacticalComboVisualController __instance )
    {
      CotiThermalWorld.Register( __instance );
    }
  }

  internal class CotiLampAwakePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( LampController ), nameof( LampController.Awake ) );
    }

    [PatchPrefix]
    private static void Prefix( LampController __instance )
    {
      CotiThermalWorld.Register( __instance );
    }
  }

  // FlameDamageTrigger has no Awake of its own; its base's runs for every damage trigger.
  internal class CotiFireAwakePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( DamageTrigger ), nameof( DamageTrigger.Awake ) );
    }

    [PatchPrefix]
    private static void Prefix( DamageTrigger __instance )
    {
      if( __instance is FlameDamageTrigger fire )
        CotiThermalWorld.Register( fire );
    }
  }
}
