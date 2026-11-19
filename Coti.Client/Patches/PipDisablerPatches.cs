using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// PiP-Disabler leaves a scope alone when its own bypass check says so (thermal sights, its blacklist), and then puts the
  /// game's optic camera, lens and zoom back itself. A scope on a COTI ring or riser joins that list, powered or not, so
  /// it is always drawn by the game's own optic camera and COTI heats it the way it does without PiP-Disabler.
  /// </summary>
  public class PipLeavesOurScopesPatch : ModulePatch
  {
    internal static MethodBase Target()
    {
      var lifecycle = AccessTools.TypeByName( "PiPDisabler.ScopeLifecycle" );
      return lifecycle != null ? AccessTools.Method( lifecycle, "ShouldBypassForCurrentOptic", new[] { typeof( OpticSight ) } ) : null;
    }

    protected override MethodBase GetTargetMethod()
    {
      return Target();
    }

    [PatchPostfix]
    private static void Postfix( OpticSight os, ref bool __result )
    {
      if( !__result && os != null && ThermalRiserState.IsOnRiser( os.transform ) )
        __result = true;
    }
  }
}
