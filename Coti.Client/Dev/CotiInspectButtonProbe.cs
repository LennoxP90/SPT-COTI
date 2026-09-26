using System.Diagnostics;

#if COTI_DEV
using System;
using System.Reflection;
#endif

namespace Coti.Client.Dev
{
  /// <summary>
  /// An in-process version of scripts/EftResolveProbe, scoped to the accessors CotiInspectButton
  /// depends on (including the obfuscated 4.0 names, method_1/method_5/item_0). It runs the same
  /// resolvers inside any COTI_DEV build against the assemblies BepInEx loaded, to check
  /// EftCompat's inspect-window section after a game update.
  ///
  /// Reporting only: it resolves the accessors and logs what each bound to. The entry point is
  /// [Conditional] on COTI_DEV, like CotiDevTools, so Release drops the call entirely.
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
    /// One resolver failing must not hide the rest, so every accessor's result is reported in one
    /// pass.
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
