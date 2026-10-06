using System.Collections.Generic;
using Coti.Shared;
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
    /// Off by default because the COTI is an offset sensor, so a 1x thermal is what it would really
    /// produce. Costs a second scene render while aiming.
    /// </summary>
    [JsonProperty( "magnifyWithOptic" )]
    public bool MagnifyWithOptic { get; set; }

    [JsonProperty( "thermalCamera" )]
    public CotiCameraConfig ThermalCamera { get; set; } = new CotiCameraConfig();

    /// <summary>
    /// Thermal image tuning shared by every host - see <see cref="CotiImageConfig"/>. Global rather
    /// than per-host because the values are the same for every device.
    /// </summary>
    [JsonProperty( "image" )]
    public CotiImageConfig Image { get; set; } = new CotiImageConfig();

    [JsonProperty( "powerSequence" )]
    public CotiPowerSequenceConfig PowerSequence { get; set; } = new CotiPowerSequenceConfig();

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
    /// Sensor refresh in hertz - the real ECOTI's is 60. The thermal cameras render only on frames due at this rate
    /// (CotiSensorPacer) and the last picture stands between, over a night-vision picture running at full framerate,
    /// which also saves the skipped scene passes. 0 renders every frame.
    /// </summary>
    [JsonProperty( "hz" )]
    public int Hz { get; set; } = 60;

    /// <summary>
    /// How far the 1x thermal draws, in metres; through a magnified scope, this times the zoom, since a target that
    /// far through the scope looks as near. 0 draws as far as the eye's own camera does (the player's Overall
    /// Visibility). Never further than the eye.
    /// </summary>
    public float RangeMetres { get; set; }

#if COTI_DEV
    /// <summary>
    /// Writes this many thermal-camera frames out as PNGs with per-channel statistics, then stops.
    /// </summary>
    [JsonProperty( "dumpFrames" )]
    public int DumpFrames { get; set; }

    /// <summary>
    /// The magnified lens redraw, taken apart for diagnosis: 0 as shipped, 1 adds the heat texture straight to the
    /// screen, 2 draws the lens mesh with the overlay's copy pass instead of the lens shader, 3 redraws with culling
    /// inverted.
    /// </summary>
    public int LensProbe { get; set; }
#endif

  }

  /// <summary>
  /// The F12 "Power Sequence" section. Enabled false restores the instant CTRL+N toggle.
  /// </summary>
  public class CotiPowerSequenceConfig
  {
    public bool Enabled { get; set; } = true;
    public float InitializingSeconds { get; set; } = 1.2f;
    public float ModeSeconds { get; set; } = 0.75f;
    public float WarmingSeconds { get; set; } = 0.3f;
    public float PowerOffSeconds { get; set; } = 1.5f;

    /// <summary>
    /// On top of the game's own volume settings. 0 mutes the click.
    /// </summary>
    public float ClickVolume { get; set; } = 1f;

    /// <summary>
    /// Into an existing instance rather than a new one: this runs every frame.
    /// </summary>
    public void CopyTo( CotiPowerTimings target )
    {
      target.InitializingSeconds = InitializingSeconds;
      target.ModeSeconds = ModeSeconds;
      target.WarmingSeconds = WarmingSeconds;
      target.PowerOffSeconds = PowerOffSeconds;
    }
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
    /// Radius as a fraction of screen height, which keeps the circle round on any aspect ratio.
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
    /// Offset from the anchor in metres - x right, y up, z forward. Positions the model's origin.
    /// </summary>
    [JsonProperty( "mountPositionX" )]
    public float MountPositionX { get; set; }

    [JsonProperty( "mountPositionY" )]
    public float MountPositionY { get; set; }

    [JsonProperty( "mountPositionZ" )]
    public float MountPositionZ { get; set; }

    /// <summary>
    /// Roll about the clamp ring's own axis, in degrees - the device rotating around the tube it
    /// grips. Applied pre-multiplied in the host's frame rather than as a fourth Euler term: the mount
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

    /// <summary>
    /// The same fields in the shape CotiMountTransform takes.
    /// </summary>
    public CotiMountBlock ToMountBlock()
    {
      return new CotiMountBlock
      {
        AnchorBone = MountAnchorBone,
        PositionX = MountPositionX,
        PositionY = MountPositionY,
        PositionZ = MountPositionZ,
        RotationX = MountRotationX,
        RotationY = MountRotationY,
        RotationZ = MountRotationZ,
        RollDegrees = MountRollDegrees,
        PitchDegrees = MountPitchDegrees,
        YawDegrees = MountYawDegrees,
        Scale = MountScale,
      };
    }

    /// <summary>
    /// The layout of a multi-tube device, or null on a v1 device, which has mod_coti alone, posed
    /// from the Mount fields above.
    /// </summary>
    public CotiLayout? Layout { get; set; }

    /// <summary>
    /// Keyed by every label of the layout: the mount each tube uses (its own, or the legacy mount
    /// when the file did not pose it, as CotiTubeValidation.MountFor decides) and its pod. Empty on
    /// a v1 device.
    /// </summary>
    public Dictionary<string, CotiTube> Tubes { get; } = new Dictionary<string, CotiTube>();

    /// <summary>Where a v1 device places its one circle's messages. Null keeps the rule.</summary>
    public CotiTextBlock? Text { get; set; }

    /// <summary>Where a tube's messages sit; a null label is a v1 device's one circle. No allocation, so per frame is fine.</summary>
    public CotiTextPlacement TextPlacement( string? label )
    {
      if( label == null )
        return CotiDisplayLayout.Resolve( Text, null );

      return CotiDisplayLayout.Resolve( Tubes.TryGetValue( label, out var tube ) ? tube.Text : null, label );
    }

    /// <summary>The COTI slots this device has, in auto-pick order.</summary>
    public IReadOnlyList<string> SlotNames => CotiTubes.SlotNames( Layout );

    /// <summary>The tube behind a slot, or null on a v1 device and for a slot outside the layout.</summary>
    public CotiTube? TubeForSlot( string slotName )
    {
      var label = CotiTubes.LabelOfSlot( slotName, Layout );
      return label != null && Tubes.TryGetValue( label, out var tube ) ? tube : null;
    }

    /// <summary>
    /// The pose of a slot's bone. A slot with no tube mounts at the Mount fields: the v1 behaviour,
    /// and where a COTI left in a slot after its file went back to v1 ends up.
    /// </summary>
    public CotiMountBlock MountForSlot( string slotName )
    {
      return TubeForSlot( slotName )?.Mount ?? ToMountBlock();
    }

    /// <summary>
    /// One device file flattened for the runtime. MaskName is the device's own name - a label for
    /// logs, not a mask selector.
    /// </summary>
    public static CotiNvgHostConfig FromDevice( CotiDeviceFile device )
    {
      var mask = device.Mask ?? new CotiMaskBlock();
      var mount = device.Mount ?? new CotiMountBlock();

      var config = new CotiNvgHostConfig
      {
        MaskName = device.Device,
        MaskCenterX = mask.CenterX,
        MaskCenterY = mask.CenterY,
        MaskRadius = mask.Radius,
        MaskFeather = mask.Feather,
        MountAnchorBone = mount.AnchorBone,
        MountPositionX = mount.PositionX,
        MountPositionY = mount.PositionY,
        MountPositionZ = mount.PositionZ,
        MountRotationX = mount.RotationX,
        MountRotationY = mount.RotationY,
        MountRotationZ = mount.RotationZ,
        MountRollDegrees = mount.RollDegrees,
        MountPitchDegrees = mount.PitchDegrees,
        MountYawDegrees = mount.YawDegrees,
        MountScale = mount.Scale,
        Text = device.Text,
      };

      // Not validated again here: the server's table went through CotiDeviceMerge, and the embedded
      // fallback is the shipped files, which CotiShippedDevicesTests validates.
      if( !device.IsMultiTube || !CotiLayouts.TryGet( device.Layout, out var layout ) )
        return config;

      config.Layout = layout;

      foreach( var tube in layout.Tubes )
      {
        var declared = device.Tubes != null && device.Tubes.TryGetValue( tube.Label, out var found ) ? found : null;

        config.Tubes[tube.Label] = new CotiTube
        {
          Mount = CotiTubeValidation.MountFor( device, tube.Label ),
          Pod = declared?.Pod,
          Text = declared?.Text,
        };
      }

      return config;
    }
  }
}
