using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Per-frame values read once at the top of Plugin.Update and shared by everything that runs later in the frame.
  /// Camera.main searches the cameras tagged MainCamera on every read.
  /// </summary>
  internal static class CotiFrame
  {
    /// <summary>The game's main camera this frame, or null outside a scene that has one.</summary>
    internal static Camera Main { get; private set; }

    internal static void Begin()
    {
      Main = Camera.main;
    }
  }
}
