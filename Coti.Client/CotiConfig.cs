using System.Collections.Generic;
using Newtonsoft.Json;

namespace Coti.Client
{
  /// <summary>
  /// The complete COTI configuration. Global defaults live as this class's field initialisers;
  /// per-host mask and mount geometry comes from hosts/*.json; the player's settings come from F12.
  /// </summary>
  public class CotiConfig
  {
    /// <summary>
    /// Keyed by host NVG template id.
    /// </summary>
    [JsonProperty( "nvgHosts" )]
    public Dictionary<string, CotiNvgHostConfig> NvgHosts { get; set; } = new Dictionary<string, CotiNvgHostConfig>();

    public bool Enabled { get; set; } = true;

    [JsonProperty( "verboseLogging" )]
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Arms the tuner's keyboard shortcut (arrows/,./[]/;'/-=). The pose editor's own on-screen
    /// buttons, opened from the inspect window's COTI Pose button, work regardless of this setting -
    /// it only gates the raw keys, which are otherwise free ones EFT does not bind.
    /// </summary>
    public bool EnablePoseModifier { get; set; }

    /// <summary>
    /// Millimetres per tuner keypress or pose editor button press.
    /// </summary>
    public float TunerStepMm { get; set; } = 2f;

    /// <summary>
    /// Degrees per tuner keypress or pose editor button press.
    /// </summary>
    public float TunerStepDegrees { get; set; } = 5f;

    /// <summary>
    /// Scale added per tuner keypress or pose editor button press, as a fraction. 0.01 is one
    /// percent of the model's true size, which on an 87 mm device is just under a millimetre - about
    /// the resolution the clamp ring's fit against a tube housing can be judged by eye.
    /// </summary>
    public float TunerStepScale { get; set; } = 0.01f;

    /// <summary>
    /// Whether the pose editor's preview gets its own light. On by default because without it the
    /// model renders as a flat black silhouette. Switchable because the light is culled to the
    /// same layers as the camera, and those layers carry the game's own inspect model, so it may
    /// brighten EFT's inspect view as a side effect.
    /// </summary>
    public bool TunerPreviewLight { get; set; } = true;

    /// <summary>
    /// Modifier held while using the tuner keys, as "+"-separated KeyCode names.
    /// </summary>
    public string TunerModifier { get; set; } = "LeftControl+LeftAlt";

    /// <summary>
    /// Renders a second thermal pass matched to a magnified optic, so heat lines up with the scope.
    ///
    /// Off by default as a position, not caution: the COTI is an offset sensor, so a 1x thermal is
    /// what it would really produce. Costs a second scene render while aiming.
    /// </summary>
    [JsonProperty( "magnifyWithOptic" )]
    public bool MagnifyWithOptic { get; set; }

    [JsonProperty( "thermalCamera" )]
    public CotiCameraConfig ThermalCamera { get; set; } = new CotiCameraConfig();

    /// <summary>
    /// Thermal image tuning shared by every host - see <see cref="CotiImageConfig"/>. Global rather
    /// than per-host because the values it carries were byte-identical across every device.
    /// </summary>
    [JsonProperty( "image" )]
    public CotiImageConfig Image { get; set; } = new CotiImageConfig();

    /// <summary>
    /// Mask used when a host has no entry, or its named mask is missing.
    /// </summary>
    public const string FallbackMaskName = "centre";

    public static CotiConfig Fallback => new CotiConfig();
  }

  public class CotiCameraConfig
  {
    /// <summary>
    /// Master switch for the second camera, with no F12 entry. Must default true or a fresh install
    /// renders no thermal picture.
    /// </summary>
    [JsonProperty( "enabled" )]
    public bool Enabled { get; set; } = true;

    // Must stay one of CotiSensorResolutions.All's values - CotiF12Config seeds the
    // "Sensor Resolution (rows)" bind from Height.
    [JsonProperty( "width" )]
    public int Width { get; set; } = 1536;

    /// <summary>
    /// Render target height. The ECOTI's real sensor height.
    /// </summary>
    [JsonProperty( "height" )]
    public int Height { get; set; } = 1152;

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
    /// Writes this many thermal-camera frames out as PNGs with per-channel statistics, then stops.
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
