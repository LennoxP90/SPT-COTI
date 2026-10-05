namespace Coti.Shared
{
  /// <summary>
  /// Heat the thermal image gives props EFT leaves cold or stale: a burning fire's own prop, and a weapon lying loose.
  /// </summary>
  public static class CotiWorldHeat
  {
    /// <summary>Kelvin at 0 C. COTI's temperatures go into a renderer's block in Kelvin.</summary>
    public const float Kelvin = 273.15f;

    /// <summary>
    /// What COTI writes into a renderer's _CotiHeatTemp: a temperature in Kelvin, or 0 for none. In Kelvin so that 0 can
    /// only ever mean "none": in Celsius a cultist or a lit LED on a freezing map is at or below 0, and the heat shader
    /// would take it for no COTI heat at all.
    /// </summary>
    public static float BlockValue( float? celsius )
    {
      return celsius.HasValue ? celsius.Value + Kelvin : 0f;
    }

    /// <summary>A burning fire's prop, past the top of the 30 to 40 C range so it draws at full heat.</summary>
    public const float FireCelsius = 60f;

    /// <summary>A lit lamp's glowing parts: past the top of the hot range, so it reads at full heat.</summary>
    public const float LampCelsius = 50f;

    /// <summary>
    /// Whether a lamp controller's state is lit. EFT authors many lamps (the tripod construction lights among them) at
    /// a heat factor as cold as a wall, so what decides is whether the controller has it on, not the material.
    /// </summary>
    public static bool IsLampLit( string state )
    {
      return state == "On" || state == "TurningOn" || state == "ConstantFlickering";
    }

    /// <summary>
    /// A lit flashlight's head over the air, in degrees: LEDs run far cooler than a lamp's bulb. One value for every
    /// weapon light, set by eye across all of them at once.
    /// </summary>
    public const float FlashlightWarmth = 1.5f;

    /// <summary>
    /// How bright a lit flashlight's heat draws, against 1 for everything else: an LED's head reads dimmer on screen than
    /// a body. Only the drawn pixel; its warmth, and so whether it shows and how it grades, is FlashlightWarmth's.
    /// </summary>
    public const float FlashlightBrightness = 0.5f;

    /// <summary>How far a lit flashlight's warmth reaches back from its lens, and how wide, in metres.</summary>
    public const float FlashlightHeatMetres = 0.07f;

    /// <summary>A weapon's parts at rest, overheat 0 (WeaponPrefab).</summary>
    public const float WeaponRestingCelsius = 30f;

    /// <summary>
    /// A FlameDamageTrigger this large is a map's edge kill zone, not a fire. Fire barrels and bonfires are a metre or two.
    /// </summary>
    public const float LargestFireMetres = 10f;

    /// <summary>A renderer this large beside a fire is the ground or a building under it, not the fire's prop.</summary>
    public const float LargestFirePropMetres = 4f;

    /// <summary>
    /// Whether a renderer is part of a burning fire's prop: inside the fire's volume, the size of a barrel or a pile of
    /// wood, and not a particle system (the flames themselves are see-through).
    /// </summary>
    public static bool IsFireProp( float fireMetres, float rendererMetres, bool insideFire, bool particles )
    {
      return fireMetres <= LargestFireMetres && rendererMetres <= LargestFirePropMetres && insideFire && !particles;
    }

    /// <summary>
    /// A weapon part's Celsius for its weapon's overheat, the same conversion EFT uses (HotObject.ConvertHeat2Celsio):
    /// 30 at rest. A part with no weapon around it is at rest.
    /// </summary>
    public static float WeaponCelsius( float? overheat )
    {
      return overheat.HasValue ? 10f * overheat.Value / 220f + WeaponRestingCelsius : WeaponRestingCelsius;
    }

    /// <summary>
    /// Whether a weapon part reads warmer than its weapon's overheat says it is. EFT rewrites a weapon's parts only
    /// when its overheat changes, so a weapon that has not fired keeps whatever its prefab was saved with (40 C on some
    /// gas blocks), in a bot's hands as much as on the ground.
    /// </summary>
    public static bool IsStaleWeaponHeat( float celsius, float expectedCelsius )
    {
      return celsius > expectedCelsius + 0.5f;
    }
  }
}
