using System;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Writes a line whenever what the mod is RENDERING changes, so an external performance capture
  /// can be aligned by timestamp instead of by hand-written notes.
  ///
  /// A capture without it produced two clean frame-time regimes and no record of which condition
  /// either was. Gated on VerboseLogging, since magnifying toggles on every weapon raise.
  /// </summary>
  internal static class CotiRenderStateLog
  {
    private static bool _known;
    private static bool _active;
    private static bool _magnifying;
    private static int _fovTenths;
    private static int _rows;

    internal static void Tick()
    {
      if( !( Plugin.Config?.VerboseLogging ?? false ) )
      {
        // Forgotten rather than kept: with logging off the state can change unobserved, and resuming
        // from a stale one would skip the line that says where the capture starts.
        _known = false;
        return;
      }

      var active = CotiState.Active;
      var magnifying = CotiOpticThermalCamera.Magnifying;

      // Bucketed so a scope being swept does not emit a line per frame, while still separating the
      // stops that matter.
      var fovTenths = Mathf.RoundToInt( CotiOpticThermalCamera.Optic.FieldOfView * 10f );
      var rows = Plugin.Config?.ThermalCamera?.Height ?? 0;

      if( _known && active == _active && magnifying == _magnifying && fovTenths == _fovTenths
          && rows == _rows )
        return;

      _known = true;
      _active = active;
      _magnifying = magnifying;
      _fovTenths = fovTenths;
      _rows = rows;

      // Our own wall clock: BepInEx's disk log carries no timestamps, so without this a capture can
      // only be aligned by matching plateaus to the order of these lines.
      Plugin.Log.LogInfo(
          $"[COTI BENCH] {DateTime.Now:HH:mm:ss.fff} " +
          $"overlay={( active ? "on" : "off" )} " +
          $"magnifying={( magnifying ? "on" : "off" )} " +
          $"opticFov={fovTenths / 10f:F1} " +
          $"rows={rows} " +
          $"magnifyConfig={( Plugin.Config?.MagnifyWithOptic == true ? "on" : "off" )}" );
    }
  }
}
