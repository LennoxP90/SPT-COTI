namespace Coti.Shared
{
  /// <summary>
  /// Paces a thermal camera to the sensor's refresh rate: due on about <c>hz</c> frames a second, whatever the game's
  /// frame rate, and on every frame when the game runs slower than the sensor. Frames between are skipped and the last
  /// picture stands, as on the real device, which also saves the scene pass.
  ///
  /// A quarter period of slack keeps a frame that lands a hair early from being skipped: at exactly the sensor's rate,
  /// jitter alone would otherwise halve it.
  /// </summary>
  public sealed class CotiSensorPacer
  {
    private const double Slack = 0.25;

    private double _next = double.NaN;

    /// <summary>Whether to render this frame. <paramref name="hz"/> 0 or less renders every frame.</summary>
    public bool Due( double now, int hz )
    {
      if( hz <= 0 )
      {
        _next = double.NaN;
        return true;
      }

      var period = 1.0 / hz;
      if( !double.IsNaN( _next ) && now < _next - period * Slack )
        return false;

      // On schedule, step one period; after a pause, restart from now rather than catching up in a burst.
      _next = double.IsNaN( _next ) || now - _next >= period ? now + period : _next + period;
      return true;
    }

    /// <summary>Renders on the next frame, for when the camera wakes.</summary>
    public void Reset()
    {
      _next = double.NaN;
    }
  }
}
