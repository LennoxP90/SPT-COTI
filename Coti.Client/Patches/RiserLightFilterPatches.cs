using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Coti.Shared;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client.Patches
{
  /// <summary>
  /// The thermal riser is a tactical device so its LED rides the game's light-state sync, but only the COTI
  /// power key may switch it. These patches keep it out of T, Ctrl+T and the hold-mode release.
  /// </summary>
  internal static class RiserLights
  {
    internal static bool IsRiser( Item weapon, string lightItemId )
    {
      if( weapon == null )
        return false;

      foreach( var item in weapon.GetAllItems() )
      {
        if( item.Id == lightItemId )
          return ThermalRiserState.IsRiser( item.TemplateId );
      }

      return false;
    }

    internal static LightsState[] WithoutRiser( Item weapon, LightsState[] states )
    {
      if( states == null || states.Length == 0 )
        return states;

      return states.Where( s => !IsRiser( weapon, s.Id ) ).ToArray();
    }

    internal static bool OtherLightOn( Item weapon )
    {
      if( weapon == null )
        return false;

      foreach( var item in weapon.GetAllItems() )
      {
        var light = item.GetItemComponent<LightComponent>();
        if( light != null && light.IsActive && !ThermalRiserState.IsRiser( item.TemplateId ) )
          return true;
      }

      return false;
    }
  }

  /// <summary>
  /// T, Ctrl+T and the release-tactical command all read the weapon's lights from GetCurrentLightStates, so
  /// dropping the riser there covers all three.
  /// </summary>
  public class RiserTacticalKeysPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( FirearmHandsInputTranslator ), "GetCurrentLightStates" );
    }

    [PatchPostfix]
    private static void Postfix( FirearmHandsInputTranslator __instance, ref LightsState[] __result )
    {
      var states = __result;
      CotiPatchGuard.Run( "RiserTacticalKeysPatch", () => states = RiserLights.WithoutRiser( __instance._controller?.Item, states ) );
      __result = states;
    }
  }

  /// <summary>
  /// GamePlayerOwner.ReleaseTactical builds its own array of every light and switches them all off. It runs
  /// on a hold-mode key release, a weapon swap, focus loss and quitting. It is skipped when the riser is the
  /// only light on, since it would otherwise play the switch sound for nothing; else the call is marked so
  /// RiserReleaseFilterPatch can strip the riser from the SetLightsState it makes.
  /// </summary>
  public class RiserReleaseAllPatch : ModulePatch
  {
    [ThreadStatic]
    internal static bool Releasing;

    private static readonly AccessTools.FieldRef<GamePlayerOwner, IFirearmHandsController> Hands =
      AccessTools.FieldRefAccess<GamePlayerOwner, IFirearmHandsController>( "_firearmHandsController" );

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( GamePlayerOwner ), nameof( GamePlayerOwner.ReleaseTactical ) );
    }

    [PatchPrefix]
    private static bool Prefix( GamePlayerOwner __instance )
    {
      var run = true;
      CotiPatchGuard.Run( "RiserReleaseAllPatch", () => run = RiserLights.OtherLightOn( Hands( __instance )?.Item ) );
      Releasing = run;
      return run;
    }

    [PatchFinalizer]
    private static void Finalizer() => Releasing = false;
  }

  /// <summary>
  /// Strips the riser from SetLightsState while ReleaseTactical runs. Fika's controller overrides the method
  /// and sends its packet before calling the base, so its override is patched as well, or peers would see
  /// the riser switch off while the local player's stays on.
  /// </summary>
  public class RiserReleaseFilterPatch : ModulePatch
  {
    internal const string FikaControllerType = "Fika.Core.Main.ClientClasses.HandsControllers.FikaClientFirearmController";

    private readonly Type _controller;

    public RiserReleaseFilterPatch( Type controller )
    {
      _controller = controller;
    }

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.DeclaredMethod( _controller, "SetLightsState", new[] { typeof( LightsState[] ), typeof( bool ), typeof( bool ) } );
    }

    [PatchPrefix]
    private static void Prefix( Player.FirearmController __instance, ref LightsState[] lightsStates )
    {
      if( !RiserReleaseAllPatch.Releasing )
        return;

      var states = lightsStates;
      CotiPatchGuard.Run( "RiserReleaseFilterPatch", () => states = RiserLights.WithoutRiser( __instance.Item, states ) );
      lightsStates = states;
    }
  }

  /// <summary>
  /// A weapon leaving the hands, by holster, swap or drop, goes through its controller's Drop. A riser shutting down on
  /// it ends its shutdown there and goes dark (ThermalRiserState.LeavingHands), while the controller still holds it, so the
  /// LED never stays lit on a weapon nobody holds. Fika's client controller does not override Drop.
  /// </summary>
  public class RiserDropPatch : ModulePatch
  {
    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.DeclaredMethod( typeof( Player.FirearmController ), nameof( Player.FirearmController.Drop ),
          new[] { typeof( float ), typeof( Action ), typeof( bool ), typeof( Item ) } );
    }

    [PatchPrefix]
    private static void Prefix( Player.FirearmController __instance )
    {
      CotiPatchGuard.Run( "RiserDropPatch", () => ThermalRiserState.LeavingHands( __instance ) );
    }
  }

  /// <summary>
  /// TacticalComboVisualController collects every mode_ node, Light and LaserBeam below it. On the riser that
  /// would take in a sight's own, so those under the riser's mod_scope are dropped.
  ///
  /// Init normally runs in Awake, before a sight is attached, so this mostly finds nothing to drop; a sight
  /// attached after Init is not filtered.
  /// </summary>
  public class RiserComboInitPatch : ModulePatch
  {
    private static readonly AccessTools.FieldRef<TacticalComboVisualController, List<Transform>> Beams =
      AccessTools.FieldRefAccess<TacticalComboVisualController, List<Transform>>( "_ligthbeamsTransforms" );
    private static readonly AccessTools.FieldRef<TacticalComboVisualController, Transform> Disabled =
      AccessTools.FieldRefAccess<TacticalComboVisualController, Transform>( "_disabledTransform" );
    private static readonly AccessTools.FieldRef<TacticalComboVisualController, Light[]> Lights =
      AccessTools.FieldRefAccess<TacticalComboVisualController, Light[]>( "_lights" );
    private static readonly AccessTools.FieldRef<TacticalComboVisualController, LaserBeam[]> Lasers =
      AccessTools.FieldRefAccess<TacticalComboVisualController, LaserBeam[]>( "_laserBeams" );

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( TacticalComboVisualController ), nameof( TacticalComboVisualController.Init ) );
    }

    [PatchPostfix]
    private static void Postfix( TacticalComboVisualController __instance )
    {
      CotiPatchGuard.Run( "RiserComboInitPatch", () => DropSightParts( __instance ) );
    }

    private static void DropSightParts( TacticalComboVisualController combo )
    {
      var scope = RiserScopeNode( combo.transform );
      if( scope == null )
        return;

      bool UnderScope( Component c ) => c != null && c.transform.IsChildOf( scope );

      Beams( combo ).RemoveAll( t => t.IsChildOf( scope ) );
      Lights( combo ) = Lights( combo )?.Where( l => !UnderScope( l ) ).ToArray();
      Lasers( combo ) = Lasers( combo )?.Where( l => !UnderScope( l ) ).ToArray();

      if( Disabled( combo ) != null && Disabled( combo ).IsChildOf( scope ) )
        Disabled( combo ) = OwnDisabledNode( combo.transform, scope );
    }

    private static Transform RiserScopeNode( Transform root )
    {
      foreach( var node in root.GetComponentsInChildren<Transform>( includeInactive: true ) )
      {
        if( node.name == CotiIds.RiserMeshName )
          return node.parent?.Find( "mod_scope" );
      }

      return null;
    }

    private static Transform OwnDisabledNode( Transform root, Transform scope )
    {
      foreach( var node in root.GetComponentsInChildren<Transform>( includeInactive: true ) )
      {
        if( node.name == TacticalComboVisualController.DISABLED_TRANSFORM_NAME && !node.IsChildOf( scope ) )
          return node;
      }

      return null;
    }
  }
}
