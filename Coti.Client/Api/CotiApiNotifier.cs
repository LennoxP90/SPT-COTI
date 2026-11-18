namespace Coti.Client.Api
{
  /// <summary>
  /// Decides when <see cref="CotiApi.Changed"/> fires: when anything the API reports differs from the last frame. Pure,
  /// so Coti.Tests can pin it.
  /// </summary>
  internal sealed class CotiApiNotifier
  {
    private bool _seen;
    private bool _active;
    private bool _attached;
    private string? _nightVision;

    internal bool Observe( bool active, bool attached, string? nightVision )
    {
      if( _seen && active == _active && attached == _attached && nightVision == _nightVision )
        return false;

      _seen = true;
      _active = active;
      _attached = attached;
      _nightVision = nightVision;
      return true;
    }
  }
}
