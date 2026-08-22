using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Draws the pose editor. Arithmetic lives in CotiPoseTuner and CotiDpad; this only draws.
  ///
  /// OnGUI runs several times a frame and allocates on every call, so Draw does nothing at all
  /// unless the panel is open. Its buttons are inert in a raid because the cursor is locked -
  /// deliberate, since the readout is still worth having with the goggles worn.
  /// </summary>
  public static class CotiTunerPanel
  {
    // An arbitrary but stable id - GUI.Window ids only need to be unique among windows drawn by
    // this plugin, and this is the only one.
    private const int WindowId = 0x434f5449;

    private const float MetresToMm = 1000f;

    // Wide enough for the widest control row plus a scrollbar.
    private const float MinWidth = 398f + 20f + MinPreviewSide + 24f;

    // Floor for the header, the smallest useful preview, its buttons and the footer.
    private const float MinHeight = MinPreviewSide + ChromeHeight;
    private const float GripSize = 16f;

    // The preview is SQUARE and sized from the window rather than fixed, so the render fills
    // whatever room the window is given. The render target is square too (see CotiTunerPreview),
    // so nothing is stretched.
    private const float MinPreviewSide = 180f;
    private const float MaxPreviewSide = 512f;

    // Everything in the window that is not the preview square: title bar, the three header lines,
    // the viewport's own button row, the footer and its note, and the spacing between them.
    private const float ChromeHeight = 190f;

    // How much of the window width the preview may take before the controls start losing room.
    private const float PreviewWidthFraction = 0.5f;

    private static ConfigEntry<float> _windowX;
    private static ConfigEntry<float> _windowY;
    private static ConfigEntry<float> _windowWidth;
    private static ConfigEntry<float> _windowHeight;

    private static Rect _rect;
    private static bool _resizing;

    /// <summary>Where the window is, for CotiUiBlocker's cursor hit-test.</summary>
    public static Rect WindowRect => _rect;
    private static Vector2 _scroll;

    // Same shape as _resizing: set true on a MouseDown that started inside the viewport rect,
    // cleared on MouseUp, and only MouseDrag events while true move the camera - so a drag that
    // carries the mouse outside the (small) viewport rect mid-motion still keeps orbiting rather
    // than dropping the input the instant the cursor crosses the edge.
    private static bool _orbiting;

    public static void Install( ConfigFile file )
    {
      const string section = "Tuner Window";

      _windowX = file.Bind( section, "X", 80f,
          "Pose editor window position. Set by dragging the window; not meant to be hand-edited." );
      _windowY = file.Bind( section, "Y", 80f,
          "Pose editor window position. Set by dragging the window; not meant to be hand-edited." );
      _windowWidth = file.Bind( section, "Width", 460f,
          "Pose editor window size. Set by dragging the resize grip; not meant to be hand-edited." );
      // Clamped against the current minimum: a config file written before it was raised carries a
      // smaller value.
      _windowHeight = file.Bind( section, "Height", MinHeight,
          "Pose editor window size. Set by dragging the resize grip; not meant to be hand-edited." );

      // Clamped against the current minimum rather than trusted outright - a config file written
      // before MinWidth was raised could still carry a narrower value.
      _rect = new Rect(
          _windowX.Value, _windowY.Value,
          Mathf.Max( MinWidth, _windowWidth.Value ), Mathf.Max( MinHeight, _windowHeight.Value ) );
    }

    public static void Draw()
    {
      if( !CotiPoseTuner.IsOpen )
        return;

      _rect = GUI.Window( WindowId, _rect, DrawWindow, "COTI Pose" );
    }

    private static void DrawWindow( int id )
    {
      // Read once per Draw rather than once per row: each read walks the device table and
      // allocates a fresh CotiDeviceFile/CotiMountBlock (Bake), and this method runs several
      // times a frame (Layout, Repaint, and once per input event) - seven rows worth of
      // allocation collapses to one Saved and one Current.
      var saved = CotiPoseTuner.Saved;
      var current = CotiPoseTuner.Current;

      // Before anything is laid out: the skin's own window background is effectively transparent,
      // so without this the inventory reads straight through the panel body.
      CotiGuiFill.Window( _rect.width, _rect.height, "COTI Pose" );

      DrawHeader( saved );
      GUILayout.Space( 4f );

      var fine = CotiDpad.FineHeld();
      var side = PreviewSide();

      GUILayout.BeginHorizontal();

      // Left: the preview square and the two view buttons directly under it.
      GUILayout.BeginVertical( GUILayout.Width( side ) );
      DrawViewport( side );
      GUILayout.EndVertical();

      GUILayout.Space( 8f );

      // Right: every control, scrolled. This is what lets the window be shorter than the sum of
      // its rows instead of taller than the screen it sits over.
      GUILayout.BeginVertical();
      _scroll = GUILayout.BeginScrollView( _scroll );

      DrawAnchorRow();
      GUILayout.Space( 4f );
      DrawFlipTestRow();
      GUILayout.Space( 6f );

      DrawPads( fine, saved, current );

      GUILayout.EndScrollView();
      GUILayout.EndVertical();

      GUILayout.EndHorizontal();

      GUILayout.Space( 6f );
      DrawFooter();

      DrawResizeGrip();

      GUI.DragWindow( new Rect( 0f, 0f, _rect.width, 20f ) );

      PersistRectOnRelease();
    }

    /// <summary>
    /// The preview square's side, from the window size. Bounded below by what is too small to judge
    /// and above by the render target, and constrained by height so a wide short window cannot push
    /// it past the room the header and footer leave.
    /// </summary>
    private static float PreviewSide()
    {
      var byHeight = _rect.height - ChromeHeight;
      var byWidth = _rect.width * PreviewWidthFraction;

      return Mathf.Clamp( Mathf.Min( byHeight, byWidth ), MinPreviewSide, MaxPreviewSide );
    }

    private static void DrawHeader( CotiDeviceFile saved )
    {
      GUILayout.Label( $"Host: {CotiPoseTuner.OpenHostId ?? "(none)"}  ({CotiPoseTuner.OpenHostName})" );
      GUILayout.Label( $"Bounds: {CotiPoseTuner.MeasuredBoundsLabel}" );

      GUILayout.Label( saved == null
          ? "Tuned: (device not found in the host table)"
          : $"Tuned: {( saved.Tuned ? "yes" : "no - auto-generated stub" )}" );
    }

    /// <summary>
    /// The preview viewport. Falls back to a label rather than a blank rect, because "no camera
    /// yet", "model not in scene" and an actual render are three different situations.
    /// </summary>
    private static void DrawViewport( float side )
    {
      GUILayout.Label( "Drag to orbit, scroll to zoom" );

      var viewportRect = GUILayoutUtility.GetRect( side, side, GUILayout.Width( side ),
          GUILayout.Height( side ) );

      var texture = CotiTunerPreview.Texture;
      if( texture != null )
      {
        // The render target is opaque and white-backed even though the camera clears to dark grey.
        // Cosmetic, and a pale backdrop shows a dark COTI against a tan goggle better than grey did.
        GUI.DrawTexture( viewportRect, texture, ScaleMode.StretchToFill );
      }
      else
        GUI.Box( viewportRect, CotiTunerPreview.HasLiveModel ? "(preview unavailable)" : "(model not in scene)" );

      HandleViewportInput( viewportRect );

      GUILayout.BeginHorizontal();

      if( GUILayout.Button( "Reset view" ) )
        CotiTunerPreview.ResetView();

      if( GUILayout.Button( "Frame COTI" ) )
        CotiTunerPreview.FrameCurrent();

      GUILayout.EndHorizontal();
    }

    /// <summary>
    /// Orbit and zoom. The drag is tracked by a caller-owned flag so it survives the mouse leaving
    /// the rect, and the scroll is read on Repaint only - mouseScrollDelta holds the same value
    /// across every call in a frame, so reading it twice applies it twice.
    /// </summary>
    private static void HandleViewportInput( Rect viewportRect )
    {
      var evt = Event.current;

      if( evt.type == EventType.MouseDown && viewportRect.Contains( evt.mousePosition ) )
      {
        _orbiting = true;
        evt.Use();
      }
      else if( evt.type == EventType.MouseUp )
      {
        _orbiting = false;
      }
      else if( _orbiting && evt.type == EventType.MouseDrag )
      {
        CotiTunerPreview.Orbit( evt.delta.x, evt.delta.y );
        evt.Use();
      }
      else if( evt.type == EventType.Repaint && viewportRect.Contains( evt.mousePosition ) )
      {
        var scroll = Input.mouseScrollDelta.y;
        if( scroll != 0f )
          CotiTunerPreview.Zoom( scroll );
      }
    }

    /// <summary>
    /// The anchor bone choice: the current (pending) value with cycle buttons over every transform
    /// name ReportHostBones found, plus the CurveRotator's own suggestion when this host has one -
    /// offered, not forced, so a human still clicks "Use" rather than it being applied on its own.
    /// </summary>
    private static void DrawAnchorRow()
    {
      GUILayout.BeginHorizontal();

      GUILayout.Label( "Anchor bone", GUILayout.Width( 96f ) );
      GUILayout.Label( CotiPoseTuner.AnchorBoneLabel, GUILayout.Width( 160f ) );

      if( GUILayout.Button( "<", GUILayout.Width( 22f ) ) )
        CotiPoseTuner.CycleAnchorBone( -1 );

      if( GUILayout.Button( ">", GUILayout.Width( 22f ) ) )
        CotiPoseTuner.CycleAnchorBone( 1 );

      GUILayout.EndHorizontal();

      var suggested = CotiPoseTuner.SuggestedAnchorBoneLabel;

      GUILayout.BeginHorizontal();

      if( suggested == null )
      {
        GUILayout.Label( "Flip axis: no CurveRotator seen on this host - it does not flip" );
      }
      else
      {
        GUILayout.Label( $"Flip axis suggests: {suggested}" );

        if( GUILayout.Button( "Use", GUILayout.Width( 50f ) ) )
          CotiPoseTuner.UseSuggestedAnchorBone();
      }

      GUILayout.EndHorizontal();
    }

    /// <summary>
    /// Snap and animate as separate rows: snap shows whether the anchor is on the moving part at
    /// all, animate catches a pose that is fine at both extremes but sweeps through the host.
    /// Disabled with a reason when the host has no CurveRotator - that message is itself the answer
    /// to whether the device needs a special anchor.
    /// </summary>
    private static void DrawFlipTestRow()
    {
      GUILayout.Label( "Flip test - the check that proves the anchor moves with the goggle:" );

      var reason = CotiPoseTuner.FlipUnavailableReason;
      var enabled = reason == null;
      var wasEnabled = GUI.enabled;

      GUILayout.BeginHorizontal();
      GUILayout.Label( "Snap", GUILayout.Width( 60f ) );
      GUI.enabled = wasEnabled && enabled;

      // Flip UP is the stowed position, so deployed: false. See FlipSnap's own note - passing
      // "up = true" straight through reversed both buttons in game.
      if( GUILayout.Button( "Flip up" ) )
        CotiPoseTuner.FlipSnap( deployed: false );
      if( GUILayout.Button( "Flip down" ) )
        CotiPoseTuner.FlipSnap( deployed: true );

      GUI.enabled = wasEnabled;
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label( "Animate", GUILayout.Width( 60f ) );
      GUI.enabled = wasEnabled && enabled;

      if( GUILayout.Button( "Flip up" ) )
        CotiPoseTuner.FlipAnimate( deployed: false );
      if( GUILayout.Button( "Flip down" ) )
        CotiPoseTuner.FlipAnimate( deployed: true );

      GUI.enabled = wasEnabled;
      GUILayout.EndHorizontal();

      if( !enabled )
        GUILayout.Label( reason );
    }

    /// <summary>
    /// Position and rotation pads side by side, then scale, then one compact readout for all seven
    /// values. Replaces seven full-width live/saved/delta rows: every number is still here, but
    /// they no longer set the window's width or eat its height.
    /// </summary>
    private static void DrawPads( bool fine, CotiDeviceFile saved, CotiDeviceFile current )
    {
      // Measured, not chosen: a hardcoded 42 clipped "Pitch+" to "itch+", and any fixed number is
      // one font or one label away from doing it again.
      var posCell = CotiDpad.CellWidth( "X-", "X+", "Y+", "Y-", "Z-", "Z+" );
      var rotCell = CotiDpad.CellWidth( "Pitch+", "Pitch-", "Yaw-", "Yaw+", "Roll-", "Roll+" );

      var stepMm = Plugin.Config?.TunerStepMm ?? 2f;
      if( fine )
        stepMm /= CotiPoseTuner.FineDivisorDistance;

      var stepDegrees = Plugin.Config?.TunerStepDegrees ?? 5f;
      if( fine )
        stepDegrees /= CotiPoseTuner.FineDivisorAngle;

      GUILayout.BeginHorizontal();

      // Position: X across, Y up, Z the pair. Axis indices match Component()'s own ordering.
      GUILayout.BeginVertical();
      GUILayout.Label( "Position (mm)" );
      var pos = CotiDpad.Pad( "pos", "mm", "Y+", "Y-", "X-", "X+", stepMm, posCell );
      var posZ = CotiDpad.Pair( "posz", "depth", "Z-", "Z+", stepMm, posCell );
      GUILayout.EndVertical();

      GUILayout.Space( 12f );

      // Rotation: yaw across, pitch up, roll the pair - the pad matches how the device turns
      // rather than the order the fields happen to sit in the file.
      GUILayout.BeginVertical();
      GUILayout.Label( "Rotation (deg)" );
      var rot = CotiDpad.Pad( "rot", "deg", "Pitch+", "Pitch-", "Yaw-", "Yaw+", stepDegrees, rotCell );
      var rotRoll = CotiDpad.Pair( "rotr", "roll", "Roll-", "Roll+", stepDegrees, rotCell );
      GUILayout.EndVertical();

      GUILayout.EndHorizontal();

      if( pos.x != 0f ) CotiPoseTuner.NudgePosition( 0, pos.x / MetresToMm );
      if( pos.y != 0f ) CotiPoseTuner.NudgePosition( 1, pos.y / MetresToMm );
      if( posZ != 0f ) CotiPoseTuner.NudgePosition( 2, posZ / MetresToMm );

      if( rot.y != 0f ) CotiPoseTuner.NudgeRotation( 0, rot.y );
      if( rot.x != 0f ) CotiPoseTuner.NudgeRotation( 1, rot.x );
      if( rotRoll != 0f ) CotiPoseTuner.NudgeRotation( 2, rotRoll );

      GUILayout.Space( 8f );

      var stepScale = Plugin.Config?.TunerStepScale ?? 0.01f;
      if( fine )
        stepScale /= CotiPoseTuner.FineDivisorScale;

      var scale = CotiDpad.Pair( "scale", "scale", "-", "+", stepScale, posCell );
      if( scale != 0f )
        CotiPoseTuner.NudgeScale( scale );

      GUILayout.Space( 8f );
      DrawReadout( saved, current );
    }

    /// <summary>
    /// All seven values in three lines. Each shows live, and its delta only when there is one - a
    /// permanent "delta 0.0" column was most of what made the old rows as wide as they were.
    /// Yellow still marks a value that differs from what the server holds.
    /// </summary>
    private static void DrawReadout( CotiDeviceFile saved, CotiDeviceFile current )
    {
      GUILayout.BeginHorizontal();
      ReadoutCell( "X", MetresToMm * Component( saved, 0, false ),
          MetresToMm * Component( current, 0, false ), "{0:F1}" );
      ReadoutCell( "Y", MetresToMm * Component( saved, 1, false ),
          MetresToMm * Component( current, 1, false ), "{0:F1}" );
      ReadoutCell( "Z", MetresToMm * Component( saved, 2, false ),
          MetresToMm * Component( current, 2, false ), "{0:F1}" );
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      ReadoutCell( "Pitch", Component( saved, 0, true ), Component( current, 0, true ), "{0:F1}" );
      ReadoutCell( "Yaw", Component( saved, 1, true ), Component( current, 1, true ), "{0:F1}" );
      ReadoutCell( "Roll", Component( saved, 2, true ), Component( current, 2, true ), "{0:F1}" );
      GUILayout.EndHorizontal();

      ReadoutCell( "Scale", saved?.Mount?.Scale ?? 1f, current?.Mount?.Scale ?? 1f, "{0:F3}" );
    }

    private static void ReadoutCell( string label, float savedValue, float currentValue, string format )
    {
      var delta = currentValue - savedValue;
      var changed = Math.Abs( delta ) > 0.0005f;

      var previousColour = GUI.color;
      if( changed )
        GUI.color = Color.yellow;

      var live = string.Format( format, currentValue );
      GUILayout.Label(
          changed ? label + " " + live + " (" + string.Format( format, delta ) + ")" : label + " " + live,
          GUILayout.Width( 136f ) );

      GUI.color = previousColour;
    }

    private static float Component( CotiDeviceFile device, int axis, bool isRotation )
    {
      var mount = device?.Mount;
      if( mount == null )
        return 0f;

      if( isRotation )
      {
        switch( axis )
        {
          case 0: return mount.PitchDegrees;
          case 1: return mount.YawDegrees;
          default: return mount.RollDegrees;
        }
      }

      switch( axis )
      {
        case 0: return mount.PositionX;
        case 1: return mount.PositionY;
        default: return mount.PositionZ;
      }
    }

    // ---- Footer, resize, persistence -------------------------------------------------------------

    private static void DrawFooter()
    {
      GUILayout.BeginHorizontal();

      if( GUILayout.Button( "Reset" ) )
        CotiPoseTuner.Reset();

      if( GUILayout.Button( "Publish" ) )
        CotiPoseTuner.Publish();

      if( GUILayout.Button( "Close" ) )
        CotiPoseTuner.Close();

      GUILayout.EndHorizontal();

      var note = CotiPoseTuner.LastPublishNote;
      if( !string.IsNullOrEmpty( note ) )
        GUILayout.Label( note );
    }

    private static void DrawResizeGrip()
    {
      var gripRect = new Rect( _rect.width - GripSize, _rect.height - GripSize, GripSize, GripSize );
      GUI.Box( gripRect, string.Empty );

      var evt = Event.current;

      if( evt.type == EventType.MouseDown && gripRect.Contains( evt.mousePosition ) )
      {
        _resizing = true;
        evt.Use();
      }
      else if( evt.type == EventType.MouseUp )
      {
        _resizing = false;
      }
      else if( _resizing && evt.type == EventType.MouseDrag )
      {
        _rect.width = Mathf.Max( MinWidth, _rect.width + evt.delta.x );
        _rect.height = Mathf.Max( MinHeight, _rect.height + evt.delta.y );
        evt.Use();
      }
    }

    /// <summary>
    /// Written on mouse-up rather than every frame - BepInEx's ConfigFile saves to disk on every
    /// value set, and a drag or resize fires many frames in a row. One write when the mouse comes
    /// up (after a drag, a resize, or just a button click) is enough to survive a relaunch without
    /// turning every drag into a stream of disk writes.
    /// </summary>
    private static void PersistRectOnRelease()
    {
      if( Event.current.type != EventType.MouseUp )
        return;

      if( _windowX == null || _windowY == null || _windowWidth == null || _windowHeight == null )
        return;

      _windowX.Value = _rect.x;
      _windowY.Value = _rect.y;
      _windowWidth.Value = _rect.width;
      _windowHeight.Value = _rect.height;
    }
  }
}
