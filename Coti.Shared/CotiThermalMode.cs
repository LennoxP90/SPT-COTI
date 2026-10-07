namespace Coti.Shared
{
  /// <summary>
  /// How heat is drawn. Alt+N switches between them; the choice persists in the F12 config.
  /// </summary>
  public enum CotiThermalMode
  {
    Outline,
    Full,
  }

  public static class CotiThermalModes
  {
    public static CotiThermalMode Next( CotiThermalMode mode )
    {
      return mode == CotiThermalMode.Outline ? CotiThermalMode.Full : CotiThermalMode.Outline;
    }

    /// <summary>
    /// The overlay shader's _OutlineMix. Its rim always draws at full heat, so a mix leaves the interior at
    /// (1 - mix) of it: Full is the rim over a fill at fillPercent, Outline is the rim alone.
    /// </summary>
    public static float OutlineMix( CotiThermalMode mode, float fillPercent )
    {
      if( mode == CotiThermalMode.Outline )
        return 1f;

      var fill = fillPercent / 100f;
      return 1f - ( fill < 0f ? 0f : fill > 1f ? 1f : fill );
    }

    /// <summary>
    /// Full mode's fill as saved, applied once per install: a saved 55 is the 3.2.0 default rather than a choice and
    /// becomes 45; any other value is a choice and stays.
    /// </summary>
    public static float MovedFill( float saved )
    {
      return saved == 55f ? 45f : saved;
    }
  }
}
