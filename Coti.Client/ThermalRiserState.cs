using Coti.Shared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The thermal riser as a device: whether one sits under a sight on the weapon in hand, and its own power
  /// sequence. The riser keeps state of its own, apart from the goggle COTI's, and has its own power key (Riser Power
  /// Toggle); the goggle COTI's never touches it. Both may be on at once. The thermal mode is one setting the two share.
  ///
  /// The power sequence leads and the riser's light state follows it (CotiRiserLight): a press starts the boot or the
  /// shutdown here at once, the light comes on at the boot's first frame and goes off only when Power Off has ended, so
  /// the LED, on this player's screen and through Fika on everyone else's, stays lit through the whole shutdown.
  ///
  /// Ticked every frame, so the search stays cheap: the weapon's parts are walked only when the weapon in
  /// hand changes, or once a second while no riser has been found, or to confirm one is still fitted.
  /// </summary>
  internal static class ThermalRiserState
  {
    private const float RescanSeconds = 1f;
    private static readonly MongoID[] RiserTemplates = System.Array.ConvertAll( CotiIds.RiserTplIds, id => new MongoID( id ) );
    private static readonly CotiPowerTimings Timings = new CotiPowerTimings();
    private static CotiPowerSequence Sequence = new CotiPowerSequence( Timings, startOn: false );

    private static Item _weapon;
    private static CompoundItem _riser;
    private static float _nextScan;
    private static LightComponent _light;
    private static float _nextLightSend;

    /// <summary>A light change that did not take is sent again after this long, never every frame.</summary>
    private const float LightResendSeconds = 0.25f;

    /// <summary>This frame's riser power state: phase, click and the boot message.</summary>
    internal static CotiPowerFrame Frame { get; private set; } = CotiPowerFrame.Steady( CotiPowerPhase.Off );

    /// <summary>A riser on the weapon in hand, with a sight fitted on it.</summary>
    internal static bool Present => _riser != null && SightFitted( _riser );

    /// <summary>The riser draws its heat: present and fully on. The goggle COTI may draw at the same time.</summary>
    internal static bool Active => Present && Frame.ThermalOn;

    /// <summary>Present and anywhere from its boot to the end of its Power Off message: the riser is using its sight.</summary>
    internal static bool Powered => Present && Frame.Phase != CotiPowerPhase.Off;

    /// <summary>The riser's light state: its LED, and what every player's copy of the item shows.</summary>
    private static bool LightOn => _light != null && _light.IsActive;

    /// <summary>
    /// The player is aiming through a sight on the riser, in its red dot or its magnified mode: the aimed sight's camera
    /// bone sits under the riser.
    /// </summary>
    internal static bool AimedThrough
    {
      get
      {
        if( !Present )
          return false;

        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var animation = world?.MainPlayer?.ProceduralWeaponAnimation;
        if( animation == null || !animation.IsAiming || animation.ScopeAimTransforms.Count == 0 )
          return false;

        var bone = animation.CurrentScope?.Bone;
        return bone != null && IsOnRiser( bone );
      }
    }

    /// <summary>
    /// Whether a transform sits on a thermal riser: some ancestor has the riser's mesh node as a child. The riser's red
    /// dot window, its magnified lens and the aim bone of a sight on it all pass.
    /// </summary>
    internal static bool IsOnRiser( Transform node )
    {
      for( node = node != null ? node.parent : null; node != null; node = node.parent )
      {
        if( node.Find( CotiIds.RiserMeshName ) != null )
          return true;
      }

      return false;
    }

    /// <summary>
    /// The riser's power key: boots it, or starts its shutdown, or turns a shutdown back into a boot. A boot lights the
    /// LED at once; a shutdown leaves it lit until Power Off has ended.
    /// </summary>
    internal static void RequestToggle()
    {
      if( Hands() == null )
        return;

      Sequence.Press( Time.realtimeSinceStartup );
      _nextLightSend = 0f;
      SyncLight();
    }

    /// <summary>
    /// The weapon in <paramref name="hands"/> is leaving them (holster, swap, drop). A shutdown under way ends here and the
    /// light goes off through those hands while they still hold the riser, so Fika carries it.
    /// </summary>
    internal static void LeavingHands( Player.FirearmController hands )
    {
      if( _riser == null || !CotiRiserLight.EndsOnLeavingHands( Sequence.Phase ) || hands == null
          || !Patches.RiserLights.IsRiser( hands.Item, _riser.Id ) )
        return;

      Sequence = new CotiPowerSequence( Timings, startOn: false );
      Frame = CotiPowerFrame.Steady( CotiPowerPhase.Off );
      if( LightOn )
        SendLight( hands, false );
    }

    /// <summary>Names a newly chosen mode in the sight, on the same clock Tick advances with.</summary>
    internal static void ShowMode()
    {
      Sequence.ShowMode( Time.realtimeSinceStartup );
    }

    internal static void Tick()
    {
      Track();

      var settings = Plugin.Config?.PowerSequence;
      if( settings != null )
      {
        settings.CopyTo( Timings );
        Sequence.Enabled = settings.Enabled;
      }

      Frame = Sequence.Advance( Time.realtimeSinceStartup );
      SyncLight();
    }

    /// <summary>
    /// Brings the light state to the sequence's phase. Anything else that flips the riser's light while it is in hand is
    /// put back here; T, Ctrl+T and the release-all already leave it alone (RiserLightFilterPatches).
    /// </summary>
    private static void SyncLight()
    {
      var request = CotiRiserLight.Request( Sequence.Phase, LightOn );
      if( request == CotiLightRequest.None || Time.realtimeSinceStartup < _nextLightSend )
        return;

      var hands = Hands();
      if( hands != null )
        SendLight( hands, request == CotiLightRequest.On );
    }

    /// <summary>Through the hands, so Fika carries it to the other players. Forced: the sequence decided, not the hands.</summary>
    private static void SendLight( Player.FirearmController hands, bool on )
    {
      _nextLightSend = Time.realtimeSinceStartup + LightResendSeconds;
      hands.SetLightsState( new[] { new LightsState { Id = _riser.Id, IsActive = on, LightMode = 0 } }, true, false );
    }

    /// <summary>
    /// The local player's hands while they hold the tracked riser, else null. Not once they are being put away (Drop has
    /// run): a press then would start a shutdown that LeavingHands can no longer end, and peers would keep the LED lit.
    /// </summary>
    private static Player.FirearmController Hands()
    {
      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      var hands = world?.MainPlayer?.HandsController as Player.FirearmController;
      return hands != null && !hands.Destroyed && _riser != null && _light != null && Patches.RiserLights.IsRiser( hands.Item, _riser.Id )
          ? hands : null;
    }

    private static void Track()
    {
      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      var weapon = world?.MainPlayer?.HandsController?.Item as Weapon;

      if( weapon != _weapon || Time.realtimeSinceStartup >= _nextScan )
      {
        var riser = Find( weapon );
        _weapon = weapon;
        if( riser != _riser )
          Adopt( riser );
        _nextScan = Time.realtimeSinceStartup + RescanSeconds;
      }
    }

    /// <summary>
    /// A different riser in hand shows its own state at once, with no boot or shutdown replayed. One that left mid-shutdown
    /// without passing LeavingHands (the player died holding it) has its item state set off here; nothing in hand can send
    /// that to Fika, so other players keep its last state.
    /// </summary>
    private static void Adopt( CompoundItem riser )
    {
      if( _light != null && _light.IsActive && CotiRiserLight.EndsOnLeavingHands( Sequence.Phase ) )
        _light.IsActive = false;

      _riser = riser;
      _light = riser?.GetItemComponent<LightComponent>();
      Sequence = new CotiPowerSequence( Timings, startOn: LightOn );
      Frame = Sequence.Advance( Time.realtimeSinceStartup );
      _nextLightSend = 0f;
    }

    private static CompoundItem Find( Item weapon )
    {
      if( weapon == null )
        return null;

      foreach( var item in weapon.GetAllItems() )
      {
        if( IsRiser( item.TemplateId ) )
          return item as CompoundItem;
      }

      return null;
    }

    /// <summary>Any riser colourway (CotiIds.RiserTplIds). Allocation-free, for the per-item checks.</summary>
    internal static bool IsRiser( MongoID template )
    {
      for( var i = 0; i < RiserTemplates.Length; i++ )
      {
        if( template == RiserTemplates[i] )
          return true;
      }

      return false;
    }

    private static bool SightFitted( CompoundItem riser )
    {
      foreach( var slot in riser.Slots )
      {
        if( slot.ID == "mod_scope" && slot.ContainedItem != null )
          return true;
      }

      return false;
    }
  }
}
