using System.Runtime.CompilerServices;
using Coti.Shared;
using EFT;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The capsules a body is mirrored as: exactly <see cref="CotiCapsuleBounds.CapsulesPerBody"/>, tapered along each limb
  /// so a reflection reads as a body rather than a row of sausages. Every end comes from a bone; the feet, which
  /// PlayerBones does not name, are found under each calf once per body and kept.
  /// </summary>
  internal static class CotiBodyCapsules
  {
    private sealed class Feet
    {
      internal Transform Left, Right;
    }

    private static readonly ConditionalWeakTable<Player, Feet> FeetOf = new ConditionalWeakTable<Player, Feet>();
    private static readonly float[] Ends = new float[CotiCapsuleBounds.CapsulesPerBody * 6];
    private static readonly float[] Radii = new float[CotiCapsuleBounds.CapsulesPerBody];
    private static Vector4[] _out;
    private static int _at, _n;

    /// <summary>
    /// Writes one body's capsules into <paramref name="capsules"/> from capsule index <paramref name="first"/> (two
    /// Vector4 each: one end and its Celsius, the other end and its radius) and returns the sphere that holds them.
    /// </summary>
    internal static Vector4 Write( Player player, PlayerBones bones, float celsius, Vector4[] capsules, int first )
    {
      _out = capsules;
      _at = first;
      _n = 0;

      var up = Vector3.up;
      var forward = player.Transform.forward;
      forward.y = 0f;
      forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
      var right = Vector3.Cross( up, forward );

      var head = bones.Head.position;
      var neck = bones.Neck.position;
      var spine3 = bones.Spine3.position;
      var ribcage = bones.Ribcage.position;
      var pelvis = bones.Pelvis.position;
      var shoulderL = bones.Upperarms[0].position;
      var shoulderR = bones.Upperarms[1].position;
      var thighL = bones.LeftThigh1.position;
      var thighR = bones.RightThigh1.position;
      var kneeL = bones.LeftThigh2.position;
      var kneeR = bones.RightThigh2.position;
      var feet = FeetOf.GetValue( player, Find );
      var ground = player.Position.y;

      // Head and neck.
      Add( head + up * 0.02f, head + up * 0.10f, 0.09f, celsius );
      Add( head + forward * 0.05f + up * 0.02f, head + forward * 0.06f - up * 0.06f, 0.06f, celsius );
      Add( head - up * 0.05f + forward * 0.02f, neck + forward * 0.03f, 0.05f, celsius );
      Add( neck, head, 0.05f, celsius );

      // Torso: a flat chest and back from side-by-side capsules, the spine, the belly, the hips.
      Add( spine3, neck, 0.08f, celsius );
      Add( shoulderL + up * 0.02f, shoulderR + up * 0.02f, 0.06f, celsius );
      for( var side = -1f; side <= 1f; side += 2f )
      {
        var lateral = right * ( side * 0.08f );
        Add( ribcage + lateral + forward * 0.03f, neck - up * 0.08f + lateral + forward * 0.03f, 0.10f, celsius );
        Add( ribcage + lateral - forward * 0.05f, neck - up * 0.08f + lateral - forward * 0.05f, 0.10f, celsius );
        Add( pelvis + right * ( side * 0.06f ) + up * 0.05f, ribcage + right * ( side * 0.07f ), 0.10f, celsius );
      }
      Add( thighL, thighR, 0.10f, celsius );
      Add( pelvis, pelvis - up * 0.08f, 0.09f, celsius );

      // Arms and legs, each tapered over two segments, with a shoulder cap, a hand and a foot.
      Arm( shoulderL, bones.Forearms[0].position, bones.LeftPalm.position, celsius );
      Arm( shoulderR, bones.Forearms[1].position, bones.RightPalm.position, celsius );
      Leg( thighL, kneeL, Ankle( feet.Left, kneeL, ground ), forward, celsius );
      Leg( thighR, kneeR, Ankle( feet.Right, kneeR, ground ), forward, celsius );

      var sphere = CotiCapsuleBounds.Sphere( Ends, Radii, _n );
      return new Vector4( sphere.X, sphere.Y, sphere.Z, sphere.Radius );
    }

    private static void Arm( Vector3 shoulder, Vector3 elbow, Vector3 palm, float celsius )
    {
      var upper = ( shoulder + elbow ) / 2f;
      var fore = ( elbow + palm ) / 2f;
      Add( shoulder, shoulder + ( elbow - shoulder ) * 0.15f, 0.075f, celsius );
      Add( shoulder, upper, 0.055f, celsius );
      Add( upper, elbow, 0.048f, celsius );
      Add( elbow, fore, 0.045f, celsius );
      Add( fore, palm, 0.038f, celsius );
      Add( palm, palm + ( palm - elbow ).normalized * 0.09f, 0.04f, celsius );
    }

    private static void Leg( Vector3 hip, Vector3 knee, Vector3 ankle, Vector3 forward, float celsius )
    {
      var thigh = ( hip + knee ) / 2f;
      var calf = ( knee + ankle ) / 2f;
      Add( hip, thigh, 0.085f, celsius );
      Add( thigh, knee, 0.065f, celsius );
      Add( knee, calf, 0.06f, celsius );
      Add( calf, ankle, 0.045f, celsius );
      Add( ankle, ankle + forward * 0.17f - Vector3.up * 0.03f, 0.045f, celsius );
    }

    private static Vector3 Ankle( Transform foot, Vector3 knee, float ground )
    {
      return foot != null ? foot.position : new Vector3( knee.x, ground + 0.08f, knee.z );
    }

    private static void Add( Vector3 a, Vector3 b, float radius, float celsius )
    {
      var i = ( _at + _n ) * 2;
      _out[i] = new Vector4( a.x, a.y, a.z, celsius );
      _out[i + 1] = new Vector4( b.x, b.y, b.z, radius );
      var e = _n * 6;
      Ends[e] = a.x; Ends[e + 1] = a.y; Ends[e + 2] = a.z;
      Ends[e + 3] = b.x; Ends[e + 4] = b.y; Ends[e + 5] = b.z;
      Radii[_n] = radius;
      _n++;
    }

    private static Feet Find( Player player )
    {
      var bones = player.PlayerBones;
      return new Feet { Left = FootUnder( bones.LeftThigh2.Original ), Right = FootUnder( bones.RightThigh2.Original ) };
    }

    private static Transform FootUnder( Transform calf )
    {
      if( calf == null )
        return null;
      foreach( var child in calf.GetComponentsInChildren<Transform>( true ) )
        if( child.name.EndsWith( "Foot" ) )
          return child;
      return null;
    }
  }
}
