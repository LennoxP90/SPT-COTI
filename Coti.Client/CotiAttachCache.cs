using System;

namespace Coti.Client
{
  /// <summary>
  /// A probe result held against the host it was taken from, re-run only when the host changes or
  /// something invalidates it. The result is a set of slots as bits (CotiTubeSet).
  ///
  /// Keyed on reference identity rather than equality: two items that compare equal are still two
  /// items.
  /// Pure and free of EFT types so Coti.Tests can drive it with a counting probe.
  /// </summary>
  internal sealed class CotiAttachCache<T> where T : class
  {
    private readonly Func<T, int> _probe;

    private T _host;
    private int _value;
    private bool _dirty = true;

    internal CotiAttachCache( Func<T, int> probe )
    {
      _probe = probe;
    }

    internal void Invalidate()
    {
      _dirty = true;
    }

    internal int Read( T host )
    {
      if( _dirty || !ReferenceEquals( host, _host ) )
      {
        _host = host;
        _value = _probe( host );
        _dirty = false;
      }

      return _value;
    }
  }
}
