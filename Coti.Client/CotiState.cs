using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  public enum CotiShowing
  {
    Nothing,
    Thermal,
    Message,
  }

  /// <summary>
  /// Resolves whether the COTI should render this frame and with what. Holds no rendering code -
  /// the patches in Coti.Client.Patches read these fields, they never compute activation themselves.
  /// </summary>
  public static class CotiState
  {
    public static bool Active;
    public static Texture2D Mask;
    public static CotiNvgHostConfig Host;

    /// <summary>
    /// What the circle shows this frame. Message covers the boot and shutdown text and the lit,
    /// blank display between them; Active stays "the thermal renders".
    /// </summary>
    public static CotiShowing Showing;

    public static CotiDisplayMessage Message;

    /// <summary>
    /// A COTI is in the equipped night vision's slot, whatever its power or the tube's state.
    /// </summary>
    public static bool CotiAttached;

    /// <summary>
    /// The template id <see cref="Host"/> was resolved from. The mask editor needs it to find
    /// which device file to publish, since CotiNvgHostConfig does not carry its own id.
    /// </summary>
    public static string HostTemplateId;

    /// <summary>
    /// Whatever night vision is equipped, regardless of whether it is switched on, powered, or
    /// carrying a COTI. Distinct from <see cref="HostTemplateId"/>, which is only set on the path
    /// where the overlay actually renders.
    ///
    /// The mask editor keys on this one so it can open in the stash, where goggles are never on.
    /// </summary>
    public static string EquippedHostTemplateId;

    // Once per raid rather than per session, so a settings change between raids produces a fresh line
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
      HostTemplateId = null;
      Showing = CotiShowing.Nothing;
      Message = CotiDisplayMessage.None;
      EquippedHostTemplateId = hostTemplateId;
      CotiAttached = cotiAttached;

      var enabled = Plugin.Config?.Enabled ?? true;
      var frame = CotiPowerToggle.Frame;

      var thermal = CotiActivation.ShouldBeActive(
          Plugin.IsHeadless, cotiAttached, hostNvgOn, frame.ThermalOn && enabled );

      var display = !thermal && CotiActivation.ShouldShowDisplay(
          Plugin.IsHeadless, cotiAttached, hostNvgOn, frame.Message != CotiDisplayMessage.None && enabled );

      if( !thermal && !display )
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

      // Logged as soon as the host is resolved: mask generation or the composite pass can still
      // decline to run (a non-positive maskRadius, an unverified shader), and this line is the
      // only record of which values the raid resolved to.
      LogHostResolvedOnce( hostTemplateId, maskLabel, host );

      // The mask is built in the compositing camera's pixel space, not the screen's.
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
      HostTemplateId = hostTemplateId;
      Mask = mask;
      Active = thermal;
      Showing = thermal ? CotiShowing.Thermal : CotiShowing.Message;
      Message = frame.Message;
    }

    private static void LogHostResolvedOnce( string hostTemplateId, string maskLabel, CotiNvgHostConfig host )
    {
      if( _loggedHostForRaid )
        return;
      _loggedHostForRaid = true;

      // Image parameters are global (see CotiImageConfig); only the mask geometry is per-host.
      var image = Plugin.Config?.Image ?? new CotiImageConfig();
      var paletteText = string.IsNullOrEmpty( image.Palette ) ? "(unchanged)" : image.Palette;

      Plugin.Log.LogInfo(
          $"[COTI] host {hostTemplateId} ({maskLabel}) " +
          $"minTemp={image.MinimumTemperatureValue:F2} colorCoef={image.MainTexColorCoef:F2} " +
          $"depthFade={image.DepthFade:F2} unsharp={image.UnsharpRadiusBlur:F1}/{image.UnsharpBias:F1} " +
          $"mask=({host.MaskCenterX:F3},{host.MaskCenterY:F3}) r={host.MaskRadius:F3} f={host.MaskFeather:F3} " +
          $"pix={image.IsPixelated} noise={image.IsNoisy} motion={image.IsMotionBlurred} " +
          $"palette={paletteText} rampShift={image.RampShift:F2}" );
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
