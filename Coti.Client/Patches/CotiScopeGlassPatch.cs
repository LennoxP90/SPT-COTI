using System.Reflection;
using Diz.Resources;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Every bundle load, vanilla or a mod's (SPT builds a plain EasyBundle for those too), starts LoadingCoroutine once.
  /// The bundle is recorded here; a scope bundle is tagged from its own LoadState, synchronously as it turns Loaded, so
  /// no instance can be made from the prefab before its glass is tagged. The load itself is left untouched.
  /// </summary>
  public class CotiScopeGlassPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( EasyBundle ), "LoadingCoroutine" );
    }

    [PatchPostfix]
    private static void Postfix( EasyBundle __instance )
    {
      CotiPatchGuard.Run( nameof( CotiScopeGlassPatch ), () => CotiScopeIndex.OnBundleLoading( __instance ) );
    }
  }
}
