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

    /// <summary>
    /// The thermal scope rings: TSR-1 for 30 mm scopes, TSR-2 for 34 mm, TSR-3 for 35 mm, each black then tan. Same
    /// profile warning as <see cref="TplId"/>.
    /// </summary>
    public static readonly string[] ScopeRingTplIds =
    {
      "6ad5739462c39577194eedc5", "6ad573a014ebd436fbd1dfda", // TSR-1 30 mm
      "6ad5775e327076f93b3b36bf", "6ad57762f51afaf4379f19a2", // TSR-2 34 mm
      "6ad577679385f594db176ad6", "6ad5776a93ea4db35ed55dcf", // TSR-3 35 mm
    };

    public static bool IsScopeRing( string tplId ) => System.Array.IndexOf( ScopeRingTplIds, tplId ) >= 0;

    /// <summary>The scope ring's mesh node (mount and device in one) and its power LED, as the riser's two above.</summary>
    public const string ScopeRingMeshName = "TR2_LOD0";
    public const string ScopeRingLedName = "TR2_LED";

    /// <summary>A riser's or a scope ring's mesh node: the client treats both as the one thermal device.</summary>
    public static bool IsThermalMeshName( string name ) => name == RiserMeshName || name == ScopeRingMeshName;

    public static bool IsThermalNodeName( string name ) =>
        IsThermalMeshName( name ) || name == RiserLedName || name == ScopeRingLedName;
  }
}
