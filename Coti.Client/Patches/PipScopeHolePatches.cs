using System.Collections.Generic;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>PiP-Disabler's mesh cut, skipped for a sight on a COTI device while CotiScopeHole can cut its view on the GPU.</summary>
  public class PipSkipMeshCutPatch : ModulePatch
  {
    internal static MethodBase Target()
    {
      var manager = AccessTools.TypeByName( "PiPDisabler.MeshSurgeryManager" );
      return manager != null ? AccessTools.Method( manager, "ApplyForOptic" ) : null;
    }

    protected override MethodBase GetTargetMethod()
    {
      return Target();
    }

    [PatchPrefix]
    private static bool Prefix( OpticSight os )
    {
      return os == null || !CotiScopeHole.Takes( os.transform );
    }
  }

  /// <summary>
  /// PiP-Disabler hides its reticle wherever the scope's housing covers the screen, a mask drawn from the housing's meshes.
  /// Left uncut (PipSkipMeshCutPatch), the housing would hide the reticle inside the eyepiece, so these sights get none.
  /// </summary>
  public class PipNoReticleOccludersPatch : ModulePatch
  {
    internal static MethodBase Target()
    {
      var reticle = AccessTools.TypeByName( "PiPDisabler.ReticleRenderer" );
      return reticle != null ? AccessTools.Method( reticle, "SetOccluderMaskRenderers" ) : null;
    }

    protected override MethodBase GetTargetMethod()
    {
      return Target();
    }

    [PatchPrefix]
    private static void Prefix( List<Renderer> renderers )
    {
      if( renderers != null && renderers.Count > 0 && renderers[0] != null && CotiScopeHole.Takes( renderers[0].transform ) )
        renderers.Clear();
    }
  }
}
