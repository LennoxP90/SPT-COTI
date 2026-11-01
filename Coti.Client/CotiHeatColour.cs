using BepInEx.Configuration;
using UnityEngine;

namespace Coti.Client
{
  public enum CotiHeatColourMode
  {
    Default,
    Custom,
    RainbowWave,
    RainbowStatic,
  }

  /// <summary>
  /// The player's heat colour per device, from the F12 Colour section. Default leaves each device's own tint: the
  /// goggle follows the tube's phosphor, the riser and scope ring draw white-hot.
  /// </summary>
  internal static class CotiHeatColour
  {
    private sealed class Device
    {
      public ConfigEntry<CotiHeatColourMode> Mode;
      public ConfigEntry<Color> Colour;
      public ConfigEntry<float> RainbowSpeed;
    }

    private static Device _goggle, _riser, _ring;

    internal static void Bind( ConfigFile file )
    {
      _goggle = BindDevice( file, "COTI", "the ECOTI on your goggles. Default follows the night vision tube's colour." );
      _riser = BindDevice( file, "Thermal Riser", "the thermal riser. Default is white-hot." );
      _ring = BindDevice( file, "Thermal Scope Ring", "the thermal scope rings. Default is white-hot." );
    }

    private static Device BindDevice( ConfigFile file, string name, string what )
    {
      const string section = "Colour";
      return new Device
      {
        Mode = file.Bind( section, name + " Colour", CotiHeatColourMode.Default, new ConfigDescription(
            "The heat colour of " + what + " Custom uses the colour below. RainbowWave sweeps a rainbow across the heat, " +
            "RainbowStatic holds one still." ) ),
        Colour = file.Bind( section, name + " Custom Colour", Color.white, new ConfigDescription(
            "The heat colour while " + name + " Colour is Custom. It replaces the night vision's own " +
            "colour where heat shows. Alpha is ignored: Heat Brightness and Overlay Intensity set how strongly it draws." ) ),
        RainbowSpeed = file.Bind( section, name + " Rainbow Speed", 0.25f, new ConfigDescription(
            "How fast the rainbow moves across the heat while " + name + " Colour is RainbowWave, in colour cycles " +
            "per second.",
            new AcceptableValueRange<float>( 0f, 2f ) ) ),
      };
    }

    /// <summary>The shader's _HeatOverride: a is the mode (0 Default, 1 Custom with rgb the colour, 2 Rainbow drawn by the shader with r its speed).</summary>
    internal static readonly int OverrideId = Shader.PropertyToID( "_HeatOverride" );

    internal static Vector4 Goggle() => Resolve( _goggle );

    /// <summary>The riser or a scope ring, whichever is on the weapon in hand.</summary>
    internal static Vector4 Riser() => Resolve( ThermalRiserState.IsScopeRing ? _ring : _riser );

    private static Vector4 Resolve( Device device )
    {
      if( device == null || device.Mode.Value == CotiHeatColourMode.Default )
        return Vector4.zero;
      if( device.Mode.Value == CotiHeatColourMode.RainbowWave )
        return new Vector4( device.RainbowSpeed.Value, 0f, 0f, 2f );
      if( device.Mode.Value == CotiHeatColourMode.RainbowStatic )
        return new Vector4( 0f, 0f, 0f, 2f );

      var colour = device.Colour.Value;
      return new Vector4( colour.r, colour.g, colour.b, 1f );
    }
  }
}
