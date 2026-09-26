using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  internal class GameStartedPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( GameWorld ), nameof( GameWorld.OnGameStarted ) );
    }

    [PatchPostfix]
    private static void Postfix()
    {
      CotiPatchGuard.Run( "GameStartedPatch", () =>
      {
        // Patches are only enabled from the non-headless branch of Plugin.Awake. Guarded anyway,
        // like Update()'s IsHeadless check, since a per-tick NRE on the headless is costly.
        if( Plugin.IsHeadless )
          return;

        CotiState.ResetPerRaidLogging();
      } );
    }
  }
}
