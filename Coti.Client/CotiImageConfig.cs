using Newtonsoft.Json;

namespace Coti.Client
{
  /// <summary>
  /// The thermal image tuning shared by every night vision host. These were byte-identical across
  /// all six devices in coti-defaults.json - they were never per-device data, they were global
  /// settings stored six times - so they now live in one place and are bound as F12 globals rather
  /// than duplicated per host.
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
    /// Ramp palette mapping heat to colour - Fusion, Rainbow, WhiteHot, BlackHot. A string, not the
    /// game enum, because the shared half must not reference a game assembly. Empty leaves the player's
    /// current palette alone.
    /// </summary>
    [JsonProperty( "palette" )]
    public string Palette { get; set; } = "";

    /// <summary>
    /// Shifts where the ramp palette is sampled. Vanilla default 0.
    /// </summary>
    [JsonProperty( "rampShift" )]
    public float RampShift { get; set; }

    // 0.16 is the value every shipped host ran with.
    [JsonProperty( "heatThreshold" )]
    public float HeatThreshold { get; set; } = 0.16f;

    /// <summary>
    /// Crossfades solid hot shapes (0) against edge-only contours (1).
    /// </summary>
    [JsonProperty( "outlineMix" )]
    public float OutlineMix { get; set; } = 1.0f;

    /// <summary>
    /// Contour thickness in texels of the thermal target, when OutlineMix &gt; 0.
    /// </summary>
    [JsonProperty( "outlineWidth" )]
    public float OutlineWidth { get; set; } = 1.5f;

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
