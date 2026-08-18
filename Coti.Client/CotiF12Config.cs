using Coti.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;

namespace Coti.Client
{
  public class CotiF12Config
  {
    private const string DefaultsResource = "Coti.Client.Assets.coti-defaults.json";

    private readonly ConfigFile _file;
    private readonly List<Action> _appliers = new List<Action>();

    public CotiConfig Current { get; } = new CotiConfig();

    public ConfigEntry<KeyboardShortcut> PowerToggle { get; private set; }

    public CotiF12Config( ConfigFile file )
    {
      _file = file;

      var defaults = LoadDefaults() ?? new CotiConfig();

      // Everything measured, loaded wholesale and left alone: per-host mask geometry, mount
      // poses, the rotation basis, and the thermal camera's own settings.
      Current.ThermalCamera = defaults.ThermalCamera;

      foreach( var host in CotiNvgHosts.All )
      {
        Current.NvgHosts[host.TemplateId] = defaults.NvgHosts.TryGetValue( host.TemplateId, out var d )
            ? d
            : new CotiNvgHostConfig { MaskName = host.MaskName };
      }

      BindImage( defaults );
      BindControls();
      BindDebug( defaults );

      Apply();

      // One handler for the file rather than one per entry.
      _file.SettingChanged += ( _, __ ) => Apply();
    }

    private void Apply()
    {
      foreach( var applier in _appliers )
        applier();
    }

    private void BindImage( CotiConfig defaults )
    {
      var sample = FirstHost( defaults );

      var enabled = _file.Bind( "Image", "Enabled", defaults.Enabled, new ConfigDescription(
          "Master switch for the thermal overlay." ) );

      var threshold = _file.Bind( "Image", "Heat Threshold", sample.HeatThreshold, new ConfigDescription(
          "How hot something must be before it shows. Anything cooler contributes nothing, which " +
          "is what lets the night vision image show through - raise this if the overlay washes " +
          "the picture out, lower it to pick up cooler things.",
          new AcceptableValueRange<float>( 0f, 1f ) ) );

      var intensity = _file.Bind( "Image", "Overlay Intensity", sample.OverlayIntensity, new ConfigDescription(
          "Brightness of the heat that does show. Lower this if hot bodies read as solid blobs " +
          "rather than shapes.",
          new AcceptableValueRange<float>( 0f, 20f ) ) );

      var outline = _file.Bind( "Image", "Outline Mix", sample.OutlineMix, new ConfigDescription(
          "0% is solid hot shapes, 100% is edge-only contours.",
          new AcceptableValueRange<float>( 0f, 1f ) ) );

      var rows = _file.Bind( "Image", "Sensor Resolution (rows)", Current.ThermalCamera.Height,
          new ConfigDescription(
              "Vertical resolution of the thermal render. Raising it is what recovers DISTANT " +
              "contacts: a target far enough away covers only a fraction of a texel, its heat is " +
              "averaged with the cold background, and below about a quarter coverage it falls under " +
              "the heat threshold and contributes nothing at all - so it dims, flickers as it moves, " +
              "and eventually vanishes. At 576 a man at 300 m already swings between a fifth of full " +
              "brightness and full brightness depending on where he lands on the texel grid. Costs " +
              "fill rate, which measurement says is not the bottleneck - the second camera is bound " +
              "by CPU-side culling and draw submission, and those do not change with resolution. " +
              "Contour thickness is scaled to match, so the picture keeps its look. The values BELOW " +
              "576 are a test aid rather than a setting: angular size in texels is (size / range) x " +
              "(rows / fov), so halving the rows is identical to doubling the range. 288 at 50 m " +
              "shows what 576 does at 100 m, which is how the failure can be provoked on a sightline " +
              "you actually have.",
              new AcceptableValueList<int>( CotiSensorResolutions.All ) ) );

      var hz = _file.Bind( "Image", "Sensor Refresh (Hz)", Current.ThermalCamera.Hz, new ConfigDescription(
          "The sensor's simulated refresh. The thermal image is captured at this rate and the " +
          "held copy is re-blitted in between, which is what a low-refresh core looks like. Set " +
          "0 to disable the hold entirely and capture every frame. This is NOT a render cap and " +
          "costs no extra rendering either way - the camera renders every frame regardless.",
          new AcceptableValueRange<int>( 0, 240 ) ) );

      var magnify = _file.Bind( "Image", "Magnify With Optic", defaults.MagnifyWithOptic,
          new ConfigDescription(
              "Renders a second thermal pass matched to a magnified scope, so heat lines up with " +
              "what the scope shows instead of with the 1x view around it, and keeps the 1x heat " +
              "off the lens. Off by default: the COTI is an offset sensor looking downrange on its " +
              "own axis, so a 1x thermal is what it would really produce. Costs a second scene " +
              "render while aiming. Non-magnified sights are unaffected either way. If Borkel's " +
              "scope blur is on, this spends that render aligning heat onto a deliberately blurred " +
              "picture." ) );

      var lensCover = _file.Bind( "Image", "Magnified Lens Cover", defaults.MagnifiedLensCover,
          new ConfigDescription(
              "How much of the scope lens the 1x heat is kept off while the above is on, as a " +
              "multiple of the lens's measured size. Lower it if the cleared area spills past the " +
              "scope body; 0 leaves the 1x heat painted over the lens as before. Does nothing with " +
              "Magnify With Optic off.",
              new AcceptableValueRange<float>( 0f, 2f ) ) );

      _appliers.Add( () =>
      {
        Current.Enabled = enabled.Value;
        Current.MagnifyWithOptic = magnify.Value;
        Current.MagnifiedLensCover = lensCover.Value;

        foreach( var host in Current.NvgHosts.Values )
        {
          host.HeatThreshold = threshold.Value;
          host.OverlayIntensity = intensity.Value;
          host.OutlineMix = outline.Value;
        }

        Current.ThermalCamera.Hz = hz.Value;

        // Width follows height at the sensor's own 4:3 ratio, so one control cannot leave the two
        // inconsistent. EnsureRenderTexture already reallocates only when the size really changes.
        Current.ThermalCamera.Height = rows.Value;
        Current.ThermalCamera.Width = CotiSensorResolutions.WidthFor( rows.Value );
      } );
    }

    private void BindControls()
    {
      PowerToggle = _file.Bind( "Controls", "Power Toggle",
          new KeyboardShortcut( KeyCode.N, KeyCode.LeftControl ),
          new ConfigDescription(
              "Switches the ECOTI on and off without touching the night vision device. Keep a " +
              "modifier: EFT does not require an exact match on its own binds, so a bare N would " +
              "toggle the goggles as well." ) );
    }

    private void BindDebug( CotiConfig defaults )
    {
      var verbose = _file.Bind( "Debug", "Verbose Logging", defaults.VerboseLogging,
          "Writes detailed diagnostics to the BepInEx log." );

#if COTI_DEV
      var poseModifier = _file.Bind( "Debug", "Enable Pose Modifier", false, new ConfigDescription(
          "Arms the mount tuner, which moves the ECOTI on the night vision device while you " +
          "have it open in the inventory. Off by default: it binds keys that are otherwise " +
          "free, and nothing it does is saved." ) );

      var modifier = _file.Bind( "Debug", "Tuner Modifier", defaults.TunerModifier ?? "LeftControl+LeftAlt",
          new ConfigDescription(
              "Held while using the tuner keys, then arrows to move, PageUp/PageDown for depth, " +
              ",/. to roll, [/] to pitch, ;/' to yaw, -/= to scale." ) );

      var stepMm = _file.Bind( "Debug", "Tuner Step (mm)", defaults.TunerStepMm,
          "Distance per keypress. Hold Shift while tuning for a quarter of this." );

      var stepDegrees = _file.Bind( "Debug", "Tuner Step (degrees)", defaults.TunerStepDegrees,
          "Rotation per keypress. Hold Shift while tuning for a fifth of this." );

      var dumpFrames = _file.Bind( "Debug", "Dump Frames", 0, new ConfigDescription(
          "Writes this many frames of every thermal render target to coti-dumps/ as PNGs, with " +
          "per-channel statistics in the log, then stops. Change the number to start a fresh batch. " +
          "Live, because the alternative was a rebuild per attempt: dumpFrames is a compiled-in " +
          "default and nothing else on this panel can reach it. The channel MEANS are the point - " +
          "under a grayscale palette a real thermal render has neutral means and a lit one is " +
          "colour-cast, which is the check four in-raid screenshot comparisons could not settle.",
          new AcceptableValueRange<int>( 0, 30 ) ) );

      var stepScale = _file.Bind( "Debug", "Tuner Step (scale)", defaults.TunerStepScale,
          new ConfigDescription(
              "Uniform scale per keypress, as a fraction of true size. Scaling grows the device " +
              "around the mount bone, so the position needs a small re-nudge afterwards - scale " +
              "first, then position. Hold Shift while tuning for a quarter of this.",
              new AcceptableValueRange<float>( 0.001f, 0.5f ) ) );
#endif

      _appliers.Add( () =>
      {
        Current.VerboseLogging = verbose.Value;
#if COTI_DEV
        Current.EnablePoseModifier = poseModifier.Value;
        Current.TunerModifier = modifier.Value;
        Current.TunerStepMm = stepMm.Value;
        Current.TunerStepDegrees = stepDegrees.Value;
        Current.TunerStepScale = stepScale.Value;
        Current.ThermalCamera.DumpFrames = dumpFrames.Value;
#endif
      } );
    }

    /// <summary>
    /// An NVG host to read the shared image defaults from. They are the same across devices, so the
    /// first one is representative; falling back to a fresh config keeps its property initialisers
    /// rather than binding zeroes if the defaults ever fail to load.
    /// </summary>
    private static CotiNvgHostConfig FirstHost( CotiConfig defaults )
    {
      foreach( var host in CotiNvgHosts.All )
      {
        if( defaults.NvgHosts.TryGetValue( host.TemplateId, out var found ) )
          return found;
      }

      return new CotiNvgHostConfig();
    }

    private static CotiConfig LoadDefaults()
    {
      try
      {
        var assembly = Assembly.GetExecutingAssembly();

        using( var stream = assembly.GetManifestResourceStream( DefaultsResource ) )
        {
          if( stream == null )
          {
            Debug.LogError( $"[COTI] Embedded defaults '{DefaultsResource}' missing - falling back to code defaults" );
            return null;
          }

          using( var reader = new StreamReader( stream ) )
          {
            return JsonConvert.DeserializeObject<CotiConfig>( reader.ReadToEnd() );
          }
        }
      }
      catch( Exception ex )
      {
        Debug.LogError( $"[COTI] Could not read embedded defaults, falling back to code defaults: {ex.Message}" );
        return null;
      }
    }
  }
}
