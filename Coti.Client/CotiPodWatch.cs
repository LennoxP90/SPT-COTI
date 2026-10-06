using System;
using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Takes the filled tubes whose pod is flipped up out of the lit set: such a tube closes as if the night vision were
  /// off for it. A pod is up when its bone's live rotation is more than 45 degrees from the down rotation its device
  /// file declares (<see cref="CotiPodGate"/>). Bones only, no other mod's types, so any mod that rotates pod bones
  /// works.
  ///
  /// Only the local player's own goggles are read; the overlay is local. A pod bone is found through the tube's own
  /// bone, which CotiMountBonePatch names after its slot and parents to the tube's anchor: the first bone above it with
  /// the pod's name. It is kept until the inventory or the host table changes or the bone is destroyed. One not found
  /// counts as down, as every tube did before pods existed.
  /// </summary>
  internal static class CotiPodWatch
  {
    /// <summary>How long a pod bone that was not found waits before the player's hierarchy is searched again.</summary>
    private const float RetrySeconds = 0.5f;

    private static readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
    private static readonly Dictionary<string, float> RetryAt = new Dictionary<string, float>();
    private static readonly HashSet<string> LoggedMissing = new HashSet<string>();

    private static int _loggedFilled;
    private static int _loggedLit;

    internal static void Invalidate()
    {
      Bones.Clear();
      RetryAt.Clear();
    }

    /// <summary>
    /// <paramref name="filledSlots"/> without the tubes on a pod that is up, as CotiTubeSet bits. Logs one line
    /// whenever the filled or the lit set changes.
    /// </summary>
    internal static int LitSlots( string hostTemplateId, int filledSlots )
    {
      var host = Host( hostTemplateId );
      var layout = host?.Layout;
      var up = 0;

      if( filledSlots != 0 && host?.Tubes != null && layout != null )
      {
        for( var i = 0; i < layout.Tubes.Count; i++ )
        {
          var label = layout.Tubes[i].Label;
          var slot = CotiTubes.SlotName( label, layout );
          var bit = CotiTubeSet.Bit( slot );

          CotiTube tube;
          if( ( filledSlots & bit ) == 0 || !host.Tubes.TryGetValue( label, out tube ) || tube?.Pod == null )
            continue;

          var bone = PodBone( slot, PodBoneName( tube ) );
          if( bone == null )
            continue;

          var q = bone.localRotation;
          if( CotiPodGate.IsUp( new CotiQuat( q.x, q.y, q.z, q.w ), tube.Pod ) )
            up |= bit;
        }
      }

      var lit = filledSlots & ~up;
      if( filledSlots != _loggedFilled || lit != _loggedLit )
        LogSets( filledSlots, lit, host, layout );

      return lit;
    }

    private static CotiNvgHostConfig Host( string hostTemplateId )
    {
      var hosts = Plugin.Config?.NvgHosts;
      CotiNvgHostConfig host;
      return hosts != null && hostTemplateId != null && hosts.TryGetValue( hostTemplateId, out host ) ? host : null;
    }

    /// <summary>The pod's own bone when it names one, else the tube's anchor.</summary>
    private static string PodBoneName( CotiTube tube )
    {
      return string.IsNullOrEmpty( tube.Pod.Bone ) ? tube.Mount?.AnchorBone : tube.Pod.Bone;
    }

    private static Transform PodBone( string slot, string name )
    {
      Transform bone;
      if( Bones.TryGetValue( slot, out bone ) && bone != null )
        return bone;

      float retryAt;
      var now = Time.realtimeSinceStartup;
      if( RetryAt.TryGetValue( slot, out retryAt ) && now < retryAt )
        return null;

      bone = Find( slot, name );
      if( bone == null )
      {
        // Find parks a misnamed pod bone until the next Invalidate; anything else is retried.
        if( !RetryAt.TryGetValue( slot, out retryAt ) || retryAt != float.MaxValue )
          RetryAt[slot] = now + RetrySeconds;
        return null;
      }

      Bones[slot] = bone;
      return bone;
    }

    /// <summary>
    /// The first bone named <paramref name="name"/> above the tube's own bone. Logged once when the tube's bone is there
    /// and the pod's is not, which is a device file naming the wrong bone; a tube bone not built yet is only retried.
    /// </summary>
    private static Transform Find( string slot, string name )
    {
      var player = CotiNvgHost.LocalPlayer;
      if( player == null )
        return null;

      var root = player.gameObject.transform;
      var tubeBone = EftCompat.FindTransformRecursive( root, slot, ignoreCase: true );
      if( tubeBone == null )
        return null;

      for( var t = tubeBone.parent; t != null && t != root; t = t.parent )
      {
        if( string.Equals( t.name, name, StringComparison.OrdinalIgnoreCase ) )
          return t;
      }

      if( LoggedMissing.Add( slot + "/" + name ) )
      {
        Plugin.Log.LogWarning(
            $"[COTI] pod bone '{name ?? "(none)"}' not found above {slot} on the local player's goggles - " +
            "that tube counts as down" );
      }

      RetryAt[slot] = float.MaxValue;
      return null;
    }

    private static void LogSets( int filled, int lit, CotiNvgHostConfig host, CotiLayout layout )
    {
      _loggedFilled = filled;
      _loggedLit = lit;

      var pods = new List<string>();
      for( var i = 0; layout != null && i < layout.Tubes.Count; i++ )
      {
        var label = layout.Tubes[i].Label;
        CotiTube tube;
        if( ( filled & ~lit & CotiTubeSet.Bit( CotiTubes.SlotName( label, layout ) ) ) == 0
            || !host.Tubes.TryGetValue( label, out tube ) )
          continue;

        var text = "pod " + PodBoneName( tube ) + " up";
        if( !pods.Contains( text ) )
          pods.Add( text );
      }

      Plugin.Log.LogInfo(
          $"[COTI] tubes lit {CotiTubeSet.Describe( lit, layout )} of filled {CotiTubeSet.Describe( filled, layout )}" +
          ( pods.Count == 0 ? "" : " (" + string.Join( ", ", pods ) + ")" ) );
    }
  }
}
