using System.Collections.Generic;
using Newtonsoft.Json;

namespace Coti.Client
{
  /// <summary>
  /// The complete COTI configuration. Defaults are deserialised from the embedded
  /// Coti.Client/Assets/coti-defaults.json; the player's own settings come from the F12 page.
  /// </summary>
  public class CotiConfig
  {
    /// <summary>Keyed by host NVG template id.</summary>
    [JsonProperty( "nvgHosts" )]
    public Dictionary<string, CotiNvgHostConfig> NvgHosts { get; set; } = new Dictionary<string, CotiNvgHostConfig>();

    public bool Enabled { get; set; } = true;

    [JsonProperty( "verboseLogging" )]
    public bool VerboseLogging { get; set; }

#if COTI_DEV
    /// <summary>
    /// Arms the mount tuner's keys. A dev build reports geometry and attach results on its own; this
    /// only decides whether the arrow keys move the device.
    /// </summary>
    public bool EnablePoseModifier { get; set; }

    /// <summary>Millimetres per tuner keypress.</summary>
    public float TunerStepMm { get; set; } = 2f;

    /// <summary>Degrees per tuner keypress.</summary>
    public float TunerStepDegrees { get; set; } = 5f;

    /// <summary>Modifier held while tuning, as "+"-separated KeyCode names.</summary>
    public string TunerModifier { get; set; } = "LeftControl+LeftAlt";
#endif

    [JsonProperty( "thermalCamera" )]
    public CotiCameraConfig ThermalCamera { get; set; } = new CotiCameraConfig();

    /// <summary>Mask used when a host has no entry, or its named mask is missing.</summary>
    public const string FallbackMaskName = "centre";

    public static CotiConfig Fallback => new CotiConfig();
  }

  public class CotiCameraConfig
  {
    /// <summary>
    /// Master switch, and the live kill switch if the cost turns out unacceptable mid-raid.
    /// Defaults false so a config predating this feature cannot start spawning a camera.
    /// </summary>
    [JsonProperty( "enabled" )]
    public bool Enabled { get; set; }

    [JsonProperty( "width" )]
    public int Width { get; set; } = 640;

    /// <summary>
    /// Render target height. The ECOTI's real sensor height.
    /// </summary>
    [JsonProperty( "height" )]
    public int Height { get; set; } = 480;

    /// <summary>
    /// Sensor refresh in hertz - the real ECOTI's is 60. Drives ThermalVision's own frame hold, so
    /// the image updates at this rate over a night-vision picture running at full framerate. 0 is
    /// smooth. Costs slightly more than smooth, never less: the scene is still drawn every frame and
    /// the held copy is blitted over it.
    /// </summary>
    [JsonProperty( "hz" )]
    public int Hz { get; set; } = 60;

#if COTI_DEV
    /// <summary>
    /// Writes this many thermal-camera frames out as PNGs, with per-channel statistics, then stops.
    /// Reading the camera's own texture answers directly what a composited screenshot can only be
    /// used to guess at.
    /// </summary>
    [JsonProperty( "dumpFrames" )]
    public int DumpFrames { get; set; }
#endif

  }

  public class CotiNvgHostConfig
  {
    /// <summary>
    /// A label for log lines. Does not select a mask - the mask is generated from the geometry below.
    /// </summary>
    [JsonProperty( "maskName" )]
    public string MaskName { get; set; }

    /// <summary>
    /// Circle centre, normalised 0..1 across screen width.
    /// </summary>
    [JsonProperty( "maskCenterX" )]
    public float MaskCenterX { get; set; }

    /// <summary>
    /// Circle centre, normalised 0..1 down screen height.
    /// </summary>
    [JsonProperty( "maskCenterY" )]
    public float MaskCenterY { get; set; }

    /// <summary>
    /// Radius as a fraction of screen HEIGHT, which is what keeps the circle round on any aspect ratio.
    /// </summary>
    [JsonProperty( "maskRadius" )]
    public float MaskRadius { get; set; }

    /// <summary>
    /// Feather width, also a fraction of screen height.
    /// </summary>
    [JsonProperty( "maskFeather" )]
    public float MaskFeather { get; set; }

    [JsonProperty( "minimumTemperatureValue" )]
    public float MinimumTemperatureValue { get; set; }

    [JsonProperty( "mainTexColorCoef" )]
    public float MainTexColorCoef { get; set; }

    [JsonProperty( "depthFade" )]
    public float DepthFade { get; set; }

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
    public float UnsharpRadiusBlur { get; set; }

    /// <summary>
    /// ThermalVision.UnsharpBias, the edge-dominance lever. Vanilla default 2.
    /// </summary>
    [JsonProperty( "unsharpBias" )]
    public float UnsharpBias { get; set; }

    [JsonProperty( "overlayContrast" )]
    public float OverlayContrast { get; set; }

    [JsonProperty( "overlayExposure" )]
    public float OverlayExposure { get; set; }

    /// <summary>
    /// Diagnostic switch for the composite pass. 0 is the normal composite and must stay the
    /// default; an out-of-range value falls back to 0 rather than throwing.
    /// </summary>
    [JsonProperty( "compositeMode" )]
    public int CompositeMode { get; set; }

    /// <summary>
    /// Ramp palette mapping heat to colour - Fusion, Rainbow, WhiteHot, BlackHot. A string, not the
    /// game enum, because the shared half must not reference a game assembly. Empty leaves the player's
    /// current palette alone.
    /// </summary>
    [JsonProperty( "palette" )]
    public string Palette { get; set; }

    /// <summary>
    /// Shifts where the ramp palette is sampled. Vanilla default 0.
    /// </summary>
    [JsonProperty( "rampShift" )]
    public float RampShift { get; set; }

    /// <summary>
    /// Heat floor, 0..1. Anything cooler contributes EXACTLY zero, which is what lets the night
    /// vision image show through instead of the circle washing out.
    /// </summary>
    [JsonProperty( "heatThreshold" )]
    public float HeatThreshold { get; set; } = 0.75f;

    /// <summary>
    /// Crossfades solid hot shapes (0) against edge-only contours (1).
    /// </summary>
    [JsonProperty( "outlineMix" )]
    public float OutlineMix { get; set; }

    /// <summary>
    /// Contour thickness in texels of the thermal target, when OutlineMix &gt; 0.
    /// </summary>
    [JsonProperty( "outlineWidth" )]
    public float OutlineWidth { get; set; } = 1.5f;

    /// <summary>
    /// Overall brightness of the added heat.
    /// </summary>
    [JsonProperty( "overlayIntensity" )]
    public float OverlayIntensity { get; set; } = 1f;

    /// <summary>
    /// Transform on the host NVG to hang the COTI from. Empty means the host's root.
    /// CotiMountBonePatch logs the available names the first time it sees each host.
    /// </summary>
    [JsonProperty( "mountAnchorBone" )]
    public string MountAnchorBone { get; set; }

    /// <summary>
    /// Offset from the anchor in METRES - x right, y up, z forward. Positions the model's origin.
    /// </summary>
    [JsonProperty( "mountPositionX" )]
    public float MountPositionX { get; set; }

    [JsonProperty( "mountPositionY" )]
    public float MountPositionY { get; set; }

    [JsonProperty( "mountPositionZ" )]
    public float MountPositionZ { get; set; }

    /// <summary>
    /// Roll about the clamp ring's own axis, in degrees - the device rotating around the tube it
    /// grips. Applied pre-multiplied in the host's frame, NOT as a fourth Euler term: the mount
    /// rotation is already a fixed-order triple and folding a roll into it would not roll about
    /// the bore.
    /// </summary>
    [JsonProperty( "mountRollDegrees" )]
    public float MountRollDegrees { get; set; }

    /// <summary>
    /// Pitch in degrees, about the host's left-right axis. Pre-multiplied like roll.
    /// </summary>
    [JsonProperty( "mountPitchDegrees" )]
    public float MountPitchDegrees { get; set; }

    /// <summary>
    /// Yaw in degrees, about the host's vertical axis. Pre-multiplied like roll. The GPNVG-18 needs
    /// it: its outer tubes are canted outward.
    /// </summary>
    [JsonProperty( "mountYawDegrees" )]
    public float MountYawDegrees { get; set; }

    /// <summary>
    /// Euler angles in degrees, applied as the mount's local rotation.
    /// </summary>
    [JsonProperty( "mountRotationX" )]
    public float MountRotationX { get; set; }

    [JsonProperty( "mountRotationY" )]
    public float MountRotationY { get; set; }

    [JsonProperty( "mountRotationZ" )]
    public float MountRotationZ { get; set; }

    /// <summary>
    /// Uniform scale. The model is built at true scale, so anything but 1 is a per-host fudge.
    /// </summary>
    [JsonProperty( "mountScale" )]
    public float MountScale { get; set; } = 1f;
  }
}
