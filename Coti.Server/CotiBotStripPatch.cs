using System.Reflection;
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
/// </summary>
[Injectable( TypePriority = CotiLoadOrder.Preload )]
public class CotiBotStripPatch : AbstractPatch, IOnLoad
{
  private static CotiServerConfig _config = default!;
  private static readonly MongoId CotiTpl = new( CotiItemFactory.CotiTplId );

  public CotiBotStripPatch( CotiServerConfig config )
  {
    _config = config;
  }

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
    if( _config.Loot.OnBots )
      return;

    // A COTI has no slots, so nothing is ever parented to one.
    __result?.Inventory?.Items?.RemoveAll( item => item.Template == CotiTpl );
  }
}
