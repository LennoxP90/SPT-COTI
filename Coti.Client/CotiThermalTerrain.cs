using System;
using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// The terrain COTI's thermal cameras draw in place of the real one. EFT's terrain is instanced and its own shader lifts
  /// flat patches by the heightmap, so under the heat-only replacement it would draw flat and heat behind every hill would
  /// show through. Instead one shared grid is lifted by each terrain's own heightmap and holes textures, already on the GPU,
  /// through a command buffer on each thermal camera. The patches are laid out when the set of loaded or shown terrain
  /// slices changes, and each camera records only the patches inside its view, right before it renders. The real terrain's
  /// own material is tagged so the replacement skips it.
  /// </summary>
  internal static class CotiThermalTerrain
  {
    private const int GridVertices = 256;
    private const float PatchMetres = 256f;
    private const float CheckSeconds = 1f;
    // Added on every side of a patch's box, so float error at its faces never drops a patch the camera can see.
    private const float BoxMarginMetres = 1f;

    private static readonly int HeightmapId = Shader.PropertyToID( "_CotiHeightmap" );
    private static readonly int HolesId = Shader.PropertyToID( "_CotiHoles" );
    private static readonly int SizeId = Shader.PropertyToID( "_CotiTerrainSize" );
    private static readonly int PatchId = Shader.PropertyToID( "_CotiPatch" );

    // One grid draw over part of one terrain. Its block holds that terrain's textures and the patch's place in it.
    private struct Patch
    {
      public Terrain Terrain;
      public MaterialPropertyBlock Block;
      public Matrix4x4 Matrix;
    }

    // A thermal camera's buffer: added to the camera once, re-recorded in place when the patches or those in view change.
    private sealed class View
    {
      public Camera Camera;
      public CommandBuffer Buffer;
      public int Version = -1;
      public bool[] Visible = new bool[0];
    }

    private static readonly List<Terrain> Terrains = new List<Terrain>();
    private static readonly List<Patch> Patches = new List<Patch>();
    // Each patch's box, kept apart from Patches so the per-render cull reads only these.
    private static readonly List<Bounds> Boxes = new List<Bounds>();
    // One block per patch, refilled by every layout; grows to the most patches a raid has needed and is reused after that.
    private static readonly List<MaterialPropertyBlock> Blocks = new List<MaterialPropertyBlock>();
    private static readonly List<View> Views = new List<View>();
    private static readonly Plane[] Planes = new Plane[6];
    private static readonly HashSet<Shader> TerrainShaders = new HashSet<Shader>();
    private static int _loaded;
    private static int _shown;
    private static int _version;
    private static float _nextCheck;
    private static bool _subscribed;
    private static bool _broken;
    private static Mesh _grid;
    private static Material _material;

    /// <summary>
    /// Keeps the terrain patches current and gives the camera its buffer. meshMaterials: every material a mesh renderer
    /// draws, which must not be hidden even when it shares a terrain's shader.
    /// </summary>
    internal static void Update( Camera camera, HashSet<Material> meshMaterials )
    {
      var shader = CotiShaderBundle.TerrainGrid;
      if( shader == null )
        return;

      if( _grid == null || _material == null )
      {
        if( _grid == null )
          _grid = Grid();
        if( _material == null )
          _material = new Material( shader );
        _version++;
      }

      if( Find( camera ) == null )
      {
        Add( camera );
        _nextCheck = 0f;
      }

      var now = Time.realtimeSinceStartup;
      if( now < _nextCheck )
        return;
      _nextCheck = now + CheckSeconds;
      Refresh( meshMaterials );
    }

    private static void Refresh( HashSet<Material> meshMaterials )
    {
      Terrains.Clear();
      Terrain.GetActiveTerrains( Terrains );

      // Hiding walks every loaded material, so it runs only when a slice loads or unloads. EFT shows and hides slices as
      // the player crosses its terrain zones, which only changes what is drawn.
      var loaded = Key( shown: false );
      if( loaded != _loaded )
      {
        _loaded = loaded;
        HideTerrain( meshMaterials );
      }

      var shown = Key( shown: true );
      if( shown != _shown )
      {
        _shown = shown;
        LayOut();
      }
    }

    // Changes whenever a terrain slice loads or unloads, and with shown, whenever one is shown or hidden.
    private static int Key( bool shown )
    {
      var key = Terrains.Count;
      foreach( var t in Terrains )
        if( t != null )
          key = key * 31 + t.GetInstanceID() * ( !shown || t.drawHeightmap ? 1 : 2 );
      return key;
    }

    private static void LayOut()
    {
      Patches.Clear();
      Boxes.Clear();
      _version++;
      foreach( var terrain in Terrains )
      {
        var data = terrain != null && terrain.drawHeightmap ? terrain.terrainData : null;
        var heightmap = data != null ? data.heightmapTexture : null;
        if( heightmap == null )
          continue;

        var holes = data.holesTexture;
        var size = data.size;
        var sizeAndResolution = new Vector4( size.x, size.y, size.z, data.heightmapResolution );
        var nx = Mathf.Max( 1, Mathf.CeilToInt( size.x / PatchMetres ) );
        var nz = Mathf.Max( 1, Mathf.CeilToInt( size.z / PatchMetres ) );
        var origin = terrain.GetPosition();
        var matrix = Matrix4x4.Translate( origin );
        for( var i = 0; i < nx; i++ )
          for( var j = 0; j < nz; j++ )
          {
            var block = BlockFor( Patches.Count );
            block.SetTexture( HeightmapId, heightmap );
            block.SetTexture( HolesId, holes != null ? (Texture)holes : Texture2D.whiteTexture );
            block.SetVector( SizeId, sizeAndResolution );
            block.SetVector( PatchId, new Vector4( (float)i / nx, (float)j / nz, 1f / nx, 1f / nz ) );
            Patches.Add( new Patch { Terrain = terrain, Block = block, Matrix = matrix } );
            Boxes.Add( Box( origin, size, (float)i / nx, (float)j / nz, (float)( i + 1 ) / nx, (float)( j + 1 ) / nz ) );
          }
      }

      if( Plugin.Config != null && Plugin.Config.VerboseLogging )
        Plugin.Log.LogInfo( $"Thermal: terrain drawn from {Terrains.Count} heightmap(s) in {Patches.Count} patch(es)" );
    }

    private static MaterialPropertyBlock BlockFor( int index )
    {
      if( index == Blocks.Count )
        Blocks.Add( new MaterialPropertyBlock() );
      return Blocks[index];
    }

    // The patch's ground rectangle and the terrain's whole height range: the highest the shader can lift it is size.y.
    private static Bounds Box( Vector3 origin, Vector3 size, float u0, float v0, float u1, float v1 )
    {
      var box = new Bounds();
      box.SetMinMax( origin + new Vector3( u0 * size.x, 0f, v0 * size.z ), origin + new Vector3( u1 * size.x, size.y, v1 * size.z ) );
      box.Expand( 2f * BoxMarginMetres );
      return box;
    }

    // The view for camera, if it is a thermal camera. Views whose camera has been destroyed are dropped on the way.
    private static View Find( Camera camera )
    {
      View found = null;
      for( var i = Views.Count - 1; i >= 0; i-- )
      {
        var view = Views[i];
        if( view.Camera == null )
        {
          view.Buffer.Release();
          Views.RemoveAt( i );
          if( Views.Count == 0 )
            Forget();
        }
        // ==, not ReferenceEquals: on il2cpp each callback hands over a fresh wrapper for the native camera.
        else if( view.Camera == camera )
          found = view;
      }
      return found;
    }

    private static void Add( Camera camera )
    {
      var view = new View { Camera = camera, Buffer = new CommandBuffer { name = "COTI thermal terrain" } };
      camera.AddCommandBuffer( CameraEvent.BeforeForwardOpaque, view.Buffer );
      Views.Add( view );

      if( !_subscribed )
      {
        Camera.onPreRender += OnPreRender;
        _subscribed = true;
      }
    }

    // No thermal camera is left. Lets go of the slices' terrains and textures, and makes the next camera lay them out again.
    private static void Forget()
    {
      foreach( var block in Blocks )
        block.Clear();
      Patches.Clear();
      Boxes.Clear();
      Terrains.Clear();
      _shown = 0;
      _broken = false;
    }

    // Every camera's pre-render, after its pose, field of view and crop are final for the frame.
    private static void OnPreRender( Camera camera )
    {
      var view = Find( camera );
      if( view == null || _broken )
        return;

      try
      {
        Cull( camera, view );
      }
      catch( Exception ex )
      {
        // Latched: this runs for every camera every frame, and an unguarded throw at that rate floods the log.
        _broken = true;
        Plugin.Log.LogError( $"Thermal: terrain culling stopped; its buffers keep their last patches until thermal is switched off and on. {ex}" );
      }
    }

    // A patch wholly outside the camera's view costs all its vertices and a draw call and fills no pixel.
    private static void Cull( Camera camera, View view )
    {
      GeometryUtility.CalculateFrustumPlanes( camera, Planes );

      var count = Boxes.Count;
      var changed = view.Version != _version;
      if( view.Visible.Length < count )
        view.Visible = new bool[count];
      for( var i = 0; i < count; i++ )
      {
        var visible = GeometryUtility.TestPlanesAABB( Planes, Boxes[i] );
        changed |= visible != view.Visible[i];
        view.Visible[i] = visible;
      }

      if( !changed )
        return;
      view.Version = _version;
      Record( view.Buffer, view.Visible, count );
    }

    private static void Record( CommandBuffer buffer, bool[] visible, int count )
    {
      buffer.Clear();
      for( var i = 0; i < count; i++ )
      {
        if( !visible[i] )
          continue;
        var patch = Patches[i];
        // Its slice unloaded since the last check; the next check lays the patches out without it.
        if( patch.Terrain == null )
          continue;
        buffer.DrawMesh( _grid, patch.Matrix, _material, 0, 0, patch.Block );
      }
    }

    /// <summary>
    /// The real terrain is drawn with Unity's own copy of its material, which no mesh uses: tagged so the heat-only
    /// replacement skips it. A material a mesh draws keeps its tag, even on a terrain's shader, so the mesh still hides
    /// what is behind it.
    /// </summary>
    private static void HideTerrain( HashSet<Material> meshMaterials )
    {
      TerrainShaders.Clear();
      foreach( var terrain in Terrains )
      {
        var template = terrain != null ? terrain.materialTemplate : null;
        var shader = template != null ? template.shader : null;
        if( shader != null )
          TerrainShaders.Add( shader );
      }
      if( TerrainShaders.Count == 0 )
        return;

      foreach( var material in Resources.FindObjectsOfTypeAll<Material>() )
        if( material != null && TerrainShaders.Contains( material.shader ) && !meshMaterials.Contains( material ) )
          material.SetOverrideTag( "RenderType", CotiRenderTypeTag.DrawnByCoti );
    }

    // A flat GridVertices x GridVertices grid over 0..1 in x and z, lifted in the shader; 16-bit indices.
    private static Mesh Grid()
    {
      var n = GridVertices;
      var vertices = new Vector3[n * n];
      for( var z = 0; z < n; z++ )
        for( var x = 0; x < n; x++ )
          vertices[z * n + x] = new Vector3( x / (float)( n - 1 ), 0f, z / (float)( n - 1 ) );
      var indices = new int[( n - 1 ) * ( n - 1 ) * 6];
      var k = 0;
      for( var z = 0; z < n - 1; z++ )
        for( var x = 0; x < n - 1; x++ )
        {
          var a = z * n + x;
          indices[k++] = a;
          indices[k++] = a + n;
          indices[k++] = a + 1;
          indices[k++] = a + 1;
          indices[k++] = a + n;
          indices[k++] = a + n + 1;
        }
      var mesh = new Mesh { name = "COTI thermal terrain grid", indexFormat = IndexFormat.UInt16, vertices = vertices, triangles = indices };
      mesh.bounds = new Bounds( new Vector3( 0.5f, 0f, 0.5f ), new Vector3( 1f, 100000f, 1f ) );
      mesh.UploadMeshData( true );
      return mesh;
    }
  }
}
