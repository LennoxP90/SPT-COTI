using Coti.Shared;
using Newtonsoft.Json;

namespace Coti.Client
{
  /// <summary>
  /// The thermal image tuning shared by every night vision host. The values are the same for every
  /// device, so they live in one place and are bound as F12 globals rather than per host.
  /// </summary>
  public class CotiImageConfig
  {
    [JsonProperty( "minimumTemperatureValue" )]
    public float MinimumTemperatureValue { get; set; } = 0.25f;

    [JsonProperty( "mainTexColorCoef" )]
    public float MainTexColorCoef { get; set; } = 0.2f;

    [JsonProperty( "depthFade" )]
    public float DepthFade { get; set; } = 0.03f;

    [JsonProperty( "isPixelated" )]
    public bool IsPixelated { get; set; }

    [JsonProperty( "isNoisy" )]
    public bool IsNoisy { get; set; }

    [JsonProperty( "isMotionBlurred" )]
    public bool IsMotionBlurred { get; set; }

    /// <summary>
    /// ThermalVision.UnsharpRadiusBlur. Vanilla default 5.
    /// </summary>
    [JsonProperty( "unsharpRadiusBlur" )]
    public float UnsharpRadiusBlur { get; set; } = 5.0f;

    /// <summary>
    /// ThermalVision.UnsharpBias, the edge-dominance lever. Vanilla default 2.
    /// </summary>
    [JsonProperty( "unsharpBias" )]
    public float UnsharpBias { get; set; } = 2.0f;

    /// <summary>
    /// Ramp palette mapping heat to colour - Fusion, Rainbow, WhiteHot, BlackHot. A string rather than
    /// the game enum because the shared half must not reference a game assembly. Empty leaves the player's
    /// current palette alone.
    /// </summary>
    [JsonProperty( "palette" )]
    public string Palette { get; set; } = "";

    /// <summary>
    /// Shifts where the ramp palette is sampled. Vanilla default 0.
    /// </summary>
    [JsonProperty( "rampShift" )]
    public float RampShift { get; set; }

    [JsonProperty( "heatThreshold" )]
    public float HeatThreshold { get; set; } = 0.16f;

    /// <summary>
    /// How heat is drawn. Alt+N switches it; F12 remembers it.
    /// </summary>
    [JsonProperty( "mode" )]
    public CotiThermalMode Mode { get; set; } = CotiThermalMode.Outline;

    /// <summary>
    /// How glass draws: Plain hides what is behind it; Reflections also mirrors the bodies in front of it.
    /// </summary>
    [JsonProperty( "glass" )]
    public CotiGlassMode Glass { get; set; } = CotiGlassMode.Reflections;

    /// <summary>
    /// A multiplier on every heat signature's brightness, over the default the heat rule is tuned to. Below 1 dims the
    /// image, above brightens it.
    /// </summary>
    [JsonProperty( "heatBrightness" )]
    public float HeatBrightness { get; set; } = 1f;

    /// <summary>
    /// Whether a lit flashlight's head shows heat. Off, no weapon light is looked for or heated.
    /// </summary>
    [JsonProperty( "renderFlashlightHeat" )]
    public bool RenderFlashlightHeat { get; set; } = true;

    /// <summary>
    /// Whether a lit lamp that lamps.json lists shows heat. Off, every lamp is cold and no lamp's shape is found.
    /// </summary>
    [JsonProperty( "renderSearchlightHeat" )]
    public bool RenderSearchlightHeat { get; set; } = true;

    /// <summary>
    /// Full mode's interior brightness under its full-brightness rim, in percent.
    /// </summary>
    [JsonProperty( "fullFillPercent" )]
    public float FullFillPercent { get; set; } = 45f;

    /// <summary>
    /// The overlay's solid (0) to edge-only (1) crossfade, from the mode.
    /// </summary>
    [JsonIgnore]
    public float OutlineMix => CotiThermalModes.OutlineMix( Mode, FullFillPercent );

    /// <summary>
    /// Contour thickness in texels of the thermal target, when OutlineMix &gt; 0.
    /// </summary>
    [JsonProperty( "outlineWidth" )]
    public float OutlineWidth { get; set; } = 1.5f;

    /// <summary>
    /// The outline's thickness on the object itself, in centimetres. Projected through the camera, so the line
    /// thins as the object recedes and thickens under magnification, between one texel and OutlineWidth. Zero
    /// keeps the fixed width.
    /// </summary>
    [JsonProperty( "outlineThicknessCm" )]
    public float OutlineThicknessCm { get; set; } = 2.0f;

    /// <summary>
    /// Overall brightness of the added heat.
    /// </summary>
    [JsonProperty( "overlayIntensity" )]
    public float OverlayIntensity { get; set; } = 6.0f;

    /// <summary>
    /// Fraction of <see cref="OverlayIntensity"/> the magnified path uses. Lower, because the 1x
    /// overlay has the circle glow beneath it and the magnified one does not, so the value that
    /// reads correctly at 1x clips magnified contours into a solid mass.
    /// </summary>
    [JsonProperty( "magnifiedIntensityScale" )]
    public float MagnifiedIntensityScale { get; set; } = 0.25f;
  }
}
