using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// Each scope lens's own mesh, remembered while the weapon is lowered. PiP-Disabler swaps the lens for an empty mesh
  /// while aiming and puts it back after, so a window drawn from the renderer while aimed would be empty.
  /// </summary>
  internal static class CotiLensMeshes
  {
    private const float RescanSeconds = 1f;

    private static readonly Dictionary<Renderer, Mesh> Real = new Dictionary<Renderer, Mesh>();
    private static float _nextScan;

    /// <summary>The lens's remembered mesh while its renderer holds an empty one, else null (draw the renderer itself).</summary>
    internal static Mesh Hidden( Renderer lens )
    {
      if( !Plugin.PipDisabler || lens == null || !Real.TryGetValue( lens, out var mesh ) || mesh == null )
        return null;

      var now = lens.GetComponent<MeshFilter>()?.sharedMesh;
      return now == null || now.vertexCount == 0 ? mesh : null;
    }

    /// <summary>Once a second while not aiming: every sight on the weapon in hand, through the aim bones EFT lists.</summary>
    internal static void Tick()
    {
      if( Time.realtimeSinceStartup < _nextScan )
        return;
      _nextScan = Time.realtimeSinceStartup + RescanSeconds;

      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      var animation = world?.MainPlayer?.ProceduralWeaponAnimation;
      if( animation == null || animation.IsAiming )
        return;

      // Lenses of weapons long gone; whatever is in hand is learnt again within the second.
      if( Real.Count > 64 )
        Real.Clear();

      foreach( var scope in animation.ScopeAimTransforms )
      {
        // The aim bone is not under the OpticSight, so search the sight's item from its root, as WindowOf does.
        var item = scope?.Bone != null ? scope.Bone.GetComponentInParent<ScopePrefabCache>() : null;
        if( item == null )
          continue;

        foreach( var sight in item.GetComponentsInChildren<OpticSight>( true ) )
          Remember( sight.LensRenderer );
      }
    }

    private static void Remember( Renderer lens )
    {
      var mesh = lens != null ? lens.GetComponent<MeshFilter>()?.sharedMesh : null;
      if( mesh == null || mesh.vertexCount == 0 || ( Real.TryGetValue( lens, out var known ) && known == mesh ) )
        return;

      Real[lens] = mesh;
      if( Plugin.Config?.VerboseLogging ?? false )
        Plugin.Log.LogInfo( $"[COTI] Lens mesh remembered: {lens.name} ({mesh.name}, {mesh.vertexCount} vertices)" );
    }
  }
}
