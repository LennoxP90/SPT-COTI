using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Coti.Client.Patches
{
  internal class ThermalParametersPatch : ModulePatch
  {
    private static bool _loggedError;
    private static bool _loggedSnapshotError;
    private static bool _loggedRestoreError;
    private static bool _loggedUnknownPalette;

    private static bool _snapshotTaken;
    private static bool _wasActive;

    private static float _snapMinimumTemperatureValue;
    private static float _snapMainTexColorCoef;
    private static float _snapDepthFade;
    private static bool _snapIsPixelated;
    private static bool _snapIsNoisy;
    private static bool _snapIsMotionBlurred;
    private static float _snapUnsharpRadiusBlur;
    private static float _snapUnsharpBias;
    private static ThermalVisionComponent.SelectablePalette _snapCurrentRampPalette;
    private static float _snapRampShift;

    protected override MethodBase GetTargetMethod()
    {
      return AccessTools.Method( typeof( ThermalVision ), nameof( ThermalVision.SetMaterialProperties ) );
    }

    [PatchPostfix]
    private static void Postfix( ThermalVision __instance )
    {
      // Runs for every ThermalVision in the game. Unfiltered, a thermal weapon optic takes the
      // clip-on's tuning and keeps it - _wasActive is one flag, so only one instance ever restores.
      if( !CotiThermalCamera.Owns( __instance ) )
        return;

      try
      {
        if( CotiState.Active )
        {
          ApplyCotiValues( __instance );
          _wasActive = true;
          return;
        }

        if( _wasActive && _snapshotTaken )
        {
          RestorePristineValues( __instance );
        }
        _wasActive = false;
      }
      catch( Exception ex )
      {
        if( !_loggedError )
        {
          Plugin.Log.LogError( $"[COTI] Failed to apply thermal parameters, host values will not apply: {ex}" );
          _loggedError = true;
        }
      }
    }

    private static void ApplyCotiValues( ThermalVision instance )
    {
      if( CotiState.Host == null )
        return;

      var utils = instance.ThermalVisionUtilities;
      if( utils?.ValuesCoefs == null )
        return;

      // Captured at most once for the whole session, guarded by _snapshotTaken - the
      // snapshot is the game's own pristine defaults, which do not change between raids, so
      // there is nothing to gain from re-capturing on a later COTI activation, and
      // re-capturing while already active would just save our own values over themselves.
      if( !_snapshotTaken )
      {
        try
        {
          _snapMinimumTemperatureValue = utils.ValuesCoefs.MinimumTemperatureValue;
          _snapMainTexColorCoef = utils.ValuesCoefs.MainTexColorCoef;
          _snapDepthFade = utils.DepthFade;
          _snapIsPixelated = instance.IsPixelated;
          _snapIsNoisy = instance.IsNoisy;
          _snapIsMotionBlurred = instance.IsMotionBlurred;
          _snapUnsharpRadiusBlur = instance.UnsharpRadiusBlur;
          _snapUnsharpBias = instance.UnsharpBias;
          _snapCurrentRampPalette = utils.CurrentRampPalette;
          _snapRampShift = utils.ValuesCoefs.RampShift;
          _snapshotTaken = true;
        }
        catch( Exception ex )
        {
          if( !_loggedSnapshotError )
          {
            Plugin.Log.LogError(
                $"[COTI] Failed to snapshot pristine thermal parameters, COTI thermal tuning will not apply: {ex}" );
            _loggedSnapshotError = true;
          }

          // Nothing to restore later, so do not write COTI values this call either - that
          // would strand a later T-7 on COTI's settings for the rest of the session.
          return;
        }
      }

      var host = CotiState.Host;
      utils.ValuesCoefs.MinimumTemperatureValue = host.MinimumTemperatureValue;
      utils.ValuesCoefs.MainTexColorCoef = host.MainTexColorCoef;
      utils.DepthFade = host.DepthFade;

      instance.IsPixelated = host.IsPixelated;
      instance.IsNoisy = host.IsNoisy;
      instance.IsMotionBlurred = host.IsMotionBlurred;

      // BSG's own unsharp-mask fields: original + (original - blurred) * bias, inside its
      // thermal shader. This is the mechanism that makes the composite's masked overlay
      // (see ThermalVisionOnPreCullPatch) read as edge-dominated contours rather than a
      // filled thermal image - there is no separate edge-detect shader in this project.
      instance.UnsharpRadiusBlur = host.UnsharpRadiusBlur;
      instance.UnsharpBias = host.UnsharpBias;

      ApplyPalette( utils, host );

      // RampShift just shifts where the ramp above is sampled - unlike the palette itself,
      // there is no failure mode where a given float value produces a null texture, so this
      // is applied unconditionally (0 is the vanilla no-op default).
      utils.ValuesCoefs.RampShift = host.RampShift;
    }

    /// <summary>
    /// Applies <see cref="CotiNvgHostConfig.Palette"/> to
    /// <c>ThermalVisionUtilities.CurrentRampPalette</c> - the ramp is what maps heat to colour,
    /// so this is the actual control over whether the image reads as a uniformly bright disc or
    /// dark-with-hot-highlights (see CotiNvgHostConfig.Palette for the full rationale).
    ///
    /// <see cref="ThermalVision.GetRampTexture"/> walks
    /// <c>ThermalVisionUtilities.RampTexPalletteConnectors</c> and returns null if none of them
    /// match <c>CurrentRampPalette</c> - a null ramp texture handed to the material could break
    /// the thermal image entirely. So a connector for the requested palette must be confirmed to
    /// exist BEFORE writing CurrentRampPalette, never after. "" / null means "leave the game's
    /// current palette untouched" and is not an error - that is the correct behaviour for a host
    /// entry that has not opted into a specific palette. An unrecognised string and a
    /// recognised-but-connector-less palette both get the same treatment: log once (naming the
    /// requested value and the ones actually available) and leave the palette unchanged.
    /// </summary>
    private static void ApplyPalette( ThermalVisionUtilities utils, CotiNvgHostConfig host )
    {
      var requested = host.Palette;
      if( string.IsNullOrEmpty( requested ) )
        return;

      ThermalVisionComponent.SelectablePalette palette;
      if( !Enum.TryParse( requested, ignoreCase: true, out palette ) || !PaletteConnectorExists( utils, palette ) )
      {
        LogUnknownPaletteOnce( requested, utils );
        return;
      }

      utils.CurrentRampPalette = palette;
    }

    private static bool PaletteConnectorExists(
        ThermalVisionUtilities utils, ThermalVisionComponent.SelectablePalette palette )
    {
      var connectors = utils.RampTexPalletteConnectors;
      if( connectors == null )
        return false;

      for( var i = 0; i < connectors.Count; i++ )
      {
        if( connectors[i] != null && connectors[i].SelectablePalette == palette )
          return true;
      }
      return false;
    }

    private static void LogUnknownPaletteOnce( string requested, ThermalVisionUtilities utils )
    {
      if( _loggedUnknownPalette )
        return;
      _loggedUnknownPalette = true;

      var available = new List<string>();
      var connectors = utils.RampTexPalletteConnectors;
      if( connectors != null )
      {
        foreach( var connector in connectors )
        {
          if( connector == null )
            continue;
          var name = connector.SelectablePalette.ToString();
          if( !available.Contains( name ) )
            available.Add( name );
        }
      }

      Plugin.Log.LogWarning(
          $"[COTI] Unknown or unavailable palette '{requested}' - available: " +
          $"[{string.Join( ", ", available )}] - leaving current palette unchanged" );
    }

    private static void RestorePristineValues( ThermalVision instance )
    {
      try
      {
        var utils = instance.ThermalVisionUtilities;
        if( utils?.ValuesCoefs == null )
        {
          if( !_loggedRestoreError )
          {
            Plugin.Log.LogWarning(
                "[COTI] Cannot restore pristine thermal parameters - ThermalVisionUtilities/ValuesCoefs is null" );
            _loggedRestoreError = true;
          }
          return;
        }

        utils.ValuesCoefs.MinimumTemperatureValue = _snapMinimumTemperatureValue;
        utils.ValuesCoefs.MainTexColorCoef = _snapMainTexColorCoef;
        utils.DepthFade = _snapDepthFade;

        instance.IsPixelated = _snapIsPixelated;
        instance.IsNoisy = _snapIsNoisy;
        instance.IsMotionBlurred = _snapIsMotionBlurred;

        instance.UnsharpRadiusBlur = _snapUnsharpRadiusBlur;
        instance.UnsharpBias = _snapUnsharpBias;

        utils.CurrentRampPalette = _snapCurrentRampPalette;
        utils.ValuesCoefs.RampShift = _snapRampShift;
      }
      catch( Exception ex )
      {
        if( !_loggedRestoreError )
        {
          Plugin.Log.LogError( $"[COTI] Failed to restore pristine thermal parameters: {ex}" );
          _loggedRestoreError = true;
        }
      }
    }
  }
}
