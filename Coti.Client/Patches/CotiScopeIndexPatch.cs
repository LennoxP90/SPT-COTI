using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// The item factory receives every template in its constructor, before any scope can be shown: the one moment the
  /// magnified scope index is built. Singleton.Instantiated is still false here, so the instance is read directly.
  /// </summary>
  public class CotiScopeIndexPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.GetDeclaredConstructors( typeof( ItemFactory ) ).Single( c => c.GetParameters().Length == 1 );
    }

    [PatchPostfix]
    private static void Postfix( ItemFactory __instance )
    {
      CotiPatchGuard.Run( nameof( CotiScopeIndexPatch ), () => CotiScopeIndex.Build( __instance.ItemTemplates.Select( pair => pair.Value ) ) );
    }
  }
}
