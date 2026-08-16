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
    /// profile validation and refuses the login - so a change here means migrating profiles too.
    /// </summary>
    public const string TplId = "6a7f15387b68c336e18e8977";

    /// <summary>
    /// The slot the COTI occupies, and also the transform name the client creates on the host
    /// mesh: EFT resolves a mod's attachment point by matching a transform name to the slot id.
    /// </summary>
    public const string ModSlotName = "mod_coti";
  }
}
