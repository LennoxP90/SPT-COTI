using System;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// AmandsGraphics 1.8.0 postfixes TacticalComboVisualController.UpdateBeams and reads the controller's lights
  /// through the field name "light_0", which SPT 4.x renamed to "_lights". The lookup returns null and the postfix
  /// throws a NullReferenceException on every light-state change of a device on the local player's weapon. The
  /// throw leaves Firearms.UpdateBeams half done, so the thermal riser's LED, or a flashlight after it, keeps its
  /// old look.
  ///
  /// This finalizer on that postfix swallows the NullReferenceException only. Amands' flashlight tuning stays as
  /// inert as it already is on 4.1, and a later Amands that fixes the name needs nothing from here.
  /// </summary>
  public class AmandsFlashlightHookPatch : ModulePatch
  {
    internal const string HookType = "AmandsGraphics.Patches.AmandsGraphicsTacticalComboVisualControllerPatch";

    private static bool _reported;

    internal static MethodBase Target()
    {
      return AccessTools.Method( AccessTools.TypeByName( HookType ), "PatchPostFix" );
    }

    protected override MethodBase GetTargetMethod()
    {
      return Target();
    }

    [PatchFinalizer]
    private static Exception Finalizer( Exception __exception )
    {
      if( !( __exception is NullReferenceException ) )
        return __exception;

      if( !_reported )
      {
        _reported = true;
        Plugin.Log.LogWarning( "[COTI] AmandsGraphics' flashlight hook threw a NullReferenceException; contained so the weapon's devices still update" );
      }

      return null;
    }
  }
}
