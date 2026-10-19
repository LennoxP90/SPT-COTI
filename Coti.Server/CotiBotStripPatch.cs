using System.Reflection;
using Coti.Shared;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Coti.Server;

/// <summary>
/// Takes the COTI off every generated bot unless loot.onBots is set. APBS imports the COTI into
/// its NVG mod pools and force-spawns every NVG sub-slot at night, so without this every night
/// bot wears one on every tube; neither its blacklist nor its spawn chances can thin a slot whose
/// only option is the COTI. Postfixing GenerateBot covers APBS and vanilla generation alike.
/// On 4.1 it also thins the thermal riser, which sits in every slot the LaRue riser does. SPT's PMC
/// sight whitelist keeps it out of generated sight pools, but an APBS quest weapon's scope slot is
/// filled straight from the slot filter, and a bot can carry a loose one: each riser stays with
/// riser.loot.botChance while riser.loot.onBots is set, and otherwise goes, leaving its sight on
/// the gun where the slot below takes it (CotiRiserBotRule).
/// </summary>
[Injectable( TypePriority = CotiLoadOrder.Preload )]
public class CotiBotStripPatch : AbstractPatch, IOnLoad
{
  private static CotiServerConfig _config = default!;
  private static readonly MongoId CotiTpl = new( CotiItemFactory.CotiTplId );

#if SPT40
  public CotiBotStripPatch( CotiServerConfig config )
  {
    _config = config;
  }
#else
  private static CotiTemplateTable _templates = default!;
  private static readonly HashSet<MongoId> RiserTpls = CotiIds.RiserTplIds.Select( id => new MongoId( id ) ).ToHashSet();

  public CotiBotStripPatch( CotiServerConfig config, CotiTemplateTable templates )
  {
    _config = config;
    _templates = templates;
  }
#endif

  public Task OnLoad() => OnLoadAsync( CancellationToken.None );

  public Task OnLoadAsync( CancellationToken cancellationToken )
  {
    Enable();
    return Task.CompletedTask;
  }

  protected override MethodBase GetTargetMethod()
  {
    return AccessTools.Method( typeof( BotGenerator ), "GenerateBot" );
  }

  [PatchPostfix]
  public static void Postfix( BotBase __result )
  {
    var items = __result?.Inventory?.Items;
    if( items is null )
      return;

    // A COTI has no slots, so nothing is ever parented to one.
    if( !_config.Loot.OnBots )
      items.RemoveAll( item => item.Template == CotiTpl );

#if !SPT40
    if( items.Any( item => RiserTpls.Contains( item.Template ) ) )
      ThinRisers( items );
#endif
  }

#if !SPT40
  private static void ThinRisers( List<Item> items )
  {
    var settings = _config.Riser.Loot;
    var plan = CotiRiserBotRule.Plan(
        items.Select( item => ( item.Id.ToString(), item.ParentId, item.SlotId, item.Template.ToString() ) ).ToList(),
        () => settings.OnBots && Random.Shared.NextDouble() < settings.BotChance,
        SlotAccepts );

    foreach( var ( id, parentId, slotId ) in plan.Moves )
    {
      var item = items.First( i => i.Id.ToString() == id );
      item.ParentId = parentId;
      item.SlotId = slotId;
    }

    items.RemoveAll( item => plan.Remove.Contains( item.Id.ToString() ) );
  }

  private static bool SlotAccepts( string parentTpl, string slotName, string childTpl )
  {
    if( !_templates.Items.TryGetValue( new MongoId( parentTpl ), out var parent ) )
      return false;

    var child = new MongoId( childTpl );
    return parent.Properties?.Slots?
        .FirstOrDefault( slot => slot.Name == slotName )?.Properties?.Filters?
        .Any( filter => filter.Filter?.Contains( child ) == true ) == true;
  }
#endif
}
