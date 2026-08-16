using Coti.Shared;
using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Coti.Client.Dev;
using Coti.Client.Patches;
using EFT.InventoryLogic;
using UnityEngine;

namespace Coti.Client
{
  [BepInPlugin( "com.lennoxp90.coti", "ECOTI", "1.0.0" )]
  public class Plugin : BaseUnityPlugin
  {
    public static ManualLogSource Log;

    public static new CotiConfig Config = CotiConfig.Fallback;
    public static bool IsHeadless;

    private const string CotiModSlotName = CotiIds.ModSlotName;

    private bool _loggedUpdateError;
    private CotiF12Config _settings;

    private void Awake()
    {
      Log = Logger;

      // Support for fika headless clients, will essentially no-op
      IsHeadless = Chainloader.PluginInfos.ContainsKey( "com.fika.headless" );
      if( IsHeadless )
      {
        Log.LogInfo( "[COTI] Headless client detected - plugin is disabled" );
        return;
      }

      _settings = new CotiF12Config( ( (BaseUnityPlugin)this ).Config );
      Config = _settings.Current;
      CotiPowerToggle.Bind( _settings.PowerToggle );

      new ThermalParametersPatch().Enable();
      new GameStartedPatch().Enable();
      new GoggleToggleSuppressPatch().Enable();

      // Must be enabled unconditionally, not gated on being in a raid: the device has to appear
      // in the inventory and on the character preview in the menu, which is where AttachMods
      // runs most.
      new CotiMountBonePatch().Enable();
      new CotiAttachPatch().Enable();
      new CotiWorldViewPatch().Enable();
      new CotiWorldViewPatch.OnAttachMods().Enable();

      // A real fix, not a diagnostic, so it runs regardless of verboseLogging: EFT's on-disk
      // icon cache holds pictures taken before the device could attach, filed under a hash that
      // already accounts for it, so nothing else will ever invalidate them.
      new CotiIconCacheInvalidator().Enable();

      // Before any inventory UI opens: ModSlotView caches a null against the key the first
      // time it looks and does not retry.
      CotiSlotIcon.Install();

      Log.LogInfo( "[COTI] Initialised" );
    }

    private void OnDestroy()
    {
      CotiThermalCamera.Teardown();

      // Unconditional Detach, NOT Sync(): the config still reports the mode as enabled here, so
      // Sync would re-attach the buffer we are tearing down.
      CotiOverlayCompositor.Detach();

      MaskGenerator.Release();
    }

    private void Update()
    {
      // Support for fika headless clients, will essentially no-op
      if( IsHeadless )
        return;

      try
      {
        // First, deliberately: everything below is raid-oriented and can throw in the menu, which is
        // where the mount is tuned. Compiled out of Release, call included.
        CotiDevTools.Tick();

        // Before state resolution: the toggle feeds into activation.
        CotiPowerToggle.Tick();

        UpdateCotiState();

        // Order matters: CotiState must be resolved first, since the thermal camera reads
        // CotiState.Active and CotiState.Host to decide whether and how to render.
        CotiThermalCamera.Tick();

      }
      catch( Exception ex )
      {
        if( !_loggedUpdateError )
        {
          Log.LogError( $"[COTI] Per-frame state update failed: {ex}" );
          _loggedUpdateError = true;
        }
        CotiState.Active = false;
      }
      finally
      {
        // Always, including after a throw: Sync is the only thing that detaches the overlay
        // buffer, and skipping it leaves the circle drawn with the device inactive.
        CotiOverlayCompositor.Sync();
      }
    }

    private void UpdateCotiState()
    {
      // Read the equipped item straight off the player's own vision observer rather than any
      // inventory search. When a real thermal item (T-7) is worn instead of night vision,
      // NightVisionObserver's Component is null - there is no NightVisionComponent on a
      // thermal-only device - so hostItem, hostTemplateId and cotiAttached all resolve to
      // "nothing equipped" below and CotiState.Active can never become true. This patch set
      // cannot touch ThermalVision while a real thermal item is equipped.
      var nvgComponent = CotiNvgHost.Component;
      var hostItem = nvgComponent?.Item;
      var hostTemplateId = hostItem?.StringTemplateId;
      // OR the switch animation: Togglable.On flips on the FIRST frame of the toggle, while the
      // tube takes most of a second to fade. Without this the COTI would disappear instantly and
      // the fade it now rides (CotiOverlayCompositor.PhosphorFade) would never be seen on the way
      // out - which is the popping the project owner asked to remove.
      var hostNvgOn = ( nvgComponent?.Togglable?.On ?? false ) || CotiOverlayCompositor.TubeSwitching;
      var cotiAttached = IsCotiAttached( hostItem );

      CotiState.Update( hostTemplateId, cotiAttached, hostNvgOn );

      // Nothing further. The second-camera path owns thermal rendering completely, and the
      // single most important rule of that design is that nothing may switch ThermalVision on
      // for Camera.main - doing so raises the global _ThermalVisionOn across the player's whole
      // render span, which is what made the first implementation thermalise the entire screen,
      // viewmodel included. The code that drove the main camera lived here behind a check that
      // could never be false, along with the release path for a flag it could never set.
    }

    private static bool IsCotiAttached( Item hostItem )
    {
      var slots = ( hostItem as CompoundItem )?.Slots;
      if( slots == null )
        return false;

      for( var i = 0; i < slots.Length; i++ )
      {
        var slot = slots[i];
        if( slot != null && slot.Name == CotiModSlotName )
        {
          return slot.ContainedItem != null;
        }
      }

      return false;
    }
  }
}
