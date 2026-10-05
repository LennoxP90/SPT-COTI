using System.Collections.Generic;
using Coti.Shared;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// Each lit lamp's shape for the thermal image, found once: where its glow is, and which of its renderers are glow or
  /// housing. The glow is found by drawing each glowing part laid flat in texture space with Coti/GlowBake, which
  /// appends every lit point of its surface to a buffer read back without stalling (AsyncGPUReadback); the points, one
  /// per 2 cm, are grouped into spots (CotiLampHeat.Spots), each a bulb the housing warms around. A lamp's light components decide nothing unless no glow
  /// is found: they are placed to light the room, not at the bulbs. A lamp whose glowing parts the game's culling has
  /// switched off is shaped once they are drawn again.
  /// </summary>
  internal static class CotiLampShapes
  {
    private const int Texels = 64;
    // Room for a part's surface drawn four times over: parts reuse texture space (a tripod light's two heads share one
    // lens), and every copy appends its own points.
    private const int MaxPoints = Texels * Texels * 4;
    private const int InFlight = 4;
    private const float PointMetres = 0.02f;
    private const float BoundsMarginMetres = 0.05f;

    private static readonly int EmissionMapId = Shader.PropertyToID( "_EmissionMap" );
    private static readonly Vector4[] NoPoints = new Vector4[MaxPoints];

    internal sealed class Shape
    {
      /// <summary>The lamp's other renderers, warm within the cubes and cold beyond them.</summary>
      internal readonly List<Renderer> Housing = new List<Renderer>();

      internal readonly Vector4[] Bulbs = new Vector4[CotiLampHeat.MaxBulbs];
      internal int BulbCount;
      internal bool Ready;
      internal int Waiting;
      internal readonly List<float> Points = new List<float>();
      internal readonly HashSet<long> Seen = new HashSet<long>();
    }

    private sealed class Bake
    {
      internal LampController Lamp;
      internal Shape Shape;
      internal Renderer Renderer;
      internal int Slot;
      internal Texture Map;
      internal Vector2 Scale;
      internal Vector2 Offset;
    }

    private static readonly Dictionary<LampController, Shape> Shapes = new Dictionary<LampController, Shape>();
    private static readonly Queue<Bake> Waiting = new Queue<Bake>();
    // What one bake in flight draws with: a target to draw into, its own material, and the buffer its lit points are
    // appended to, zeroed before each draw so the read-back ends at the first empty entry.
    private sealed class Kit
    {
      internal RenderTexture Target;
      internal Material Material;
      internal ComputeBuffer Points;
    }

    private static readonly Stack<Kit> FreeKits = new Stack<Kit>();
    private static readonly CommandBuffer Buffer = new CommandBuffer { name = "COTI glow bake" };
    private static int _inFlight;

    /// <summary>For a new raid: every lamp is shaped again. Bakes still in flight finish into nothing.</summary>
    internal static void Clear()
    {
      Shapes.Clear();
      Waiting.Clear();
    }

    /// <summary>The lamp's shape once found, without asking for one.</summary>
    internal static Shape Known( LampController lamp )
    {
      return Shapes.TryGetValue( lamp, out var shape ) && shape.Ready ? shape : null;
    }

    /// <summary>The lamp's shape once found; null while it is being found, or while its glowing parts are not drawn.</summary>
    internal static Shape Of( LampController lamp )
    {
      if( Shapes.TryGetValue( lamp, out var known ) )
        return known.Ready ? known : null;
      if( CotiShaderBundle.GlowBake == null )
        return null;

      // A part switched off with its object is a variant the lamp is not using (a wall bracket on a pole-mounted
      // light); one only disabled is the game's culling, and the lamp waits until something of it is drawn.
      var parts = Parts( lamp );
      parts.RemoveAll( part => !part.Renderer.gameObject.activeInHierarchy );
      if( !parts.Exists( part => part.Renderer.enabled ) )
        return null;

      var shape = new Shape();
      Shapes[lamp] = shape;
      var glowing = new HashSet<Renderer>();
      foreach( var part in parts )
      {
        glowing.Add( part.Renderer );
        if( !part.Renderer.enabled )
          continue;
        var bake = BakeOf( lamp, shape, part );
        if( bake == null )
          continue;
        shape.Waiting++;
        Waiting.Enqueue( bake );
      }
      foreach( var renderer in lamp.GetComponentsInChildren<Renderer>() )
        if( !glowing.Contains( renderer ) && IsDrawn( renderer ) )
          shape.Housing.Add( renderer );

      if( shape.Waiting == 0 )
        Finish( lamp, shape );
      return shape.Ready ? shape : null;
    }

    /// <summary>Starts waiting bakes, a few at a time. Every frame.</summary>
    internal static void Update()
    {
      while( _inFlight < InFlight && Waiting.Count > 0 )
        Start( Waiting.Dequeue() );
    }

    /// <summary>A lamp's glowing parts, each a renderer and its material slot: EFT's two kinds of lamp material.</summary>
    internal static List<MaterialData> Parts( LampController lamp )
    {
      var parts = new List<MaterialData>();
      foreach( var part in lamp._materialsWithEmission )
        if( part != null && part.Renderer != null )
          parts.Add( part );
      if( lamp._legacyMaterialsWithEmission != null )
        foreach( var part in lamp._legacyMaterialsWithEmission )
          if( part != null && part.Renderer != null )
            parts.Add( part );
      return parts;
    }

    // Drawn in a camera at all: not a particle system, not a shadow caster only.
    private static bool IsDrawn( Renderer renderer )
    {
      return !( renderer is ParticleSystemRenderer ) && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
    }

    private static Bake BakeOf( LampController lamp, Shape shape, MaterialData part )
    {
      var materials = part.Renderer.sharedMaterials;
      if( part.MaterialId < 0 || part.MaterialId >= materials.Length || materials[part.MaterialId] == null )
        return null;
      var material = materials[part.MaterialId];
      var hasMap = material.HasProperty( EmissionMapId );
      var map = hasMap ? material.GetTexture( EmissionMapId ) : null;
      return new Bake
      {
        Lamp = lamp,
        Shape = shape,
        Renderer = part.Renderer,
        Slot = part.MaterialId,
        // No map of its own glows all over, as the heat shader's default reads it.
        Map = map != null ? map : Texture2D.whiteTexture,
        Scale = hasMap ? material.GetTextureScale( EmissionMapId ) : Vector2.one,
        Offset = hasMap ? material.GetTextureOffset( EmissionMapId ) : Vector2.zero,
      };
    }

    private static void Start( Bake bake )
    {
      if( bake.Renderer == null || !IsCurrent( bake ) )
      {
        Done( bake );
        return;
      }

      var kit = FreeKits.Count > 0 ? FreeKits.Pop() : NewKit();
      kit.Material.SetTexture( EmissionMapId, bake.Map );
      kit.Material.SetTextureScale( EmissionMapId, bake.Scale );
      kit.Material.SetTextureOffset( EmissionMapId, bake.Offset );
      kit.Points.SetData( NoPoints );

      Buffer.Clear();
      Buffer.SetRenderTarget( kit.Target );
      Buffer.ClearRenderTarget( false, true, Color.clear );
      // Index 1, as the shader binds it; false starts the buffer's count at 0.
      Buffer.SetRandomWriteTarget( 1, kit.Points, false );
      Buffer.DrawRenderer( bake.Renderer, kit.Material, bake.Slot, 0 );
      Buffer.ClearRandomWriteTargets();
      Graphics.ExecuteCommandBuffer( Buffer );

      _inFlight++;
      AsyncGPUReadback.Request( kit.Points, request => Read( request, bake, kit ) );
    }

    private static Kit NewKit()
    {
      var target = new RenderTexture( Texels, Texels, 0, RenderTextureFormat.ARGB32 ) { name = "CotiGlowBake" };
      target.Create();
      return new Kit
      {
        Target = target,
        Material = new Material( CotiShaderBundle.GlowBake ) { hideFlags = HideFlags.DontUnloadUnusedAsset },
        Points = new ComputeBuffer( MaxPoints, 16, ComputeBufferType.Append ),
      };
    }

    private static void Read( AsyncGPUReadbackRequest request, Bake bake, Kit kit )
    {
      _inFlight--;
      FreeKits.Push( kit );
      if( !request.hasError && IsCurrent( bake ) )
      {
        var shape = bake.Shape;
        var points = request.GetData<Vector4>();
        // Appended from the start, so the first empty entry ends them.
        for( var i = 0; i < points.Length && points[i].w > 0.5f; i++ )
        {
          var t = points[i];
          if( !shape.Seen.Add( PointKey( t ) ) )
            continue;
          shape.Points.Add( t.x );
          shape.Points.Add( t.y );
          shape.Points.Add( t.z );
        }
      }
      Done( bake );
    }

    // One point per 2 cm cube, so a lens of thousands of texels groups in a few hundred.
    private static long PointKey( Vector4 t )
    {
      const long mask = ( 1L << 21 ) - 1;
      long x = Mathf.FloorToInt( t.x / PointMetres ), y = Mathf.FloorToInt( t.y / PointMetres ), z = Mathf.FloorToInt( t.z / PointMetres );
      return ( ( x & mask ) << 42 ) | ( ( y & mask ) << 21 ) | ( z & mask );
    }

    private static bool IsCurrent( Bake bake )
    {
      return bake.Lamp != null && Shapes.TryGetValue( bake.Lamp, out var shape ) && shape == bake.Shape;
    }

    private static void Done( Bake bake )
    {
      if( --bake.Shape.Waiting == 0 && IsCurrent( bake ) )
        Finish( bake.Lamp, bake.Shape );
    }

    private static void Finish( LampController lamp, Shape shape )
    {
      shape.BulbCount = 0;
      foreach( var spot in CotiLampHeat.Spots( shape.Points ) )
        shape.Bulbs[shape.BulbCount++] = new Vector4( spot.X, spot.Y, spot.Z, CotiLampHeat.CubeMetres( spot.Radius ) );
      if( shape.BulbCount == 0 )
        BulbsFromLights( lamp, shape );
      shape.Points.Clear();
      shape.Seen.Clear();
      shape.Ready = true;
    }

    // No glow found: the lamp's light components that sit on the lamp itself, not out in the room it lights.
    private static void BulbsFromLights( LampController lamp, Shape shape )
    {
      var renderers = lamp.GetComponentsInChildren<Renderer>();
      if( renderers.Length == 0 )
        return;
      var bounds = renderers[0].bounds;
      foreach( var r in renderers )
        bounds.Encapsulate( r.bounds );
      bounds.Expand( BoundsMarginMetres * 2f );
      AddLights( lamp.MultiFlareLights, bounds, shape );
      AddLights( lamp.CustomLights, bounds, shape );
      AddLights( lamp.AreaAndTubeLights, bounds, shape );
      AddLights( lamp.Lights, bounds, shape );
    }

    private static void AddLights<T>( T[] sources, Bounds bounds, Shape shape ) where T : Component
    {
      if( sources == null )
        return;
      foreach( var source in sources )
      {
        if( source == null || shape.BulbCount >= shape.Bulbs.Length || !bounds.Contains( source.transform.position ) )
          continue;
        var at = source.transform.position;
        shape.Bulbs[shape.BulbCount++] = new Vector4( at.x, at.y, at.z, CotiLampHeat.LightCubeMetres );
      }
    }
  }
}
