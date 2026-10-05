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

    public ConfigEntry<KeyboardShortcut> ModeToggle { get; private set; }

    public ConfigEntry<CotiThermalMode> ThermalMode { get; private set; }

    /// <summary>
    /// Per-host mask and mount geometry comes from device files (hosts/*.json, embedded as the
    /// offline fallback and replaced by CotiHostTableClient's fetch once it lands). hostFallback is
    /// the synchronous seed for the geometry the mask generator and mount patches read immediately;
    /// CotiHostTableClient.Apply runs the slot patch from Update once the game's singletons are up.
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
      BindPowerSequence( defaults );
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

      var fill = _file.Bind( "Image", "Full Mode Fill (%)", image.FullFillPercent, new ConfigDescription(
          "How bright Full mode fills a hot shape inside its outline. 100 is a solid shape; Outline mode " +
          "ignores this.",
          new AcceptableValueRange<float>( 0f, 100f ) ) );

      // 3.2.0 wrote 55 into every install; it becomes 45 once, and a value set afterwards stands.
      var fillMoved = _file.Bind( "Image", "Full Mode Fill Moved", false, new ConfigDescription(
          "Set once the 3.2.0 fill default has been moved to 45.", null,
          new ConfigurationManagerAttributes { Browsable = false } ) );
      if( !fillMoved.Value )
      {
        fill.Value = CotiThermalModes.MovedFill( fill.Value );
        fillMoved.Value = true;
      }

      var glass = _file.Bind( "Image", "Glass", image.Glass, new ConfigDescription(
          "Glass blocks heat. Plain shows it blank. Reflections also mirrors the bodies in front of it, " +
          "you included, faintly face-on and strongly at a shallow angle, as real glass does to a thermal imager." ) );

      var heatBrightness = _file.Bind( "Image", "Heat Brightness", image.HeatBrightness, new ConfigDescription(
          "A multiplier on how bright heat signatures draw. 1 is the default; lower dims the image, higher brightens it.",
          new AcceptableValueRange<float>( 0.25f, 2f ) ) );

      var flashlightHeat = _file.Bind( "Image", "Render Flashlight Heat", image.RenderFlashlightHeat, new ConfigDescription(
          "A lit flashlight's head shows faintly warm, from its lens back. Only visible light: lasers and infrared " +
          "illuminators stay cold. Off, every flashlight reads cold." ) );

      var searchlightHeat = _file.Bind( "Image", "Render Searchlight Heat", image.RenderSearchlightHeat, new ConfigDescription(
          "A lit searchlight shows hot at its lens and warm around it; every other lamp stays cold. Off, searchlights " +
          "read cold too." ) );

      var outlineThickness = _file.Bind( "Image", "Outline Thickness (cm)", image.OutlineThicknessCm, new ConfigDescription(
          "The outline's thickness on the object itself. Projected through the view like the object, so it thins with " +
          "distance and thickens under magnification, never past Outline Width. 0 keeps a fixed width.",
          new AcceptableValueRange<float>( 0f, 20f ), new ConfigurationManagerAttributes { IsAdvanced = true } ) );

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
              "Vertical resolution of the thermal image. Higher values keep distant targets visible: " +
              "a target smaller than one pixel of the sensor blends into the cold background and can " +
              "flicker or vanish. Contour thickness scales to match. Values below 576 are for testing: " +
              "halving the rows shows what the default does at twice the range.",
              new AcceptableValueList<int>( CotiSensorResolutions.All ) ) );

      var hz = _file.Bind( "Image", "Sensor Refresh (Hz)", Current.ThermalCamera.Hz, new ConfigDescription(
          "The sensor's simulated refresh. The thermal image updates at this rate and holds in " +
          "between, as a real low-refresh core does, and the frames between cost nothing. 0 updates every frame.",
          new AcceptableValueRange<int>( 0, 240 ) ) );

      var range = _file.Bind( "Image", "Thermal Range (m)", Current.ThermalCamera.RangeMetres, new ConfigDescription(
          "How far the thermal draws at 1x. Through a magnified scope this is multiplied by the zoom, since a target " +
          "that far through the scope looks as near. 0 draws as far as your eyes do (Overall Visibility). Shorter " +
          "is cheaper.",
          new AcceptableValueRange<float>( 0f, 3000f ), new ConfigurationManagerAttributes { IsAdvanced = true } ) );

      var magnify = _file.Bind( "Image", "Magnify With Optic", defaults.MagnifyWithOptic,
          new ConfigDescription(
              "Renders a second thermal pass matched to a magnified scope, so heat lines up with " +
              "what the scope shows instead of with the 1x view around it, and keeps the 1x heat " +
              "off the lens. Off by default: the COTI is an offset sensor looking downrange on its " +
              "own axis, so a 1x thermal is what it would really produce. Non-magnified sights are " +
              "unaffected either way. With Borkel's scope blur on, the heat is aligned onto the blurred " +
              "scope picture. Potential FPS improvement: leave it off, since on renders the scene a " +
              "second time while aiming." ) );

      _appliers.Add( () =>
      {
        Current.Enabled = enabled.Value;
        Current.MagnifyWithOptic = magnify.Value;

        Current.Image.HeatThreshold = threshold.Value;
        Current.Image.OverlayIntensity = intensity.Value;
        Current.Image.MagnifiedIntensityScale = magnifiedScale.Value;
        Current.Image.FullFillPercent = fill.Value;
        Current.Image.OutlineWidth = outlineWidth.Value;
        Current.Image.OutlineThicknessCm = outlineThickness.Value;
        Current.Image.Glass = glass.Value;
        Current.Image.HeatBrightness = heatBrightness.Value;
        Current.Image.RenderFlashlightHeat = flashlightHeat.Value;
        Current.Image.RenderSearchlightHeat = searchlightHeat.Value;
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
        Current.ThermalCamera.RangeMetres = range.Value;

        // Width follows height at the sensor's own 4:3 ratio, so one control cannot leave the two
        // inconsistent. EnsureRenderTexture reallocates only when the size changes.
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

      ModeToggle = _file.Bind( "Controls", "Mode Toggle",
          new KeyboardShortcut( KeyCode.N, KeyCode.LeftAlt ),
          new ConfigDescription(
              "Switches the thermal between Outline and Full, naming the new mode on the display. Keep a " +
              "modifier, for the same reason as Power Toggle." ) );

      ThermalMode = _file.Bind( "Controls", "Thermal Mode", CotiThermalMode.Outline, new ConfigDescription(
          "Outline draws only the edges of hot things; Full adds a dimmer fill inside them (see Image, " +
          "Full Mode Fill). Mode Toggle changes this, and the device powers up in it." ) );

      _appliers.Add( () => Current.Image.Mode = ThermalMode.Value );
    }

    private void BindPowerSequence( CotiConfig defaults )
    {
      const string section = "Power Sequence";
      var power = defaults.PowerSequence ?? new CotiPowerSequenceConfig();

      var enabled = _file.Bind( section, "Enabled", power.Enabled, new ConfigDescription(
          "Plays the device's own start-up and shut-down when Power Toggle is pressed: " +
          "Initializing..., then the thermal with its calibration click, and Power Off... on the " +
          "way down. Off switches the thermal on and off instantly." ) );

      var initializing = _file.Bind( section, "Initializing Seconds", power.InitializingSeconds,
          new ConfigDescription( "How long Initializing... shows before the sensor starts.",
              new AcceptableValueRange<float>( 0.2f, 5f ) ) );

      var warming = _file.Bind( section, "Warm-up Gap Seconds", power.WarmingSeconds,
          new ConfigDescription( "The lit, empty display between Initializing... and the thermal image.",
              new AcceptableValueRange<float>( 0f, 3f ) ) );

      var modeLabel = _file.Bind( section, "Mode Label Seconds", power.ModeSeconds,
          new ConfigDescription( "How long the mode's name shows after Initializing..., and when Mode Toggle " +
              "is pressed. 0 never shows it.",
              new AcceptableValueRange<float>( 0f, 3f ) ) );

      var powerOff = _file.Bind( section, "Power Off Seconds", power.PowerOffSeconds,
          new ConfigDescription( "How long Power Off... shows before the display goes dark.",
              new AcceptableValueRange<float>( 0.2f, 5f ) ) );

      var clickVolume = _file.Bind( section, "Click Volume", power.ClickVolume,
          new ConfigDescription( "Loudness of the calibration click, on top of the game's own " +
              "volume settings. 0 mutes it.",
              new AcceptableValueRange<float>( 0f, 1f ) ) );

      _appliers.Add( () =>
      {
        Current.PowerSequence.Enabled = enabled.Value;
        Current.PowerSequence.InitializingSeconds = initializing.Value;
        Current.PowerSequence.ModeSeconds = modeLabel.Value;
        Current.PowerSequence.WarmingSeconds = warming.Value;
        Current.PowerSequence.PowerOffSeconds = powerOff.Value;
        Current.PowerSequence.ClickVolume = clickVolume.Value;
      } );
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
          "Under a grayscale palette a correct thermal render has neutral channel means; a " +
          "colour cast means the target was lit rather than thermal.",
          new AcceptableValueRange<int>( 0, 30 ) ) );

      var lensProbe = _file.Bind( "Debug", "Lens Probe", 0, new ConfigDescription(
          "Takes the magnified lens redraw apart: 0 as shipped, 1 the heat texture added to the screen, 2 the lens " +
          "mesh drawn with the overlay's copy pass, 3 the redraw with culling inverted, 10 to 14 the redraw with " +
          "that lens shader pass (10 is pass 0), 20 to 24 the same with the heat texture flat grey.",
          new AcceptableValueRange<int>( 0, 24 ) ) );
#endif

      // An action rather than a setting, so the drawer replaces the usual editor with a button.
      // The bound value is never read: CotiMaskPanel owns whether it is open, because the window
      // has its own Close button and hotkey and has to be able to shut itself without this menu
      // being on screen. Not under Debug and not IsAdvanced: it is the only entry point to the
      // editor.
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
        Current.ThermalCamera.LensProbe = lensProbe.Value;
#endif
      } );
    }
  }
}
