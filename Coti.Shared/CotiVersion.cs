namespace Coti.Shared
{
  /// <summary>
  /// The mod's version, declared once for both halves.
  ///
  /// It used to be four literals - two in the client's EftCompat, two in the server's ModMetadata -
  /// and nothing tied a pair together. Bumping one side and not the other ships a client and server
  /// that disagree about what they are, which no build or test can catch: both compile, both load,
  /// and the mismatch only surfaces as a confusing version in a player's log.
  ///
  /// The two SPT generations keep separate lines because they are separate releases; the major
  /// field encodes the generation. See CLAUDE.md.
  /// </summary>
  public static class CotiVersion
  {
    /// <summary>
    /// SPT 4.0 line. Bump here and the 4.0 client and server both follow.
    /// </summary>
    public const string Spt40 = "0.1.0";

    /// <summary>
    /// SPT 4.1 line. Bump here and the 4.1 client and server both follow.
    /// </summary>
    public const string Spt41 = "1.1.0";

    /// <summary>
    /// Whichever line this build belongs to. A const, so it is usable in the BepInPlugin attribute.
    /// Consumers should use this rather than picking a line by hand - the branch that compiles has
    /// already made that choice, and choosing again is a chance to choose wrong.
    /// </summary>
#if SPT40
    public const string Current = Spt40;
#else
    public const string Current = Spt41;
#endif
  }
}
