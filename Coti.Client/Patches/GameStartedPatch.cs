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
        // Patches are only ever Enabled from the non-headless branch of Plugin.Awake, so this
        // can never actually run on the headless - guarded anyway, matching Update()'s own
        // belt-and-braces IsHeadless check, since a per-tick NRE on that box is not cosmetic.
        if( Plugin.IsHeadless )
          return;

        CotiState.ResetPerRaidLogging();
      } );
    }
  }
}
