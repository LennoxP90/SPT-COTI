using System;
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
  /// Adds mod_coti to the CLIENT's own item templates and already-built instances.
  ///
  /// The client fetches /client/items once, at login, so a device fitted after that leaves this
  /// client's templates missing the slot until a relaunch.
  ///
  /// ItemFactory and CompoundItemTemplate are stable declared types on both builds, just named
  /// differently, so a #if alias is right here rather than EftCompat's runtime shape-matching.
  ///
  /// EnsureSlot is a single attempt; CotiHostTableClient owns the retry.
  /// </summary>
  public static class CotiSlotPatcher
  {
    /// <summary>
    /// Whether the ItemFactory singleton exists yet. Its constructor takes ItemTemplates as a
    /// readonly argument, so it cannot exist before /client/items has been fetched and parsed.
    ///
    /// Verified by decompiling TarkovApplication.PrepareGameJob on both installs: the singleton is
    /// created after Session is already dereferenced, so this flag going true also guarantees
    /// BackEndSession is non-null - which is what lets one retry gate cover both the template patch
    /// and PatchExistingInstances.
    /// </summary>
    public static bool ItemFactoryReady => Singleton<CotiClientItemFactory>.Instantiated;

    private static int _templatesPatched;

    /// <summary>
    /// How many templates EnsureSlot has actually added a NEW slot to, across the whole session -
    /// monotonically increasing, incremented only in the branch that mutates Slots, never on the
    /// "already had it" fast path. CotiHostTableClient.Apply reads the delta across one call to
    /// report how many of the hosts it just processed actually needed patching, as opposed to how
    /// many merely resolved (which EnsureSlot's own bool return already tells the caller).
    /// </summary>
    public static int TemplatesPatchedCount => _templatesPatched;

    /// <summary>
    /// Ensures hostTemplateId's item template - and every already-constructed instance of it in
    /// the local player's profile - carries the mod_coti slot. Idempotent: a host that already
    /// carries the slot, from a previous call or because it shipped with it, costs one Any() scan
    /// per instance and nothing more. Returns false both when hostTemplateId does not resolve to a
    /// CompoundItem template on THIS client - an optional host mod the player has not installed,
    /// the ordinary case, not a fault - AND when ItemFactoryReady is still false, which the caller
    /// (CotiHostTableClient) is what actually retries; this method itself makes no retry decision.
    /// </summary>
    public static bool EnsureSlot( string hostTemplateId )
    {
      var template = ResolveTemplate( hostTemplateId );
      if( template == null )
        return false;

      if( !HasSlot( template.Slots ) )
      {
        template.Slots = template.Slots.Concat( new[] { BuildSlot() } ).ToArray();
        System.Threading.Interlocked.Increment( ref _templatesPatched );
        Plugin.Log?.LogInfo( $"[COTI] mod_coti added to client template {hostTemplateId}" );
      }

      PatchExistingInstances( hostTemplateId, template );
      return true;
    }

    private static CotiClientCompoundTemplate? ResolveTemplate( string hostTemplateId )
    {
      if( string.IsNullOrEmpty( hostTemplateId ) || !Singleton<CotiClientItemFactory>.Instantiated )
        return null;

      var templates = Singleton<CotiClientItemFactory>.Instance.ItemTemplates;
      return templates.TryGetValue( hostTemplateId, out var found ) ? found as CotiClientCompoundTemplate : null;
    }

    private static bool HasSlot( Slot[] slots )
    {
      return slots != null && slots.Any( s => s != null && s.Name == CotiIds.ModSlotName );
    }

    // InheritFromItem, not DontMerge: the game's own template-to-slot conversion passes it that
    // way, and the two flags are not interchangeable.
    private static Slot BuildSlot()
    {
      var filters = new[] { new ItemFilter { Filter = new MongoID[] { CotiIds.TplId } } };
      return new Slot( CotiIds.ModSlotName, filters, false, EParentMergeType.InheritFromItem );
    }

    /// <summary>
    /// Everything already built from the stale template, wherever it sits in the profile - stash,
    /// equipped, quest containers, sorting table, hideout stashes. Inventory.GetPlayerItems()
    /// already walks every container tree deeply, so no separate recursion is needed here.
    ///
    /// Guarded rather than gated on a raid check: PatchConstants.BackEndSession is null before the
    /// backend session exists - the very first Update after Awake, before the main menu, being the
    /// ordinary case - and that is not a fault. There is nothing built yet to patch at that point,
    /// and the template half above already covers everything constructed from here on regardless.
    /// </summary>
    private static void PatchExistingInstances( string hostTemplateId, CotiClientCompoundTemplate template )
    {
      var templateSlot = template.Slots.FirstOrDefault( s => s != null && s.Name == CotiIds.ModSlotName );
      if( templateSlot == null )
        return;

      var inventory = PatchConstants.BackEndSession?.Profile?.Inventory;
      if( inventory == null )
        return;

      // Snapshotted before any mutation starts, so nothing here depends on how GetPlayerItems()'s
      // own lazy container walk interleaves with the Slots reassignments below.
      foreach( var item in inventory.GetPlayerItems().ToList() )
      {
        if( !( item is CompoundItem compound ) || compound.StringTemplateId != hostTemplateId )
          continue;

        if( HasSlot( compound.Slots ) )
          continue;

        // Atomic reference swap, not an in-place Add - see CotiSlotInjector.cs's own comment on
        // the server side for why: a fresh array is what keeps a concurrent reader (the inventory
        // UI redrawing this same frame) from ever observing a torn Slots collection.
        compound.Slots = compound.Slots.Concat( new[] { new Slot( templateSlot, compound ) } ).ToArray();
      }
    }
  }
}
