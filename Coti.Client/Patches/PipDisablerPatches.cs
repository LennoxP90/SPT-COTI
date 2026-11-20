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

  /// <summary>
  /// DynamicExternalResolution switches the game's upscaler to its own mode while a scope is aimed, and PiP-Disabler lets
  /// it do so for every scope it leaves alone, which since <see cref="PipLeavesOurScopesPatch"/> includes ours. With
  /// DLSS or FSR on, each switch restarts the upscaler and the whole picture goes black for a couple of frames, on the way
  /// in and again on the way out, and the lower mode smears the heat outlines. A scope on a ring or riser keeps the
  /// player's own setting, as PiP-Disabler's own scopes do.
  /// </summary>
  public class DerpLeavesOurScopesPatch : ModulePatch
  {
    internal static MethodBase Target()
    {
      var patches = AccessTools.TypeByName( "DynamicExternalResolution.DynamicExternalResolutionPatches" );
      return patches != null ? AccessTools.Method( patches, "SetResolutionAim" ) : null;
    }

    protected override MethodBase GetTargetMethod()
    {
      return Target();
    }

    [PatchPrefix]
    private static bool Prefix()
    {
      return !ThermalRiserState.AimedThrough;
    }
  }
}
