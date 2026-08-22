using System.Diagnostics;

#if COTI_DEV
using System;
using System.Reflection;
#endif

namespace Coti.Client.Dev
{
  /// <summary>
  /// A trimmed, in-process echo of scripts/EftResolveProbe, scoped to the accessors
  /// CotiInspectButton depends on. EftResolveProbe.exe already proved every one of these resolves
  /// on both installs (including the obfuscated 4.0 names, method_1/method_5/item_0), but that
  /// proof lived only in a one-off console run against a chosen install root - nothing about it
  /// was reusable from inside the repo. This runs the same resolvers for free inside any COTI_DEV
  /// build, against whatever Coti.Client.dll and game assemblies BepInEx already loaded, so the
  /// next person to touch EftCompat's inspect-window section does not have to rebuild a separate
  /// tool to ask the same question again after a game update.
  ///
  /// Reporting only - resolve the accessors, log what each bound to, done. Entry point is
  /// [Conditional] on COTI_DEV, same as CotiDevTools, so Release drops the call entirely.
  /// </summary>
  public static class CotiInspectButtonProbe
  {
    [Conditional( "COTI_DEV" )]
    public static void Run()
    {
#if COTI_DEV
      Report( "ItemSpecificationPanelType", () => EftCompat.ItemSpecificationPanelType.FullName );
      Report( "ItemSpecificationPanelShowMethod", () => Describe( EftCompat.ItemSpecificationPanelShowMethod() ) );
      Report( "InteractionButtonsContainerField", () => Describe( EftCompat.InteractionButtonsContainerField() ) );
      Report( "InspectedItemField", () => Describe( EftCompat.InspectedItemField() ) );
      Report( "ButtonTemplateField", () => Describe( EftCompat.ButtonTemplateField() ) );
      Report( "ButtonsContainerField", () => Describe( EftCompat.ButtonsContainerField() ) );
      Report( "CreateContextButtonMethod", () => Describe( EftCompat.CreateContextButtonMethod() ) );
      Report( "BindButtonMethod", () => Describe( EftCompat.BindButtonMethod() ) );
#endif
    }

#if COTI_DEV
    private static string Describe( MethodBase method )
    {
      var names = Array.ConvertAll( method.GetParameters(), p => p.Name );
      return $"{method.DeclaringType?.FullName}.{method.Name}({string.Join( ", ", names )})";
    }

    private static string Describe( FieldInfo field )
    {
      return $"{field.DeclaringType?.FullName}.{field.Name} : {field.FieldType.Name}";
    }

    /// <summary>
    /// One resolver failing must not hide the rest - the whole point of running this is to see
    /// every accessor's answer in one pass, including which ones broke.
    /// </summary>
    private static void Report( string label, Func<string> resolve )
    {
      try
      {
        Plugin.Log.LogInfo( $"[COTI PROBE] {label} -> {resolve()}" );
      }
      catch( Exception ex )
      {
        Plugin.Log.LogWarning( $"[COTI PROBE] {label} FAILED: {ex.Message}" );
      }
    }
#endif
  }
}
