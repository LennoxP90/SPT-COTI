using System;
using BepInEx.Configuration;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Editor for the circle the thermal overlay renders inside.
  ///
  /// Separate from the pose editor because a mount pose is judged against the model in that
  /// panel's viewport, while the circle only exists in first person with the goggles down. It
  /// opens from F12 - where the cursor is already free - and outlives that menu closing.
  ///
  /// Every action has a hotkey, not just a button: in a raid the cursor is locked, so nothing on
  /// screen is clickable. Keys default to the numeric keypad, which EFT does not bind and the
  /// pose editor does not use.
  /// </summary>
  public static class CotiMaskPanel
  {
    // Unique among the windows this plugin draws; CotiTunerPanel owns 0x434f5449.
    private const int WindowId = 0x434f5450;

    // Sized to hold everything: the pad's three rows, the size and edge pairs, four value rows,
    // the key legend and the button row. At 300x340 the legend AND both buttons fell off the
    // bottom, which made Publish unreachable by mouse and hid the only place the keys are written
    // down. IMGUI clips silently, so nothing said so.
    private const float MinWidth = 360f;
    private const float MinHeight = 470f;

    /// <summary>
    /// Long enough that a burst of nudges is one save, short enough to feel immediate. A held key
    /// ramps to roughly ten steps a second, so this comfortably outlasts the gap between them.
    /// </summary>
    private const float AutoSaveDelaySeconds = 0.6f;

    // Enough for the two explanatory lines plus the button row, and no more.
    private const float UnboundHeight = 130f;

    private static ConfigEntry<float> _windowX;
    private static ConfigEntry<float> _windowY;

    private static ConfigEntry<KeyCode> _keyUp;
    private static ConfigEntry<KeyCode> _keyDown;
    private static ConfigEntry<KeyCode> _keyLeft;
    private static ConfigEntry<KeyCode> _keyRight;
    private static ConfigEntry<KeyCode> _keyGrow;
    private static ConfigEntry<KeyCode> _keyShrink;
    private static ConfigEntry<KeyCode> _keySofter;
    private static ConfigEntry<KeyCode> _keyHarder;
    private static ConfigEntry<KeyCode> _keyReset;
    private static ConfigEntry<KeyCode> _keyPublish;
    private static ConfigEntry<KeyCode> _keyClose;
    private static ConfigEntry<KeyCode> _keyFine;

    private static Rect _rect;

    /// <summary>Where the window is, for CotiUiBlocker's cursor hit-test.</summary>
    public static Rect WindowRect => _rect;

    /// <summary>
    /// The host the panel is editing. Sticky: it keeps the last host it bound to when the goggles
    /// go up, rather than dropping to null and discarding a half-finished adjustment.
    /// </summary>
    private static string _boundHostId;

    private static CotiMaskBlock _saved;

    /// <summary>
    /// Seconds since the last change, or negative when there is nothing pending. Drives the
    /// debounced auto-save: see <see cref="TickAutoSave"/>.
    /// </summary>
    private static float _pendingSeconds = -1f;
    private static CotiMaskBlock _working;
    private static string _deviceLabel;
    private static string _note;

    public static bool IsOpen { get; private set; }

    public static void Install( ConfigFile file )
    {
      const string window = "Mask Editor Window";
      const string keys = "Mask Editor Keys";

      _windowX = file.Bind( window, "X", 60f,
          "Mask editor window position. Set by dragging the window; not meant to be hand-edited." );
      _windowY = file.Bind( window, "Y", 60f,
          "Mask editor window position. Set by dragging the window; not meant to be hand-edited." );

      _keyUp = Key( file, keys, "Centre Up", KeyCode.Keypad8, "Moves the circle up the screen." );
      _keyDown = Key( file, keys, "Centre Down", KeyCode.Keypad2, "Moves the circle down the screen." );
      _keyLeft = Key( file, keys, "Centre Left", KeyCode.Keypad4, "Moves the circle left." );
      _keyRight = Key( file, keys, "Centre Right", KeyCode.Keypad6, "Moves the circle right." );
      _keyGrow = Key( file, keys, "Radius Grow", KeyCode.KeypadPlus, "Makes the circle bigger." );
      _keyShrink = Key( file, keys, "Radius Shrink", KeyCode.KeypadMinus, "Makes the circle smaller." );
      _keySofter = Key( file, keys, "Edge Softer", KeyCode.Keypad7, "Widens the fade at the circle's rim." );
      _keyHarder = Key( file, keys, "Edge Harder", KeyCode.Keypad9,
          "Narrows the fade. At zero the rim is a hard cut, which is a legitimate look rather than a fault." );
      _keyReset = Key( file, keys, "Reset", KeyCode.Keypad5,
          "Discards this session's changes and returns to the values the server holds." );
      _keyPublish = Key( file, keys, "Publish", KeyCode.KeypadEnter,
          "Writes the current circle to the device's file on the server." );
      _keyClose = Key( file, keys, "Close", KeyCode.Keypad0, "Closes this window." );

      // NOT Shift, and that is a Windows behaviour rather than a preference. With NumLock on,
      // holding Shift temporarily inverts the keypad, so Shift+Numpad8 reports as UpArrow and
      // Keypad8 never fires at all - a Shift-based modifier simply cannot work for these
      // bindings. Control does not do that to the keypad.
      _keyFine = Key( file, keys, "Fine Modifier", KeyCode.LeftControl,
          "Hold for a smaller step. Deliberately not Shift: with NumLock on, Windows makes "
          + "Shift+keypad report as the arrow keys instead, so a Shift-based modifier never fires "
          + "for these bindings." );

      _rect = new Rect( _windowX.Value, _windowY.Value, MinWidth, MinHeight );
    }

    private static ConfigEntry<KeyCode> Key( ConfigFile file, string section, string name, KeyCode fallback,
        string description )
    {
      return file.Bind( section, name, fallback, new ConfigDescription( description, null,
          new ConfigurationManagerAttributes { IsAdvanced = true } ) );
    }

    public static void Open()
    {
      IsOpen = true;
      _note = null;
    }

    public static void Close()
    {
      IsOpen = false;
    }

    /// <summary>
    /// Key handling, called from Update and NOT from OnGUI. Unity calls OnGUI several times a
    /// frame, so Input.GetKeyDown read from there fires more than once per press - the same shape
    /// of defect that made the pose editor's hold-to-repeat buttons step once per rendered frame
    /// with no initial delay.
    /// </summary>
    public static void Tick()
    {
      if( !IsOpen || _keyClose == null )
        return;

      // Before the binding guard, deliberately. In a raid the buttons are unclickable, so if
      // Close sat behind "something is bound" a window that never bound could not be dismissed
      // at all without reopening F12.
      if( Input.GetKeyDown( _keyClose.Value ) )
      {
        Close();
        return;
      }

      Rebind();

      if( _working == null )
        return;

      var fine = Input.GetKey( _keyFine.Value );

      // UP IS AN INCREASE. The obvious reasoning - "centerY is measured down the screen, so a
      // visual up must be a decrease" - was wrong, and moved the circle the wrong way for both
      // the keys and the pad. Whatever the mask generator and the compositor do between them,
      // the sign that matters is the one observed on screen.
      if( Input.GetKeyDown( _keyUp.Value ) ) Nudge( CotiMaskAxis.CenterY, 1, fine );
      if( Input.GetKeyDown( _keyDown.Value ) ) Nudge( CotiMaskAxis.CenterY, -1, fine );
      if( Input.GetKeyDown( _keyLeft.Value ) ) Nudge( CotiMaskAxis.CenterX, -1, fine );
      if( Input.GetKeyDown( _keyRight.Value ) ) Nudge( CotiMaskAxis.CenterX, 1, fine );
      if( Input.GetKeyDown( _keyGrow.Value ) ) Nudge( CotiMaskAxis.Radius, 1, fine );
      if( Input.GetKeyDown( _keyShrink.Value ) ) Nudge( CotiMaskAxis.Radius, -1, fine );
      if( Input.GetKeyDown( _keySofter.Value ) ) Nudge( CotiMaskAxis.Feather, 1, fine );
      if( Input.GetKeyDown( _keyHarder.Value ) ) Nudge( CotiMaskAxis.Feather, -1, fine );

      if( Input.GetKeyDown( _keyReset.Value ) ) Reset();
      if( Input.GetKeyDown( _keyPublish.Value ) ) Publish();

      TickAutoSave();
    }

    private static void Nudge( CotiMaskAxis axis, int direction, bool fine )
    {
      _working = CotiMaskNudge.Nudge( _working, axis, direction, fine );
      ApplyLive();

      // Arms the auto-save rather than waiting for a button. Restarted on every change, so a burst
      // of nudges saves once when it settles.
      _pendingSeconds = 0f;
    }

    /// <summary>
    /// Saves shortly after the last change, so nothing has to be clicked - in a raid the cursor is
    /// locked and Publish cannot be reached at all.
    ///
    /// Debounced because publishing is a blocking POST: one per keypress stutters while a key is
    /// held, one per burst does not. The delay restarts on every change.
    /// </summary>
    private static void TickAutoSave()
    {
      if( _pendingSeconds < 0f )
        return;

      _pendingSeconds += Time.unscaledDeltaTime;
      if( _pendingSeconds < AutoSaveDelaySeconds )
        return;

      _pendingSeconds = -1f;
      Publish();
    }

    /// <summary>
    /// Binds to whichever host is live, re-reading the saved mask when that changes. Keeps the
    /// previous binding while no host is live, so raising the goggles part-way through an
    /// adjustment does not throw the work away.
    /// </summary>
    private static void Rebind()
    {
      // The EQUIPPED host, not the actively-rendering one: this window has to be usable in
      // the stash, where the goggles are never on.
      var live = CotiState.EquippedHostTemplateId;
      if( live == null )
        return;

      // The binding is only settled once a device was actually FOUND. A host can go live before
      // the /coti/hosts fetch lands - the fetch is fire-and-forget from Awake - and caching that
      // failure would leave the window saying "no host resolved" for the rest of the session
      // with no way to recover short of a relaunch. Retrying costs one short list walk per frame.
      if( live == _boundHostId && _working != null )
        return;

      var device = CotiDeviceLookup.ByHostId( CotiHostTableClient.LastApplied, live );

      // Whatever was pending belonged to the previous host; it must not be written onto this one.
      _pendingSeconds = -1f;

      if( device?.Mask == null )
      {
        // Only on a genuine change of host, so a host that stays unresolvable does not wipe the
        // note line every frame.
        if( live != _boundHostId )
        {
          _boundHostId = live;
          _saved = null;
          _working = null;
          _deviceLabel = null;
          _note = null;
        }

        return;
      }

      _boundHostId = live;
      _note = null;

      _deviceLabel = device.DisplayName ?? device.Device ?? live;
      _saved = Copy( device.Mask );
      _working = Copy( device.Mask );
    }

    private static CotiMaskBlock Copy( CotiMaskBlock from )
    {
      return new CotiMaskBlock
      {
        CenterX = from.CenterX,
        CenterY = from.CenterY,
        Radius = from.Radius,
        Feather = from.Feather,
      };
    }

    /// <summary>
    /// Writes the working values onto the live host config. MaskGenerator compares all four
    /// against what it last built and rebuilds when any differs, so this is the whole of the live
    /// preview - there is nothing to invalidate by hand.
    /// </summary>
    private static void ApplyLive()
    {
      var hosts = Plugin.Config?.NvgHosts;
      if( hosts == null || _boundHostId == null || _working == null )
        return;

      CotiNvgHostConfig host;
      if( !hosts.TryGetValue( _boundHostId, out host ) || host == null )
        return;

      host.MaskCenterX = _working.CenterX;
      host.MaskCenterY = _working.CenterY;
      host.MaskRadius = _working.Radius;
      host.MaskFeather = _working.Feather;
    }

    private static void Reset()
    {
      if( _saved == null )
        return;

      _working = Copy( _saved );
      ApplyLive();

      // Nothing to save: _working now equals what the server already holds.
      _pendingSeconds = -1f;
      _note = "reset to the server's values";
    }

    private static void Publish()
    {
      _pendingSeconds = -1f;

      if( _boundHostId == null || _working == null )
        return;

      if( !CotiPoseTuner.PublishMask( _boundHostId, _working ) )
      {
        _note = CotiPoseTuner.LastPublishNote ?? "publish failed - see the log";
        return;
      }

      _saved = Copy( _working );
      _note = CotiPoseTuner.LastPublishNote ?? "published";
    }

    public static void Draw()
    {
      // _windowX null means Install never ran - TryEnable swallows a failure, and without this
      // both Tick and Draw would throw every frame rather than the feature simply being absent.
      if( !IsOpen || _windowX == null )
        return;

      // Unbound there are four lines of content, so holding the full height just looks broken.
      _rect.width = MinWidth;
      _rect.height = _working == null ? UnboundHeight : MinHeight;

      _rect = GUI.Window( WindowId, _rect, DrawContents, "COTI Mask" );

      if( _rect.x != _windowX.Value ) _windowX.Value = _rect.x;
      if( _rect.y != _windowY.Value ) _windowY.Value = _rect.y;
    }

    private static void DrawContents( int id )
    {
      // Same reason as the pose editor: the skin's window background is effectively transparent.
      CotiGuiFill.Window( _rect.width, _rect.height, "COTI Mask" );

      GUILayout.BeginVertical();

      if( _working == null )
      {
        GUILayout.Label( "No night vision host resolved yet." );
        GUILayout.Label( "Equip a supported goggle with the COTI attached and this window binds to "
            + "it on its own." );
      }
      else
      {
        GUILayout.Label( _deviceLabel );
        GUILayout.Label( CotiState.Active
            ? "Overlay is live - changes show as you make them."
            : "Overlay is OFF. Drop the goggles to see the circle." );

        GUILayout.Space( 6f );
        DrawPad();
        GUILayout.Space( 6f );

        Row( "centre X", _working.CenterX, _saved?.CenterX );
        Row( "centre Y", _working.CenterY, _saved?.CenterY );
        Row( "radius", _working.Radius, _saved?.Radius );
        Row( "edge fade", _working.Feather, _saved?.Feather );

        GUILayout.Space( 4f );
        GUILayout.Label( _pendingSeconds >= 0f ? "saving..." : "Saved automatically." );
        GUILayout.Label( $"Keys: {Short( _keyLeft.Value )}/{Short( _keyRight.Value )}/"
            + $"{Short( _keyUp.Value )}/{Short( _keyDown.Value )} move, "
            + $"{Short( _keyShrink.Value )}/{Short( _keyGrow.Value )} size, "
            + $"{Short( _keyHarder.Value )}/{Short( _keySofter.Value )} edge, "
            + $"{Short( _keyReset.Value )} reset." );
        // Read from the binding, never written as a literal. This line said "Hold Shift" for a
        // build after the modifier became Left Control, so the window was instructing the player
        // to press a key that does nothing - the same defect as the Num labels, in a sentence.
        GUILayout.Label( $"Hold {Short( _keyFine.Value )} for a finer step. The keys are the only "
            + "way in a raid, where the cursor is locked." );
      }

      GUILayout.Space( 4f );

      GUILayout.BeginHorizontal();

      // Close always works; Publish only means something once a device is bound.
      var wasEnabled = GUI.enabled;
      GUI.enabled = wasEnabled && _working != null;
      if( GUILayout.Button( $"Publish ({Short( _keyPublish.Value )})" ) )
        Publish();
      GUI.enabled = wasEnabled;

      if( GUILayout.Button( $"Close ({Short( _keyClose.Value )})" ) )
        Close();
      GUILayout.EndHorizontal();

      if( !string.IsNullOrEmpty( _note ) )
        GUILayout.Label( _note );

      GUILayout.EndVertical();

      GUI.DragWindow();
    }

    /// <summary>
    /// The same pad the pose editor uses. Real buttons, not the key legend this once was - the keys
    /// still matter because they are the only way in a raid, so they are listed below rather than
    /// crammed into the cells.
    /// </summary>
    private static void DrawPad()
    {
      var cell = CotiDpad.CellWidth( "Y+", "Y-", "X-", "X+", "size-", "size+", "edge-", "edge+" );
      // The same modifier as the keys, not CotiDpad.FineHeld's Shift: one control must not have
      // two different fine steps depending on whether it was clicked or pressed.
      var fine = Input.GetKey( _keyFine.Value );

      // Step 1, so the pad returns TICKS rather than an amount: CotiMaskNudge owns the per-axis
      // step sizes, and it applies one step per call. Anything else would give the buttons and the
      // keys different step sizes for the same control.
      var centre = CotiDpad.Pad( "mask", "mask", "Y+", "Y-", "X-", "X+", 1f, cell );
      var radius = CotiDpad.Pair( "maskr", "size", "size-", "size+", 1f, cell );
      var feather = CotiDpad.Pair( "maskf", "edge", "edge-", "edge+", 1f, cell );

      // Same sign as the key handler above - see its note; up is an increase.
      ApplyTicks( CotiMaskAxis.CenterY, centre.y, fine );
      ApplyTicks( CotiMaskAxis.CenterX, centre.x, fine );
      ApplyTicks( CotiMaskAxis.Radius, radius, fine );
      ApplyTicks( CotiMaskAxis.Feather, feather, fine );
    }

    /// <summary>
    /// Applies one nudge per whole tick the pad reported. Capped: a frame hitch could otherwise
    /// hand back a large accumulated tick count and jump the value across the screen in one frame.
    /// </summary>
    private static void ApplyTicks( CotiMaskAxis axis, float ticks, bool fine )
    {
      var whole = Mathf.RoundToInt( ticks );
      if( whole == 0 )
        return;

      var direction = whole > 0 ? 1 : -1;
      var count = Mathf.Min( Mathf.Abs( whole ), 8 );

      for( var i = 0; i < count; i++ )
        Nudge( axis, direction, fine );
    }

    /// <summary>
    /// Shortens a key name to fit a label WITHOUT renaming the key. Dropping the "Keypad" prefix
    /// outright was wrong and player-visible: KeypadEnter became "Enter" and Keypad0 became "0",
    /// both of which name a different physical key, so the window told you to press something that
    /// does nothing. "Num" keeps it short and keeps it true.
    /// </summary>
    private static string Short( KeyCode key )
    {
      var name = key.ToString();
      if( !name.StartsWith( "Keypad", StringComparison.Ordinal ) )
        return name;

      var rest = name.Substring( 6 );
      switch( rest )
      {
        case "Plus": rest = "+"; break;
        case "Minus": rest = "-"; break;
        case "Period": rest = "."; break;
        case "Divide": rest = "/"; break;
        case "Multiply": rest = "*"; break;
        case "Equals": rest = "="; break;
      }

      return "Num " + rest;
    }

    private static void Row( string label, float value, float? saved )
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label( label, GUILayout.Width( 74f ) );
      GUILayout.Label( value.ToString( "F4" ), GUILayout.Width( 62f ) );

      var delta = saved.HasValue ? value - saved.Value : 0f;
      GUILayout.Label(
          Math.Abs( delta ) < 0.00005f ? string.Empty : $"{( delta > 0f ? "+" : string.Empty )}{delta:F4}",
          GUILayout.Width( 62f ) );

      GUILayout.EndHorizontal();
    }
  }
}
