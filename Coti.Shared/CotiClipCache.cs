namespace Coti.Shared
{
  /// <summary>
  /// EFT resets the audio system at startup and at every raid start, which empties any clip built
  /// with AudioClip.Create: it still plays, as 0 ms of nothing. A clip that reads empty is rebuilt
  /// from the samples kept since load.
  /// </summary>
  public static class CotiClipCache
  {
    public static bool NeedsRebuild( bool exists, float lengthSeconds )
    {
      return !exists || !( lengthSeconds > 0f );
    }
  }
}
