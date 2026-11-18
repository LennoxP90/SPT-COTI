using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  /// <summary>
  /// Keeps the VolumetricLightRenderer on each thermal camera clone away from the game's shared volumetric state.
  ///
  /// The clone keeps the component only because ThermalVision reads and writes its IsOn. Left to run, it breaks every
  /// scope: Awake empties the static light list, which a light only rejoins when it is next enabled. An optic instance
  /// also shares the static optic buffers with the game's scope camera. Its first render rebuilds those buffers and
  /// OnDestroy frees them, and either way the scope camera keeps the dead ones. That gives a NullReferenceException in
  /// InitPostRenderPass every frame, or a black scope while its volumetric pass is off, until the game restarts.
  ///
  /// Awake and OnDestroy are skipped for a renderer created while <see cref="Building"/> is set, and
  /// <see cref="CotiThermalRig"/> disables it, so OnPreRender never runs either.
  /// </summary>
  internal static class CloneVolumetric
  {
    private static readonly HashSet<VolumetricLightRenderer> Inert = new HashSet<VolumetricLightRenderer>();

    /// <summary>Set by CotiThermalRig around the Instantiate or AddComponent that creates a clone's renderer.</summary>
    internal static bool Building;

    internal static bool Claim( VolumetricLightRenderer renderer )
    {
      return Building && Inert.Add( renderer );
    }

    internal static bool Release( VolumetricLightRenderer renderer )
    {
      return Inert.Remove( renderer );
    }
  }

  public class CloneVolumetricAwakePatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( VolumetricLightRenderer ), "Awake" );
    }

    [PatchPrefix]
    private static bool Prefix( VolumetricLightRenderer __instance )
    {
      return CotiPatchGuard.Run( nameof( CloneVolumetricAwakePatch ), () => !CloneVolumetric.Claim( __instance ), true );
    }
  }

  public class CloneVolumetricDestroyPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( VolumetricLightRenderer ), "OnDestroy" );
    }

    [PatchPrefix]
    private static bool Prefix( VolumetricLightRenderer __instance )
    {
      return CotiPatchGuard.Run( nameof( CloneVolumetricDestroyPatch ), () => !CloneVolumetric.Release( __instance ), true );
    }
  }
}
