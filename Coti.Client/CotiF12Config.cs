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
          "0 is solid hot shapes, 1 is edge-only contours",
          new AcceptableValueRange<float>( 0f, 1f ) ) );

      _appliers.Add( () =>
      {
        Current.Enabled = enabled.Value;

        foreach( var host in Current.NvgHosts.Values )
        {
          host.HeatThreshold = threshold.Value;
          host.OverlayIntensity = intensity.Value;
          host.OutlineMix = outline.Value;
        }
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
              ",/. to roll, [/] to pitch, ;/' to yaw." ) );

      var stepMm = _file.Bind( "Debug", "Tuner Step (mm)", defaults.TunerStepMm,
          "Distance per keypress. Hold Shift while tuning for a quarter of this." );

      var stepDegrees = _file.Bind( "Debug", "Tuner Step (degrees)", defaults.TunerStepDegrees,
          "Rotation per keypress. Hold Shift while tuning for a fifth of this." );
#endif

      _appliers.Add( () =>
      {
        Current.VerboseLogging = verbose.Value;
#if COTI_DEV
        Current.EnablePoseModifier = poseModifier.Value;
        Current.TunerModifier = modifier.Value;
        Current.TunerStepMm = stepMm.Value;
        Current.TunerStepDegrees = stepDegrees.Value;
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
