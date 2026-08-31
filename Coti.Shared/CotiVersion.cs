namespace Coti.Shared
{
  /// The mod's version, declared once for both halves - bumping one side and not the other ships a
  /// client and server that disagree, which no build or test can catch.
  ///
  /// Each SPT generation has its own line, and the major encodes the generation: 2.x.y is SPT 4.0,
  /// 3.x.y is 4.1. The two advance in pairs, 4.0 taking the lower major.
  public static class CotiVersion
  {
    /// <summary>
    /// SPT 4.0 line. Bump here and the 4.0 client and server both follow.
    /// </summary>
    public const string Spt40 = "2.0.0";

    /// <summary>
    /// SPT 4.1 line. Bump here and the 4.1 client and server both follow.
    /// </summary>
    public const string Spt41 = "3.0.1";

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
