using Coti.Shared;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Coti.Client
{
  public class CotiF12Config
  {
    private readonly ConfigFile _file;
    private readonly List<Action> _appliers = new List<Action>();

    public CotiConfig Current { get; } = new CotiConfig();

    public ConfigEntry<KeyboardShortcut> PowerToggle { get; private set; }

    /// <summary>
    /// hostFallback replaces the old CotiNvgHosts.All - per-host mask and mount geometry now
    /// comes from device files (hosts/*.json, embedded as the offline fallback and overtaken by
    /// CotiHostTableClient's fetch once it lands), not from a compiled-in table. This constructor
    /// only needs a synchronous seed for the geometry the mask generator and mount patches read
    /// immediately; CotiHostTableClient.Apply is what also runs the slot patch, from Update, once
    /// the game's own singletons are up.
    /// </summary>
    public CotiF12Config( ConfigFile file, IReadOnlyList<CotiDeviceFile> hostFallback )
    {
      _file = file;

      // A plain CotiConfig() carries the field initialisers, which are the defaults.
      var defaults = new CotiConfig();

      foreach( var device in hostFallback )
      {
        if( device?.Hosts == null )
          continue;

        foreach( var host in device.Hosts )
        {
          if( string.IsNullOrEmpty( host?.Id ) )
            continue;

          Current.NvgHosts[host.Id] = CotiHostTableClient.ToHostConfig( device );
        }
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
      var image = defaults.Image ?? new CotiImageConfig();

      var enabled = _file.Bind( "Image", "Enabled", defaults.Enabled, new ConfigDescription(
          "Master switch for the thermal overlay." ) );

      var threshold = _file.Bind( "Image", "Heat Threshold", image.HeatThreshold, new ConfigDescription(
          "How hot something must be before it shows. Anything cooler contributes nothing, which " +
          "is what lets the night vision image show through - raise this if the overlay washes " +
          "the picture out, lower it to pick up cooler things.",
          new AcceptableValueRange<float>( 0f, 1f ) ) );

      var intensity = _file.Bind( "Image", "Overlay Intensity", image.OverlayIntensity, new ConfigDescription(
          "Brightness of the heat that does show. Lower this if hot bodies read as solid blobs " +
          "rather than shapes.",
          new AcceptableValueRange<float>( 0f, 20f ) ) );

      var magnifiedScale = _file.Bind( "Image", "Magnified Intensity Scale",
          image.MagnifiedIntensityScale, new ConfigDescription(
              "Fraction of Overlay Intensity used inside a magnified scope. Lower it if hot edges " +
              "there read as a solid mass.",
              new AcceptableValueRange<float>( 0.05f, 1f ) ) );

      var outline = _file.Bind( "Image", "Outline Mix", image.OutlineMix, new ConfigDescription(
          "0% is solid hot shapes, 100% is edge-only contours.",
          new AcceptableValueRange<float>( 0f, 1f ) ) );

      var outlineWidth = _file.Bind( "Image", "Outline Width", image.OutlineWidth, new ConfigDescription(
          "Contour thickness in texels of the thermal target, when Outline Mix is above 0.",
          new AcceptableValueRange<float>( 0.5f, 8f ), new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var minimumTemperature = _file.Bind( "Image", "Minimum Temperature Value", image.MinimumTemperatureValue,
          new ConfigDescription(
              "ThermalVisionUtilities.ValuesCoefs.MinimumTemperatureValue - the game's own thermal " +
              "floor coefficient.",
              null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var mainTexColorCoef = _file.Bind( "Image", "Main Tex Color Coef", image.MainTexColorCoef,
          new ConfigDescription(
              "ThermalVisionUtilities.ValuesCoefs.MainTexColorCoef - the game's own thermal colour " +
              "coefficient.",
              null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var depthFade = _file.Bind( "Image", "Depth Fade", image.DepthFade, new ConfigDescription(
          "ThermalVisionUtilities.DepthFade - the game's own depth-based fade.",
          null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var isPixelated = _file.Bind( "Image", "Pixelated", image.IsPixelated, new ConfigDescription(
          "ThermalVision.IsPixelated.", null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var isNoisy = _file.Bind( "Image", "Noisy", image.IsNoisy, new ConfigDescription(
          "ThermalVision.IsNoisy.", null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var isMotionBlurred = _file.Bind( "Image", "Motion Blurred", image.IsMotionBlurred, new ConfigDescription(
          "ThermalVision.IsMotionBlurred.", null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var unsharpRadiusBlur = _file.Bind( "Image", "Unsharp Radius Blur", image.UnsharpRadiusBlur,
          new ConfigDescription(
              "ThermalVision.UnsharpRadiusBlur. Vanilla default 5.",
              null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var unsharpBias = _file.Bind( "Image", "Unsharp Bias", image.UnsharpBias, new ConfigDescription(
          "ThermalVision.UnsharpBias, the edge-dominance lever. Vanilla default 2.",
          null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var palette = _file.Bind( "Image", "Palette", image.Palette, new ConfigDescription(
          "Ramp palette mapping heat to colour - Fusion, Rainbow, WhiteHot, BlackHot. Empty leaves " +
          "the current palette alone.",
          null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var rampShift = _file.Bind( "Image", "Ramp Shift", image.RampShift, new ConfigDescription(
          "Shifts where the ramp palette is sampled. Vanilla default 0.",
          null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

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

      _appliers.Add( () =>
      {
        Current.Enabled = enabled.Value;
        Current.MagnifyWithOptic = magnify.Value;

        Current.Image.HeatThreshold = threshold.Value;
        Current.Image.OverlayIntensity = intensity.Value;
        Current.Image.MagnifiedIntensityScale = magnifiedScale.Value;
        Current.Image.OutlineMix = outline.Value;
        Current.Image.OutlineWidth = outlineWidth.Value;
        Current.Image.MinimumTemperatureValue = minimumTemperature.Value;
        Current.Image.MainTexColorCoef = mainTexColorCoef.Value;
        Current.Image.DepthFade = depthFade.Value;
        Current.Image.IsPixelated = isPixelated.Value;
        Current.Image.IsNoisy = isNoisy.Value;
        Current.Image.IsMotionBlurred = isMotionBlurred.Value;
        Current.Image.UnsharpRadiusBlur = unsharpRadiusBlur.Value;
        Current.Image.UnsharpBias = unsharpBias.Value;
        Current.Image.Palette = palette.Value;
        Current.Image.RampShift = rampShift.Value;

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

      var poseModifier = _file.Bind( "Debug", "Enable Pose Modifier", false, new ConfigDescription(
          "Arms the tuner's keyboard shortcut, which moves the ECOTI on the night vision device " +
          "while you have it open in the inventory. Off by default: it binds keys that are " +
          "otherwise free. The pose editor's own on-screen buttons, opened from the inspect " +
          "window's COTI Pose button, work regardless of this setting." ) );

      var modifier = _file.Bind( "Debug", "Tuner Modifier", defaults.TunerModifier ?? "LeftControl+LeftAlt",
          new ConfigDescription(
              "Held while using the tuner keys, then arrows to move, PageUp/PageDown for depth, " +
              ",/. to roll, [/] to pitch, ;/' to yaw, -/= to scale." ) );

      var stepMm = _file.Bind( "Debug", "Tuner Step (mm)", defaults.TunerStepMm,
          "Distance per keypress or pose editor button press. Hold Shift while tuning for a " +
          "quarter of this." );

      var stepDegrees = _file.Bind( "Debug", "Tuner Step (degrees)", defaults.TunerStepDegrees,
          "Rotation per keypress or pose editor button press. Hold Shift while tuning for a fifth " +
          "of this." );

      var stepScale = _file.Bind( "Debug", "Tuner Step (scale)", defaults.TunerStepScale,
          new ConfigDescription(
              "Uniform scale per keypress or pose editor button press, as a fraction of true size. " +
              "Scaling grows the device around the mount bone, so the position needs a small " +
              "re-nudge afterwards - scale first, then position. Hold Shift while tuning for a " +
              "quarter of this.",
              new AcceptableValueRange<float>( 0.001f, 0.5f ) ) );

#if COTI_DEV
      var dumpFrames = _file.Bind( "Debug", "Dump Frames", 0, new ConfigDescription(
          "Writes this many frames of every thermal render target to coti-dumps/ as PNGs, with " +
          "per-channel statistics in the log, then stops. Change the number to start a fresh batch. " +
          "Live, because the alternative was a rebuild per attempt: dumpFrames is a compiled-in " +
          "default and nothing else on this panel can reach it. The channel MEANS are the point - " +
          "under a grayscale palette a real thermal render has neutral means and a lit one is " +
          "colour-cast, which is the check four in-raid screenshot comparisons could not settle.",
          new AcceptableValueRange<int>( 0, 30 ) ) );
#endif

      // An action rather than a setting, so the drawer replaces the usual editor with a button.
      // The bound value is never read: CotiMaskPanel owns whether it is open, because the window
      // has its own Close button and hotkey and has to be able to shut itself without this menu
      // being on screen. Deliberately NOT under Debug and not IsAdvanced - it is the only entry
      // point to the editor, so hiding it would hide the feature.
      _file.Bind( "Mask Editor", "Open", false, new ConfigDescription(
          "Opens the thermal circle editor. It stays open after this menu closes, so you can drop " +
          "your goggles and adjust the circle while looking through them.",
          null,
          new ConfigurationManagerAttributes
          {
            HideDefaultButton = true,
            CustomDrawer = _ =>
            {
              if( !GUILayout.Button( CotiMaskPanel.IsOpen ? "Close mask editor" : "Open mask editor",
                      GUILayout.ExpandWidth( true ) ) )
                return;

              if( CotiMaskPanel.IsOpen )
                CotiMaskPanel.Close();
              else
                CotiMaskPanel.Open();
            },
          } ) );

      var previewLight = _file.Bind( "Debug", "Pose Preview Light", true, new ConfigDescription(
          "Lights the pose editor's preview. Without it the model renders as a flat black " +
          "silhouette. Turn it off if it visibly brightens the game's own inspect view - the " +
          "light has to share the inspect model's layers, so that is a possible side effect.",
          null, new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      _appliers.Add( () =>
      {
        Current.VerboseLogging = verbose.Value;
        Current.TunerPreviewLight = previewLight.Value;
        Current.EnablePoseModifier = poseModifier.Value;
        Current.TunerModifier = modifier.Value;
        Current.TunerStepMm = stepMm.Value;
        Current.TunerStepDegrees = stepDegrees.Value;
        Current.TunerStepScale = stepScale.Value;
#if COTI_DEV
        Current.ThermalCamera.DumpFrames = dumpFrames.Value;
#endif
      } );
    }
  }
}
