using System.Reflection;
using Diz.Resources;
using HarmonyLib;
using SPT.Reflection.Patching;
#if SPT40
using EasyBundle = EasyAssetHelperClass;
#endif

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
      #if SPT40
      // 4.0 has no LoadingCoroutine; Load sets Loading and starts the load the same way.
      return AccessTools.Method( typeof( EasyBundle ), "Load" );
#else
      return AccessTools.Method( typeof( EasyBundle ), "LoadingCoroutine" );
#endif
    }

    [PatchPostfix]
    private static void Postfix( EasyBundle __instance )
    {
      CotiPatchGuard.Run( nameof( CotiScopeGlassPatch ), () => CotiScopeIndex.OnBundleLoading( __instance ) );
    }
  }
}
