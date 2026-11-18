using System;
using Coti.Shared;

namespace Coti.Client.Api
{
  /// <summary>
  /// The surface an addon plugin calls: a BepInEx DLL shipped beside an addon's device files, for a host mod that needs
  /// code as well as data. Reference the installed Coti.Client.dll with Private false and declare
  /// [BepInDependency("com.lennoxp90.coti")]. See ADDONS.md, "Addons that ship code".
  ///
  /// Additions only once released: nothing here is removed, renamed or changes meaning, and
  /// <see cref="ApiVersion"/> goes up by one with each release that adds to it. Members are read on the main thread.
  /// </summary>
  public static class CotiApi
  {
    /// <summary>
    /// Read at run time, not compiled into the addon, as a const would be: an addon compares it with the version it was
    /// built against to know which members exist.
    /// </summary>
    public static int ApiVersion => 1;

    /// <summary>COTI's own version, for log lines.</summary>
    public static string Version => CotiVersion.Current;

    /// <summary>COTI draws its thermal image this frame. False on a Fika headless, which never renders.</summary>
    public static bool Active => CotiState.Active;

    /// <summary>A COTI is fitted to the equipped night vision, whatever its power.</summary>
    public static bool Attached => CotiState.CotiAttached;

    /// <summary>The template id of the equipped night vision, or null with none.</summary>
    public static string? EquippedNightVision => CotiState.EquippedHostTemplateId;

    /// <summary>
    /// Whether a night vision template id is a COTI host in the device table now loaded. The server's table arrives after
    /// plugins load and changes when a device is published, so ask when the answer is needed rather than keeping it.
    /// </summary>
    public static bool IsHost( string templateId )
    {
      var hosts = Plugin.Config?.NvgHosts;
      return templateId != null && hosts != null && hosts.ContainsKey( templateId );
    }

    /// <summary>
    /// Raised on the main thread, at the end of the frame, when <see cref="Active"/>, <see cref="Attached"/> or
    /// <see cref="EquippedNightVision"/> changed, and once on COTI's first frame. A handler that throws is logged once and does not stop the others.
    /// </summary>
    public static event Action? Changed;

    private static readonly CotiApiNotifier Notifier = new CotiApiNotifier();
    private static bool _loggedHandlerError;

    /// <summary>Once per frame from Plugin.Update, after the state is resolved.</summary>
    internal static void Tick()
    {
      if( !Notifier.Observe( Active, Attached, EquippedNightVision ) )
        return;

      var handlers = Changed;
      if( handlers == null )
        return;

      foreach( Action handler in handlers.GetInvocationList() )
      {
        try
        {
          handler();
        }
        catch( Exception ex )
        {
          if( !_loggedHandlerError )
          {
            _loggedHandlerError = true;
            Plugin.Log.LogError( $"[COTI] An addon's CotiApi.Changed handler failed: {ex}" );
          }
        }
      }
    }
  }
}
