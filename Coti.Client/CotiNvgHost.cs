using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Coti.Client
{
  /// <summary>
  /// Finds the night vision device the player is wearing.
  /// </summary>
  public static class CotiNvgHost
  {
    /// <summary>
    /// The equipped night vision component, or null. Null for a real thermal device such as the
    /// T-7, which has no NightVisionComponent.
    ///
    /// CurrentValue, not Component: the two return the same thing, but Component re-walks the whole
    /// headwear tree on every call, and this is read once a frame.
    /// </summary>
    public static NightVisionComponent Component
    {
      get
      {
        var player = LocalPlayer;
        if( player == null )
          return null;

        var observer = player.NightVisionObserver;

        // Same field, obfuscated to a different name on 4.0. A direct read on both rather than one
        // routed through EftCompat, since reflection here would cost more than it saves.
#if SPT40
        return observer?.Gparam_0;
#else
        return observer?.CurrentValue;
#endif
      }
    }

    public static Player LocalPlayer
    {
      get
      {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return world == null ? null : world.MainPlayer;
      }
    }
  }
}
