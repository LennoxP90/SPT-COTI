using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Holds the main camera's UltimateBloom off while the magnified composite draws.
  ///
  /// The magnified heat goes into the scope camera's HDR texture, which the game then draws on the lens inside the
  /// main camera's scene, so the main camera's bloom treats it as scene light: heat over the night-vision image
  /// passes UltimateBloom's threshold (2.1 in raid) and every hot shape grew a halo across the lens. Measured by
  /// switching each bloom off in turn; BloomAndFlares (threshold 0.46) made no visible difference.
  ///
  /// Re-applied every frame, since the game may switch it back on, and given back exactly as found.
  /// </summary>
  internal static class CotiScopeBloom
  {
    private static UltimateBloom _held;

    internal static void Hold()
    {
      var camera = Camera.main;
      var bloom = camera != null ? camera.GetComponent<UltimateBloom>() : null;

      if( _held != null && _held != bloom )
        Release();

      if( bloom == null || ( _held == null && !bloom.enabled ) )
        return;

      _held = bloom;
      if( bloom.enabled )
        bloom.enabled = false;
    }

    internal static void Release()
    {
      if( _held != null )
        _held.enabled = true;

      _held = null;
    }
  }
}
