using System;
using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Utils;

#if SPT40
using CotiClientItemFactory = ItemFactoryClass;
using CotiClientCompoundTemplate = CompoundItemTemplateClass;
#else
using CotiClientItemFactory = EFT.ItemFactory;
using CotiClientCompoundTemplate = EFT.InventoryLogic.CompoundItemTemplate;
#endif

namespace Coti.Client
{
  /// <summary>
  /// Adds a device's COTI slots to the client's own item templates and already-built instances.
  ///
  /// The client fetches /client/items once, at login, so a device fitted or upgraded after that
  /// leaves this client's templates missing slots until a relaunch.
  ///
  /// ItemFactory and CompoundItemTemplate are stable declared types on both builds, just named
  /// differently, so a #if alias is used here rather than EftCompat's runtime shape-matching.
  ///
  /// EnsureSlot is a single attempt; CotiHostTableClient owns the retry.
  /// </summary>
  public static class CotiSlotPatcher
  {
    /// <summary>
    /// Whether the ItemFactory singleton exists yet. Its constructor takes ItemTemplates as a
    /// readonly argument, so it cannot exist before /client/items has been fetched and parsed.
    ///
    /// TarkovApplication.PrepareGameJob creates the singleton after Session is dereferenced on both
    /// SPT versions, so this flag being true also means BackEndSession is non-null. That lets one
    /// retry gate cover both the template patch and PatchExistingInstances.
    /// </summary>
    public static bool ItemFactoryReady => Singleton<CotiClientItemFactory>.Instantiated;

    private static int _templatesPatched;

    /// <summary>
    /// How many templates EnsureSlot has added new slots to across the session. Monotonically
    /// increasing, incremented only in the branch that mutates Slots. CotiHostTableClient.Apply
    /// reads the delta across one call to report how many hosts needed patching, as opposed to how
    /// many resolved (which EnsureSlot's bool return reports).
    /// </summary>
    public static int TemplatesPatchedCount => _templatesPatched;

    /// <summary>
    /// Ensures hostTemplateId's item template, and every already-constructed instance of it in the
    /// local player's profile, carries every slot in slotNames (CotiNvgHostConfig.SlotNames, in
    /// auto-pick order). Idempotent: a host that already carries them costs one Any() scan per slot
    /// and instance. Returns false when hostTemplateId does not resolve to a CompoundItem template
    /// on this client (an optional host mod the player has not installed, which is normal) and when
    /// ItemFactoryReady is still false. The caller, CotiHostTableClient, decides whether to retry.
    /// </summary>
    public static bool EnsureSlot( string hostTemplateId, IReadOnlyList<string> slotNames )
    {
      var template = ResolveTemplate( hostTemplateId );
      if( template == null )
        return false;

      var missing = slotNames.Where( name => !HasSlot( template.Slots, name ) ).ToList();

      if( missing.Count > 0 )
      {
        template.Slots = template.Slots.Concat( missing.Select( name => BuildSlot( name ) ) ).ToArray();
        System.Threading.Interlocked.Increment( ref _templatesPatched );
        Plugin.Log?.LogInfo( $"[COTI] {string.Join( ", ", missing )} added to client template {hostTemplateId}" );
      }

      PatchExistingInstances( hostTemplateId, template, slotNames );
      return true;
    }

    private static CotiClientCompoundTemplate? ResolveTemplate( string hostTemplateId )
    {
      if( string.IsNullOrEmpty( hostTemplateId ) || !Singleton<CotiClientItemFactory>.Instantiated )
        return null;

      var templates = Singleton<CotiClientItemFactory>.Instance.ItemTemplates;
      return templates.TryGetValue( hostTemplateId, out var found ) ? found as CotiClientCompoundTemplate : null;
    }

    private static bool HasSlot( Slot[] slots, string slotName )
    {
      return slots != null && slots.Any( s => s != null && s.Name == slotName );
    }

    // InheritFromItem rather than DontMerge: the game's own template-to-slot conversion passes it
    // that way, and the two flags are not interchangeable.
    private static Slot BuildSlot( string slotName )
    {
      var filters = new[] { new ItemFilter { Filter = new MongoID[] { CotiIds.TplId } } };
      return new Slot( slotName, filters, false, EParentMergeType.InheritFromItem );
    }

    /// <summary>
    /// Everything already built from the stale template, wherever it sits in the profile - stash,
    /// equipped, quest containers, sorting table, hideout stashes. Inventory.GetPlayerItems()
    /// walks every container tree deeply, so no separate recursion is needed.
    ///
    /// Guarded rather than gated on a raid check: PatchConstants.BackEndSession is null before the
    /// backend session exists, such as on the first Update after Awake. Nothing is built yet at
    /// that point, and the template half above covers everything constructed afterwards.
    /// </summary>
    private static void PatchExistingInstances(
        string hostTemplateId, CotiClientCompoundTemplate template, IReadOnlyList<string> slotNames )
    {
      // In slotNames order rather than the template's, so an instance's new slots keep the
      // auto-pick order.
      var templateSlots = slotNames
          .Select( name => template.Slots.FirstOrDefault( s => s != null && s.Name == name ) )
          .Where( s => s != null )
          .ToList();

      if( templateSlots.Count == 0 )
        return;

      var inventory = PatchConstants.BackEndSession?.Profile?.Inventory;
      if( inventory == null )
        return;

      // Snapshotted before any mutation, so GetPlayerItems()'s lazy container walk cannot
      // interleave with the Slots reassignments below.
      foreach( var item in inventory.GetPlayerItems().ToList() )
      {
        if( !( item is CompoundItem compound ) || compound.StringTemplateId != hostTemplateId )
          continue;

        var missing = templateSlots.Where( slot => !HasSlot( compound.Slots, slot.Name ) ).ToList();
        if( missing.Count == 0 )
          continue;

        // Atomic reference swap rather than an in-place Add, as in the server's CotiSlotInjector: a
        // fresh array keeps a concurrent reader (the inventory UI redrawing this frame) from
        // observing a torn Slots collection.
        compound.Slots = compound.Slots.Concat( missing.Select( slot => new Slot( slot, compound ) ) ).ToArray();
      }
    }
  }
}
