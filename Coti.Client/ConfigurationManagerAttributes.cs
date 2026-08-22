namespace Coti.Client
{
  /// <summary>
  /// Duck-typed by BepInEx ConfigurationManager - it reads these fields by name off whatever
  /// object is passed as a ConfigDescription tag. Copying the class in is the documented
  /// pattern; referencing ConfigurationManager would add a shipped dependency for a UI hint.
  ///
  /// Field names and types must match ConfigurationManager's expectations exactly. Do not
  /// rename or retype.
  /// </summary>
  internal sealed class ConfigurationManagerAttributes
  {
    public bool? IsAdvanced;

    /// <summary>
    /// Draws the row itself instead of ConfigurationManager's default editor for the type. Used
    /// for the mask editor's Open button, which is an action rather than a value.
    /// </summary>
    public System.Action<BepInEx.Configuration.ConfigEntryBase>? CustomDrawer;

    /// <summary>
    /// Suppresses the Reset-to-default button, which means nothing for a row that is a button.
    /// </summary>
    public bool? HideDefaultButton;
  }
}
