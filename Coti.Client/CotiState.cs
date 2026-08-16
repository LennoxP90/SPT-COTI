using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Resolves whether the COTI should render this frame and with what. Holds no rendering code -
  /// the patches in Coti.Client.Patches read these fields, they never compute activation themselves.
  /// </summary>
  public static class CotiState
  {
    public static bool Active;
    public static Texture2D Mask;
    public static CotiNvgHostConfig Host;

    // Once per RAID, not per session, so a settings change between raids produces a fresh line
    // and an in-raid screenshot can be matched back to a configuration from the log alone.
    private static bool _loggedHostForRaid;
    private static bool _loggedMissingHostForRaid;

    public static void ResetPerRaidLogging()
    {
      _loggedHostForRaid = false;
      _loggedMissingHostForRaid = false;
    }

    /// <summary>
    /// Recomputes state from the equipped device. hostTemplateId is null when no night vision
    /// is equipped; cotiAttached is true when a COTI occupies its mod_coti slot.
    /// </summary>
    public static void Update( string hostTemplateId, bool cotiAttached, bool hostNvgOn )
    {
      Mask = null;
      Host = null;

      // Folded into "powered on" rather than given its own branch, so it travels the same
      // already-proven path.
      var poweredOn = CotiPowerToggle.PoweredOn && ( Plugin.Config?.Enabled ?? true );

      if( !CotiActivation.ShouldBeActive(
              Plugin.IsHeadless, cotiAttached, hostNvgOn, poweredOn ) )
      {
        Active = false;
        return;
      }

      var hosts = Plugin.Config?.NvgHosts;
      if( hosts == null )
      {
        Active = false;
        return;
      }

      CotiNvgHostConfig host;
      if( !hosts.TryGetValue( hostTemplateId ?? string.Empty, out host ) || host == null )
      {
        LogMissingHostOnce( hostTemplateId );
        Active = false;
        return;
      }

      var maskLabel = CotiMaskResolver.ResolveMaskName( Plugin.Config, hostTemplateId );

      // Logged as soon as the host is resolved, not after mask generation or the composite
      // pass - either of those can still decline to run for its own reasons (a non-positive
      // maskRadius, an unverified shader), and this line has to appear regardless, since it is
      // the only record of which values this raid actually resolved to.
      LogHostResolvedOnce( hostTemplateId, maskLabel, host );

      // The mask must be built in the COMPOSITING CAMERA's pixel space, not the screen's.
      var camera = Camera.main;
      if( camera == null )
      {
        Active = false;
        return;
      }

      var mask = MaskGenerator.GetOrCreate(
          hostTemplateId, host, maskLabel, camera.pixelWidth, camera.pixelHeight );
      if( mask == null )
      {
        // MaskGenerator already logged the specific reason (non-positive maskRadius) once.
        Active = false;
        return;
      }

      Host = host;
      Mask = mask;
      Active = true;
    }

    private static void LogHostResolvedOnce( string hostTemplateId, string maskLabel, CotiNvgHostConfig host )
    {
      if( _loggedHostForRaid )
        return;
      _loggedHostForRaid = true;

      var paletteText = string.IsNullOrEmpty( host.Palette ) ? "(unchanged)" : host.Palette;

      Plugin.Log.LogInfo(
          $"[COTI] host {hostTemplateId} ({maskLabel}) mode={host.CompositeMode} " +
          $"minTemp={host.MinimumTemperatureValue:F2} colorCoef={host.MainTexColorCoef:F2} " +
          $"depthFade={host.DepthFade:F2} unsharp={host.UnsharpRadiusBlur:F1}/{host.UnsharpBias:F1} " +
          $"contrast={host.OverlayContrast:F2} exposure={host.OverlayExposure:F2} " +
          $"mask=({host.MaskCenterX:F3},{host.MaskCenterY:F3}) r={host.MaskRadius:F3} f={host.MaskFeather:F3} " +
          $"pix={host.IsPixelated} noise={host.IsNoisy} motion={host.IsMotionBlurred} " +
          $"palette={paletteText} rampShift={host.RampShift:F2}" );
    }

    private static void LogMissingHostOnce( string hostTemplateId )
    {
      if( _loggedMissingHostForRaid )
        return;
      _loggedMissingHostForRaid = true;

      Plugin.Log.LogWarning(
          $"[COTI] No host config entry for template id {hostTemplateId ?? "(none)"} - COTI inactive" );
    }
  }
}
