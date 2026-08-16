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
    /// </summary>
    public static NightVisionComponent Component
    {
      get
      {
        var player = LocalPlayer;
        return player?.NightVisionObserver?.Component;
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
