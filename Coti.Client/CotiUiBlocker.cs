using UnityEngine;
using UnityEngine.EventSystems;

namespace Coti.Client
{
  /// <summary>
  /// Stops a click on a COTI window also landing on the inventory behind it.
  ///
  /// IMGUI and the game's uGUI are separate input systems: GUI.Window consumes an event for IMGUI
  /// while uGUI's EventSystem raycasts the same click independently. There is no IMGUI switch for
  /// this, so the EventSystem is suspended while the cursor is inside a COTI window.
  ///
  /// Leaving it suspended kills the mouse for the whole interface. The decision is recomputed
  /// every frame, input is asserted ON unconditionally whenever no panel is open, and this runs
  /// from Update so an exception elsewhere cannot skip it.
  ///
  /// Never re-enable an EventSystem this class did not disable. The game disables its own
  /// during loading transitions, and switching that back on breaks them.
  /// </summary>
  public static class CotiUiBlocker
  {
    private static bool _loggedMissing;

    /// <summary>
    /// The EventSystem this class disabled.
    ///
    /// EventSystem.current returns only ENABLED systems, so disabling it made current null and
    /// a restore path that looked the target up through current could never find it. Restore
    /// through this reference, never through current.
    /// </summary>
    private static EventSystem _suspended;

    /// <summary>
    /// Called every frame from Plugin.Update, before anything that can throw.
    /// </summary>
    public static void Tick()
    {
      var anyOpen = CotiPoseTuner.IsOpen || CotiMaskPanel.IsOpen;

      if( !anyOpen )
      {
        // Unconditional, not "if we think we disabled it". This is the line that guarantees the
        // interface comes back no matter what happened in between.
        Release();
        return;
      }

      SetBlocking( CursorOverAnyPanel() );
    }

    /// <summary>
    /// Restores input. Safe to call at any time, including when nothing was ever blocked.
    /// </summary>
    public static void Release()
    {
      SetBlocking( false );
    }

    private static bool CursorOverAnyPanel()
    {
      // Input.mousePosition has its origin bottom-left; GUI rects are top-left.
      var cursor = new Vector2( Input.mousePosition.x, Screen.height - Input.mousePosition.y );

      if( CotiPoseTuner.IsOpen && CotiTunerPanel.WindowRect.Contains( cursor ) )
        return true;

      return CotiMaskPanel.IsOpen && CotiMaskPanel.WindowRect.Contains( cursor );
    }

    private static void SetBlocking( bool blocking )
    {
      if( blocking )
      {
        Suspend();
        return;
      }

      Restore();
    }

    private static void Suspend()
    {
      if( _suspended != null )
        return;

      // The one place current is consulted: nothing is suspended yet, so it is still live.
      var system = EventSystem.current;
      if( system == null )
      {
        if( !_loggedMissing )
        {
          _loggedMissing = true;
          Plugin.Log.LogInfo( "[COTI] No EventSystem present - window click-through is not blocked." );
        }

        return;
      }

      _suspended = system;
      _suspended.enabled = false;

      Plugin.Log.LogInfo( "[COTI] Cursor over a COTI window - game UI input suspended." );
    }

    private static void Restore()
    {
      if( _suspended == null )
        return;

      // Through the held reference. Cleared first, so a destroyed EventSystem is not retried forever.
      var system = _suspended;
      _suspended = null;

      if( system != null )
        system.enabled = true;

      Plugin.Log.LogInfo( "[COTI] Game UI input restored." );
    }
  }
}
