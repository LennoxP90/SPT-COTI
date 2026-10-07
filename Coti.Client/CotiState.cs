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
    /// <summary>The most tubes a layout has, and so the most circles drawn at once.</summary>
    public const int MaxTubes = 4;

    public static bool Active;
    public static CotiNvgHostConfig Host;

    /// <summary>
    /// What the circle shows this frame. Message covers the boot and shutdown text and the lit,
    /// blank display between them; Active stays "the thermal renders".
    /// </summary>
    public static CotiShowing Showing;

    public static CotiDisplayMessage Message;

    /// <summary>
    /// A COTI is in one of the equipped night vision's COTI slots, whatever its power, its pod or the tube's state.
    /// </summary>
    public static bool CotiAttached;

    /// <summary>The COTI slots holding a COTI, as <see cref="CotiTubeSet"/> bits.</summary>
    public static int FilledSlots;

    /// <summary>The filled slots whose pod is down, or that ride no pod.</summary>
    public static int LitSlots;

    /// <summary>
    /// The open tubes, left to right: lit, on goggles that are on, while the display shows the thermal or a message.
    /// The overlay draws nothing outside their circles, and the text draws in each. OpenLabels holds each one's
    /// CotiTubes label, null for a device file without tubes, and OpenTexts where its messages sit.
    /// </summary>
    public static readonly CotiCircle[] OpenCircles = new CotiCircle[MaxTubes];

    public static readonly string[] OpenLabels = new string[MaxTubes];
    public static readonly CotiTextPlacement[] OpenTexts = new CotiTextPlacement[MaxTubes];
    public static int OpenCount;

    /// <summary>The open tubes' slots, for anything keyed on which circles show.</summary>
    public static int OpenSlots;

    /// <summary>
    /// Every filled tube's circle, lit or not, while anything shows. The thermal renders the box around these, so a pod
    /// flip never resizes its target.
    /// </summary>
    public static readonly CotiCircle[] FilledCircles = new CotiCircle[MaxTubes];

    public static int FilledCount;

    /// <summary>The main camera's aspect, which this frame's circles were placed at.</summary>
    public static float Aspect = 16f / 9f;

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
    /// Recomputes state from the equipped device. hostTemplateId is null when no night vision is equipped.
    /// filledSlots are the COTI slots holding a COTI and litSlots those of them whose pod is down or absent, both as
    /// CotiTubeSet bits.
    /// </summary>
    public static void Update( string hostTemplateId, int filledSlots, int litSlots, bool hostNvgOn )
    {
      Host = null;
      HostTemplateId = null;
      Showing = CotiShowing.Nothing;
      Message = CotiDisplayMessage.None;
      OpenCount = 0;
      OpenSlots = 0;
      FilledCount = 0;
      EquippedHostTemplateId = hostTemplateId;
      FilledSlots = filledSlots;
      LitSlots = litSlots;
      CotiAttached = filledSlots != 0;

      var enabled = Plugin.Config?.Enabled ?? true;
      var frame = CotiPowerToggle.Frame;

      // A tube on a pod flipped up is dark as if the goggles were off, so the picture needs a lit one. The click reads
      // CotiAttached instead: the device clicks whatever its pods do.
      var anyLit = litSlots != 0;

      var thermal = CotiActivation.ShouldBeActive(
          Plugin.IsHeadless, anyLit, hostNvgOn, frame.ThermalOn && enabled );

      var display = !thermal && CotiActivation.ShouldShowDisplay(
          Plugin.IsHeadless, anyLit, hostNvgOn, frame.Message != CotiDisplayMessage.None && enabled );

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

      // Logged as soon as the host is resolved: the composite pass can still decline to run (no circle to draw, an
      // unverified shader), and this line is the only record of which values the raid resolved to.
      LogHostResolvedOnce( hostTemplateId, maskLabel, host );

      // The circles are placed for the eye camera's aspect, the one the overlay draws them at.
      var camera = CotiFrame.Main;
      if( camera == null )
      {
        Active = false;
        return;
      }

      Aspect = camera.aspect;
      PlaceCircles( host, filledSlots, litSlots );

      // Lit slots with no circle (a table without the layout the item's slots came from) leave nothing to draw.
      if( OpenCount == 0 )
      {
        Active = false;
        return;
      }

      Host = host;
      HostTemplateId = hostTemplateId;
      Active = thermal;
      Showing = thermal ? CotiShowing.Thermal : CotiShowing.Message;
      Message = frame.Message;
    }

    /// <summary>
    /// The circles of the filled and the lit tubes. A device file with a layout places each tube's circle from it, at
    /// this frame's aspect; one without passes its stored mask straight through as its one circle, on mod_coti.
    /// </summary>
    private static void PlaceCircles( CotiNvgHostConfig host, int filledSlots, int litSlots )
    {
      var layout = host.Layout;
      if( layout == null )
      {
        // CotiCircles.FromMask without the per-frame CotiMaskBlock.
        Place( new CotiCircle( host.MaskCenterX, host.MaskCenterY, host.MaskRadius, host.MaskFeather ),
            null, host.TextPlacement( null ), CotiTubeSet.Bit( CotiIds.ModSlotName ), filledSlots, litSlots );
        return;
      }

      for( var i = 0; i < layout.Tubes.Count; i++ )
      {
        var tube = layout.Tubes[i];
        Place( CotiCircles.FromLayout( tube, Aspect ), tube.Label, host.TextPlacement( tube.Label ),
            CotiTubeSet.Bit( CotiTubes.SlotName( tube.Label, layout ) ), filledSlots, litSlots );
      }
    }

    private static void Place( CotiCircle circle, string label, CotiTextPlacement text, int bit, int filledSlots, int litSlots )
    {
      if( ( filledSlots & bit ) == 0 )
        return;

      FilledCircles[FilledCount++] = circle;
      if( ( litSlots & bit ) == 0 )
        return;

      OpenCircles[OpenCount] = circle;
      OpenTexts[OpenCount] = text;
      OpenLabels[OpenCount++] = label;
      OpenSlots |= bit;
    }

    private static void LogHostResolvedOnce( string hostTemplateId, string maskLabel, CotiNvgHostConfig host )
    {
      if( _loggedHostForRaid )
        return;
      _loggedHostForRaid = true;

      // Image parameters are global (see CotiImageConfig); only the circles are per-host.
      var image = Plugin.Config?.Image ?? new CotiImageConfig();
      var paletteText = string.IsNullOrEmpty( image.Palette ) ? "(unchanged)" : image.Palette;

      Plugin.Log.LogInfo(
          $"[COTI] host {hostTemplateId} ({maskLabel}) " +
          $"minTemp={image.MinimumTemperatureValue:F2} colorCoef={image.MainTexColorCoef:F2} " +
          $"depthFade={image.DepthFade:F2} unsharp={image.UnsharpRadiusBlur:F1}/{image.UnsharpBias:F1} " +
          $"layout={host.Layout?.Name ?? "none"} " +
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
