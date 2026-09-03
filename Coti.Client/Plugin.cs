using Coti.Shared;
using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Coti.Client.Dev;
using Coti.Client.Patches;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Coti.Client
{
  [BepInPlugin( "com.lennoxp90.coti", "ECOTI", CotiVersion.Current )]
  public class Plugin : BaseUnityPlugin
  {
    public static ManualLogSource Log;

    public static new CotiConfig Config = CotiConfig.Fallback;
    public static bool IsHeadless;

    private bool _loggedUpdateError;
    private bool _loggedHostTableError;
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

      var hostFallback = CotiHostTableClient.LoadEmbeddedFallback();

      _settings = new CotiF12Config( ( (BaseUnityPlugin)this ).Config, hostFallback );
      Config = _settings.Current;
      CotiPowerToggle.Bind( _settings.PowerToggle );

      // Seeded with the same table CotiF12Config just read, then handed off to fetch the
      // server's copy - both are only ever applied from Update, never here: Awake runs before
      // the game's own singletons (ItemFactory, the backend session) are guaranteed to exist,
      // and Update is what the HideManagerGameObject finding already proved DOES run reliably
      // on this plugin. See CotiHostTableClient's own comments for the fetch and apply details.
      CotiHostTableClient.Pending = new CotiPendingTable( hostFallback, fromServer: false );
      CotiHostTableClient.BeginFetch();

#if SPT41
      CotiHostSocketClient.Start();
#endif

      TryEnable( nameof( ThermalParametersPatch ), () => new ThermalParametersPatch() );
      TryEnable( nameof( GameStartedPatch ), () => new GameStartedPatch() );
      TryEnable( nameof( GoggleToggleSuppressPatch ), () => new GoggleToggleSuppressPatch() );

      // Must be enabled unconditionally, not gated on being in a raid: the device has to appear
      // in the inventory and on the character preview in the menu, which is where AttachMods
      // runs most.
      TryEnable( nameof( CotiMountBonePatch ), () => new CotiMountBonePatch() );
      TryEnable( nameof( CotiAttachPatch ), () => new CotiAttachPatch() );
      TryEnable( nameof( CotiWorldViewPatch ), () => new CotiWorldViewPatch() );
      TryEnable( nameof( CotiWorldViewPatch.OnAttachMods ), () => new CotiWorldViewPatch.OnAttachMods() );

      // A real fix, not a diagnostic, so it runs regardless of verboseLogging: EFT's on-disk
      // icon cache holds pictures taken before the device could attach, filed under a hash that
      // already accounts for it, so nothing else will ever invalidate them.
      TryEnable( nameof( CotiIconCacheInvalidator ), () => new CotiIconCacheInvalidator() );

      // Before any inventory UI opens: ModSlotView caches a null against the key the first
      // time it looks and does not retry.
      TryEnable( nameof( CotiSlotIcon ), CotiSlotIcon.Install );

      // The pose editor's only entry point - see CotiInspectButton's own class comment for the
      // redraw lifecycle this has to cooperate with.
      TryEnable( nameof( CotiInspectButton ), CotiInspectButton.Install );

      // Subscribes to CotiInspectButton.OpenRequested. The panel itself only draws once IsOpen,
      // from OnGUI below, but its window rect is BepInEx config and has to be bound here, from
      // the same ConfigFile CotiF12Config wraps.
      TryEnable( nameof( CotiPoseTuner ), CotiPoseTuner.Install );
      TryEnable( nameof( CotiTunerPanel ), () => CotiTunerPanel.Install( ( (BaseUnityPlugin)this ).Config ) );
      TryEnable( nameof( CotiMaskPanel ), () => CotiMaskPanel.Install( ( (BaseUnityPlugin)this ).Config ) );

      // [Conditional(COTI_DEV)] - compiled out of Release. Re-verifies the accessors above
      // resolve against whatever assemblies this build actually loaded.
      CotiInspectButtonProbe.Run();

      Log.LogInfo( "[COTI] Initialised" );
    }

    private static void TryEnable( string name, Func<ModulePatch> create )
    {
      try
      {
        create().Enable();
      }
      catch( Exception ex )
      {
        // One patch failing must not abort Awake and silently disable everything after it.
        Log.LogError( $"[COTI] patch {name} failed to enable: {ex}" );
      }
    }

    private static void TryEnable( string name, Action install )
    {
      try
      {
        install();
      }
      catch( Exception ex )
      {
        // Same rationale as the ModulePatch overload above: a failure here must not cascade.
        Log.LogError( $"[COTI] {name} failed to enable: {ex}" );
      }
    }

    private void OnDestroy()
    {
      CotiThermalCamera.Teardown();
      CotiOpticThermalCamera.Teardown();
      CotiOpticOverlayCompositor.Teardown();
      CotiTunerPreview.Teardown();

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

      // Own try/catch, deliberately separate from the raid-state block below: a failure applying
      // the host table must not suppress thermal-camera updates for the rest of the session (or
      // vice versa), and each gets its own once-only log rather than sharing _loggedUpdateError.
      ApplyPendingHostTable();

      // Ahead of the try/catch below on purpose: this is what restores game UI input, so it
      // must not be skippable by an exception raised somewhere else in the frame.
      CotiUiBlocker.Tick();

      try
      {
        // First, deliberately: everything below is raid-oriented and can throw in the menu, which is
        // where the mount is tuned. CotiDevTools.Tick is compiled out of Release, call included;
        // CotiPoseTuner.Tick is the promoted keyboard shortcut and always runs.
        CotiDevTools.Tick();
        CotiPoseTuner.Tick();
        CotiMaskPanel.Tick();

        // The pose editor's preview camera. Own try/catch coverage same as everything else in
        // this block - it already no-ops the instant CotiPoseTuner.IsOpen is false, so this is not
        // a per-frame cost for a player who never opens the panel.
        CotiTunerPreview.Tick();

        // Before state resolution: the toggle feeds into activation.
        CotiPowerToggle.Tick();

        UpdateCotiState();

        // Order matters: CotiState must be resolved first, since the thermal camera reads
        // CotiState.Active and CotiState.Host to decide whether and how to render.
        CotiThermalCamera.Tick();

        // After the 1x camera, not before: the magnified path reads the same CotiState and the same
        // thermal-camera config, and the 1x overlay's lens exclusion reads what this one published,
        // so a frame where the two disagree would show heat in the lens from one and a hole from the
        // other.
        CotiOpticThermalCamera.Tick();
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
        CotiOpticOverlayCompositor.Sync();

        // Last, so it reports the state the frame was actually composited with.
        CotiRenderStateLog.Tick();
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
      // NightVision.On - the camera effect - and NOT Togglable.On, which is the ITEM's switch and
      // flips the moment the key is pressed, about 700 ms before the goggles finish flipping down
      // and the tube lights. InProcessSwitching is that animation and is no use here either: it
      // spans both sides of the moment the tube lights.
      var tube = CotiOverlayCompositor.Tube;
      var hostNvgOn = tube != null
          ? tube.On
          : ( nvgComponent?.Togglable?.On ?? false );
      var cotiAttached = CotiSlotProbe.IsCotiAttached( hostItem );

      CotiState.Update( hostTemplateId, cotiAttached, hostNvgOn );

      // Nothing further. The second-camera path owns thermal rendering completely, and the
      // single most important rule of that design is that nothing may switch ThermalVision on
      // for Camera.main - doing so raises the global _ThermalVisionOn across the player's whole
      // render span, which is what made the first implementation thermalise the entire screen,
      // viewmodel included. The code that drove the main camera lived here behind a check that
      // could never be false, along with the release path for a flag it could never set.
    }

    /// <summary>
    /// Drains the pending host table on the main thread. Only a table the server actually returned
    /// may patch slots - the embedded fallback must not add a slot the server does not have.
    /// </summary>
    private void ApplyPendingHostTable()
    {
      var pending = CotiHostTableClient.TakePending();

      try
      {
        if( pending != null )
          CotiHostTableClient.Apply( pending.Devices, Config, pending.FromServer );

        CotiHostTableClient.RetrySlotPassOnceItemFactoryIsReady();
      }
      catch( Exception ex )
      {
        if( !_loggedHostTableError )
        {
          Log.LogError( $"[COTI] Applying the host table failed: {ex}" );
          _loggedHostTableError = true;
        }
      }
    }

    /// <summary>
    /// The pose editor's panel. Kept in its own MonoBehaviour callback rather than folded into
    /// Update: IMGUI only draws from OnGUI, which Unity calls several times a frame regardless of
    /// how often Update runs. CotiTunerPanel.Draw itself does nothing at all unless the panel is
    /// open, so this is not a per-frame cost for players who never touch the feature.
    /// </summary>
    private void OnGUI()
    {
      if( IsHeadless )
        return;

      CotiTunerPanel.Draw();
      CotiMaskPanel.Draw();
    }
  }
}
