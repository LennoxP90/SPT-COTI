using Coti.Shared;
using System;
using System.IO;
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

      var pluginDirectory = Path.GetDirectoryName( Info.Location );
      CotiDisplayText.Load( pluginDirectory );
      CotiCalibrationClick.Load( pluginDirectory );

      // Seeded with the same table CotiF12Config just read, then the server's copy is fetched.
      // Both are applied from Update only: Awake runs before the game's singletons (ItemFactory,
      // the backend session) are guaranteed to exist, and Update runs reliably on this plugin.
      // See CotiHostTableClient for the fetch and apply details.
      CotiHostTableClient.Pending = new CotiPendingTable( hostFallback, fromServer: false );
      CotiHostTableClient.BeginFetch();

#if SPT41
      CotiHostSocketClient.Start();
#endif

      TryEnable( nameof( ThermalParametersPatch ), () => new ThermalParametersPatch() );
      TryEnable( nameof( GameStartedPatch ), () => new GameStartedPatch() );

      // If this one fails to enable, the attach state is only refreshed when the equipped device
      // itself changes - so a COTI added to goggles already worn would not be noticed.
      TryEnable( nameof( CotiInventoryChangePatch ), () => new CotiInventoryChangePatch() );
      TryEnable( nameof( GoggleToggleSuppressPatch ), () => new GoggleToggleSuppressPatch() );

      // Enabled unconditionally rather than only in raid: the device has to appear in the
      // inventory and on the character preview in the menu, which is where AttachMods runs most.
      TryEnable( nameof( CotiMountBonePatch ), () => new CotiMountBonePatch() );
      TryEnable( nameof( CotiAttachPatch ), () => new CotiAttachPatch() );
      TryEnable( nameof( CotiWorldViewPatch ), () => new CotiWorldViewPatch() );
      TryEnable( nameof( CotiWorldViewPatch.OnAttachMods ), () => new CotiWorldViewPatch.OnAttachMods() );

      // Runs regardless of verboseLogging: EFT's on-disk icon cache can hold pictures taken before
      // the device could attach, filed under a hash that already accounts for it, so nothing else
      // invalidates them.
      TryEnable( nameof( CotiIconCacheInvalidator ), () => new CotiIconCacheInvalidator() );

      // Before any inventory UI opens: ModSlotView caches a null against the key the first
      // time it looks and does not retry.
      TryEnable( nameof( CotiSlotIcon ), CotiSlotIcon.Install );

      // The pose editor's only entry point. See CotiInspectButton for the redraw lifecycle it
      // cooperates with.
      TryEnable( nameof( CotiInspectButton ), CotiInspectButton.Install );

      // Subscribes to CotiInspectButton.OpenRequested. The panel itself only draws once IsOpen,
      // from OnGUI below, but its window rect is BepInEx config and has to be bound here, from
      // the same ConfigFile CotiF12Config wraps.
      TryEnable( nameof( CotiPoseTuner ), CotiPoseTuner.Install );
      TryEnable( nameof( CotiTunerPanel ), () => CotiTunerPanel.Install( ( (BaseUnityPlugin)this ).Config ) );
      TryEnable( nameof( CotiMaskPanel ), () => CotiMaskPanel.Install( ( (BaseUnityPlugin)this ).Config ) );

      // [Conditional(COTI_DEV)], compiled out of Release. Verifies the accessors above resolve
      // against the assemblies this build loaded.
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
        // One patch failing must not abort Awake and disable everything after it.
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
        // As in the ModulePatch overload above, a failure here must not cascade.
        Log.LogError( $"[COTI] {name} failed to enable: {ex}" );
      }
    }

    private void OnDestroy()
    {
      CotiThermalCamera.Teardown();
      CotiOpticThermalCamera.Teardown();
      CotiOpticOverlayCompositor.Teardown();
      CotiTunerPreview.Teardown();
      CotiDisplayText.Teardown();

      // Unconditional Detach rather than Sync(): the config still reports the mode as enabled
      // here, so Sync would re-attach the buffer being torn down.
      CotiOverlayCompositor.Detach();

      MaskGenerator.Release();
    }

    private void Update()
    {
      // Support for fika headless clients, will essentially no-op
      if( IsHeadless )
        return;

      // Separate from the raid-state block below: a failure applying the host table must not
      // suppress thermal-camera updates for the rest of the session (or vice versa), and each
      // gets its own once-only log.
      ApplyPendingHostTable();

      // Ahead of the try/catch below: this restores game UI input, so an exception elsewhere in
      // the frame must not skip it.
      CotiUiBlocker.Tick();

      try
      {
        // First: everything below is raid-oriented and can throw in the menu, which is where the
        // mount is tuned. CotiDevTools.Tick is compiled out of Release, call included;
        // CotiPoseTuner.Tick handles the keyboard shortcut and always runs.
        CotiDevTools.Tick();
        CotiPoseTuner.Tick();
        CotiMaskPanel.Tick();

        // The pose editor's preview camera. It no-ops while CotiPoseTuner.IsOpen is false, so it
        // costs nothing for a player who never opens the panel.
        CotiTunerPreview.Tick();

        // Before state resolution: the toggle feeds into activation.
        CotiPowerToggle.Tick();

        UpdateCotiState();

        // After state resolution: they read CotiState.Showing, Host and CotiAttached.
        CotiDisplayText.Tick();
        CotiCalibrationClick.Tick( CotiPowerToggle.Frame );

        // Order matters: CotiState must be resolved first, since the thermal camera reads
        // CotiState.Active and CotiState.Host to decide whether and how to render.
        CotiThermalCamera.Tick();

        // After the 1x camera: the magnified path reads the same CotiState and the same
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
        CotiState.Showing = CotiShowing.Nothing;
      }
      finally
      {
        // Always, including after a throw: Sync is the only thing that detaches the overlay
        // buffer, and skipping it leaves the circle drawn with the device inactive.
        CotiOverlayCompositor.Sync();
        CotiOpticOverlayCompositor.Sync();
      }
    }

    private void UpdateCotiState()
    {
      // Read the equipped item off the player's own vision observer rather than an inventory
      // search. When a real thermal item (T-7) is worn instead of night vision,
      // NightVisionObserver's Component is null, since a thermal-only device has no
      // NightVisionComponent, so everything below resolves to "nothing equipped" and
      // CotiState.Active stays false. ThermalVision is never touched while a real thermal item is
      // equipped.
      var nvgComponent = CotiNvgHost.Component;
      var hostItem = nvgComponent?.Item;
      var hostTemplateId = hostItem?.StringTemplateId;
      // NightVision.On is the camera effect. Togglable.On is the item's switch and flips when the
      // key is pressed, about 700 ms before the goggles finish flipping down and the tube lights.
      // InProcessSwitching spans both sides of the moment the tube lights, so it cannot be used
      // either.
      var tube = CotiOverlayCompositor.Tube;
      var hostNvgOn = tube != null
          ? tube.On
          : ( nvgComponent?.Togglable?.On ?? false );
      var cotiAttached = CotiEquippedCoti.IsAttached( hostItem );

      CotiState.Update( hostTemplateId, cotiAttached, hostNvgOn );

      // Nothing further. The second-camera path owns thermal rendering, and nothing may switch
      // ThermalVision on for Camera.main: that raises the global _ThermalVisionOn across the
      // player's whole render span and thermalises the entire screen, viewmodel included.
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
    /// The pose editor's panel. IMGUI only draws from OnGUI, which Unity calls several times a
    /// frame independently of Update. CotiTunerPanel.Draw does nothing unless the panel is open.
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
