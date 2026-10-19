namespace Coti.Shared
{
  /// <summary>
  /// Identifiers both halves compile against, so they cannot drift. A duplicated id string fails
  /// silently: the server registers an item the client never matches, with no error either side.
  /// </summary>
  public static class CotiIds
  {
    /// <summary>
    /// Changing this orphans every instance in every profile, and a missing template fails 4.1's
    /// profile validation and refuses the login, so a change here requires migrating profiles.
    /// </summary>
    public const string TplId = "6a7f15387b68c336e18e8977";

    /// <summary>
    /// The slot the COTI occupies, and also the transform name the client creates on the host
    /// mesh: EFT resolves a mod's attachment point by matching a transform name to the slot id.
    /// </summary>
    public const string ModSlotName = "mod_coti";

    /// <summary>The red dot thermal riser, black. Same profile warning as <see cref="TplId"/>.</summary>
    public const string RiserTplId = "6ad3016bab020957abb6ca3b";

    /// <summary>The same riser in tan: its own item, as the HHS-1 Tan is. Same profile warning.</summary>
    public const string RiserTanTplId = "6ad408a96bf1f761d970a4ff";

    /// <summary>Every riser colourway. Anything that recognises a riser by template id checks this, never one id.</summary>
    public static readonly string[] RiserTplIds = { RiserTplId, RiserTanTplId };

    public static bool IsRiser( string tplId ) => System.Array.IndexOf( RiserTplIds, tplId ) >= 0;

    /// <summary>The riser's own mesh node, the only thing its shader rebind may touch.</summary>
    public const string RiserMeshName = "ThermalRiser_LOD0";

    /// <summary>The riser's power LED node, kept apart from its mesh so the client can light it.</summary>
    public const string RiserLedName = "ThermalRiser_LED";
  }
}
