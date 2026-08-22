using System;
using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The four-way pad and labelled pair used by both editors. Three axes fit one cluster as a pad
  /// plus a flanking pair.
  ///
  /// Cells are labelled by axis and sign rather than with arrows: this panel already had to
  /// replace a UTF-8 delta sign with ASCII because the IMGUI font's coverage is not reliable, and
  /// "Y+" is less ambiguous than an up arrow for a thing with three axes anyway.
  ///
  /// Cell width is measured from the labels, never chosen - a hardcoded 42px clipped "Pitch+".
  /// </summary>
  public static class CotiDpad
  {
    private sealed class HoldState
    {
      public float HeldSeconds;
      public float Accumulator;
    }

    /// <summary>
    /// One entry per on-screen button, keyed by a fixed string per control. Cleared the frame a
    /// control stops being held, so the next press starts its own delay - see
    /// <see cref="CotiTunerStep"/>'s own note on why the accumulator is caller-owned.
    ///
    /// Only ever touched from <see cref="Cell"/>'s Repaint-gated branch - see its comment for why
    /// every other event type must leave this dictionary alone.
    /// </summary>
    private static readonly Dictionary<string, HoldState> Holds = new Dictionary<string, HoldState>();

    /// <summary>Widest of the given labels, plus padding, floored so a pad never looks cramped.</summary>
    public static float CellWidth( params string[] labels )
    {
      var widest = 0f;
      foreach( var label in labels )
      {
        var size = GUI.skin.button.CalcSize( new GUIContent( label ) );
        if( size.x > widest )
          widest = size.x;
      }

      return Mathf.Max( 34f, widest + 10f );
    }

    /// <summary>
    /// A four-way pad. Returns the amount to apply this frame: x for the horizontal axis, y for
    /// the vertical.
    /// </summary>
    public static Vector2 Pad( string keyPrefix, string centre, string up, string down,
        string left, string right, float step, float cell )
    {
      GUILayout.BeginVertical( GUILayout.Width( cell * 3f ) );

      GUILayout.BeginHorizontal();
      GUILayout.Space( cell );
      var vPlus = Cell( keyPrefix + "up", up, step, 1f, cell );
      GUILayout.Space( cell );
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      var hMinus = Cell( keyPrefix + "lt", left, step, -1f, cell );
      // The centre is a label, not a button. A dead-looking button reads worse than an honest one,
      // and every caller so far has a Reset already.
      GUILayout.Label( centre, GUILayout.Width( cell ) );
      var hPlus = Cell( keyPrefix + "rt", right, step, 1f, cell );
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Space( cell );
      var vMinus = Cell( keyPrefix + "dn", down, step, -1f, cell );
      GUILayout.Space( cell );
      GUILayout.EndHorizontal();

      GUILayout.EndVertical();

      return new Vector2( hMinus + hPlus, vPlus + vMinus );
    }

    /// <summary>
    /// A third axis, or anything else with two directions, as a labelled pair.
    /// </summary>
    public static float Pair( string keyPrefix, string label, string minus, string plus,
        float step, float cell, float labelWidth = 44f )
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label( label, GUILayout.Width( labelWidth ) );
      var down = Cell( keyPrefix + "-", minus, step, -1f, cell );
      var up = Cell( keyPrefix + "+", plus, step, 1f, cell );
      GUILayout.EndHorizontal();

      return down + up;
    }

    /// <summary>
    /// One RepeatButton wired to CotiTunerStep. Returns the signed step for this frame.
    ///
    /// Only Repaint may touch Holds or call Step. DoRepeatButton reports true under Repaint
    /// only, and Unity runs a Layout pass first where the control is never hot - doing the
    /// bookkeeping on every event type let Layout wipe the hold state an instant before Repaint
    /// recreated it at zero, collapsing the ramp to its tap branch every frame. RepeatButton itself
    /// must still be called on every pass so GUILayout's control count stays consistent.
    /// </summary>
    private static float Cell( string key, string label, float step, float sign, float width )
    {
      var pressed = GUILayout.RepeatButton( label, GUILayout.Width( width ) );

      if( Event.current.type != EventType.Repaint )
        return 0f;

      if( !pressed )
      {
        Holds.Remove( key );
        return 0f;
      }

      HoldState hold;
      if( !Holds.TryGetValue( key, out hold ) )
      {
        hold = new HoldState();
        Holds[key] = hold;
      }

      var dt = Time.unscaledDeltaTime;
      var amount = CotiTunerStep.Step( hold.HeldSeconds, dt, ref hold.Accumulator, step, fine: false );
      hold.HeldSeconds += dt;

      return amount * sign;
    }

    /// <summary>Shift, the fine modifier both windows share.</summary>
    public static bool FineHeld()
    {
      return Input.GetKey( KeyCode.LeftShift ) || Input.GetKey( KeyCode.RightShift );
    }
  }
}
