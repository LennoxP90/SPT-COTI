namespace Coti.Shared
{
  /// <summary>
  /// Turns "held for this long" into how far to move this frame: one step on press, nothing
  /// during the delay, then a steady rate.
  ///
  /// The accumulator is caller-owned because the rate has to be independent of frame rate - a
  /// modulus of the total held time samples unevenly and drops steps.
  /// </summary>
  public static class CotiTunerStep
  {
    public const float InitialDelaySeconds = 0.25f;
    public const float RepeatIntervalSeconds = 0.06f;
    public const float FineDivisor = 4f;

    /// <param name="heldSeconds">Time held BEFORE this frame; 0 on the frame of the press.</param>
    /// <param name="accumulator">Caller-owned, one per control, reset when the hold ends.</param>
    public static float Step( float heldSeconds, float deltaSeconds, ref float accumulator, float step, bool fine )
    {
      var size = fine ? step / FineDivisor : step;

      if( heldSeconds <= 0f )
      {
        // A tap. One immediate step, and the hold that follows (if the control stays down) starts
        // its own delay from a clean accumulator.
        accumulator = 0f;
        return size;
      }

      if( heldSeconds < InitialDelaySeconds )
        return 0f;

      accumulator += deltaSeconds;

      if( accumulator < RepeatIntervalSeconds )
        return 0f;

      var repeats = (int)( accumulator / RepeatIntervalSeconds );
      accumulator -= repeats * RepeatIntervalSeconds;

      return size * repeats;
    }
  }
}
