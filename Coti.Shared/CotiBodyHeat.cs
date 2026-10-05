namespace Coti.Shared
{
  /// <summary>
  /// Body temperatures for the thermal image, which reads heat as contrast against the air, the way a thermal imager
  /// calibrates to its scene. A body is its EFT temperature (36.6 plus whatever stims do). Cultists run at nearly the
  /// air's temperature, so they are faint anywhere and all but gone on a warm map, as the lore has it.
  /// </summary>
  public static class CotiBodyHeat
  {
    public const float CultistAboveAir = 3f;

    /// <summary>Every cultist role is named sectant (sectantPriest, sectantWarrior, sectantOni and the rest).</summary>
    public static bool IsCultist( string role )
    {
      return role != null && role.StartsWith( "sectant", System.StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>The Celsius the thermal image is given for a body.</summary>
    public static float Celsius( string role, float eftCelsius, float airCelsius )
    {
      return IsCultist( role ) ? airCelsius + CultistAboveAir : eftCelsius;
    }
  }
}
