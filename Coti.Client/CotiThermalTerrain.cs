using System.Collections.Generic;
using Coti.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// The terrain COTI's thermal camera draws in place of the real one. EFT's terrain is instanced: Unity sends flat grid
  /// patches and the terrain's own shader lifts them by the heightmap, so under the heat-only replacement every hill came
  /// out flat and heat behind it showed through. Instead, one shared grid is lifted by each terrain's own heightmap and
  /// holes textures, already on the GPU, through a command buffer on the thermal camera, built once and rebuilt only when
  /// the set of loaded terrain slices changes. The real terrain's own material is tagged so the replacement skips it.
  /// </summary>
  internal static class CotiThermalTerrain
  {
    private const int GridVertices = 256;
    private const float PatchMetres = 256f;
    private const float CheckSeconds = 1f;

    private static readonly int HeightmapId = Shader.PropertyToID( "_CotiHeightmap" );
    private static readonly int HolesId = Shader.PropertyToID( "_CotiHoles" );
    private static readonly int SizeId = Shader.PropertyToID( "_CotiTerrainSize" );
    private static readonly int PatchId = Shader.PropertyToID( "_CotiPatch" );

    private static readonly Dictionary<Camera, CommandBuffer> Buffers = new Dictionary<Camera, CommandBuffer>();
    private static readonly Dictionary<Camera, int> Drawn = new Dictionary<Camera, int>();
    private static int _hidden;
    private static Mesh _grid;
    private static Material _material;
    private static float _nextCheck;

    /// <summary>
    /// Keeps the camera's terrain buffer current. meshMaterials: every material a mesh renderer draws, which must not be
    /// hidden even when it shares a terrain's shader.
    /// </summary>
    internal static void Update( Camera camera, HashSet<Material> meshMaterials )
    {
      var shader = CotiShaderBundle.TerrainGrid;
      if( shader == null )
        return;

      var now = Time.realtimeSinceStartup;
      var known = Drawn.TryGetValue( camera, out var drawn );
      if( known && now < _nextCheck )
        return;
      _nextCheck = now + CheckSeconds;

      var terrains = Terrain.activeTerrains;
      var key = Key( terrains, shown: true );
      if( known && key == drawn )
        return;
      Drawn[camera] = key;

      // Hiding walks every loaded material, so only for slices that load or unload: EFT shows and hides slices as the
      // player crosses its terrain zones, which only changes what the buffer draws.
      var hidden = Key( terrains, shown: false );
      if( hidden != _hidden )
      {
        _hidden = hidden;
        HideTerrain( terrains, meshMaterials );
      }
      Build( camera, terrains, shader );
    }

    // Changes whenever a terrain slice loads or unloads, and with shown, whenever one is shown or hidden.
    private static int Key( Terrain[] terrains, bool shown )
    {
      var key = terrains.Length;
      foreach( var t in terrains )
        if( t != null )
          key = key * 31 + t.GetInstanceID() * ( !shown || t.drawHeightmap ? 1 : 2 );
      return key;
    }

    private static void Build( Camera camera, Terrain[] terrains, Shader shader )
    {
      if( _grid == null )
        _grid = Grid();
      if( _material == null )
        _material = new Material( shader );

      if( Buffers.TryGetValue( camera, out var old ) && old != null )
        camera.RemoveCommandBuffer( CameraEvent.BeforeForwardOpaque, old );

      var buffer = new CommandBuffer { name = "COTI thermal terrain" };
      var patches = 0;
      foreach( var terrain in terrains )
      {
        var data = terrain != null ? terrain.terrainData : null;
        if( data == null || !terrain.drawHeightmap || data.heightmapTexture == null )
          continue;

        var size = data.size;
        var nx = Mathf.Max( 1, Mathf.CeilToInt( size.x / PatchMetres ) );
        var nz = Mathf.Max( 1, Mathf.CeilToInt( size.z / PatchMetres ) );
        var matrix = Matrix4x4.Translate( terrain.GetPosition() );
        for( var i = 0; i < nx; i++ )
          for( var j = 0; j < nz; j++ )
          {
            var block = new MaterialPropertyBlock();
            block.SetTexture( HeightmapId, data.heightmapTexture );
            block.SetTexture( HolesId, data.holesTexture != null ? (Texture)data.holesTexture : Texture2D.whiteTexture );
            block.SetVector( SizeId, new Vector4( size.x, size.y, size.z, data.heightmapResolution ) );
            block.SetVector( PatchId, new Vector4( (float)i / nx, (float)j / nz, 1f / nx, 1f / nz ) );
            buffer.DrawMesh( _grid, matrix, _material, 0, 0, block );
            patches++;
          }
      }

      camera.AddCommandBuffer( CameraEvent.BeforeForwardOpaque, buffer );
      Buffers[camera] = buffer;
      Plugin.Log.LogInfo( $"Thermal: terrain drawn from {terrains.Length} heightmap(s) in {patches} patch(es)" );
    }

    /// <summary>
    /// The real terrain is drawn with Unity's own copy of its material, which no mesh uses: tagged so the heat-only
    /// replacement skips it. A material a mesh draws keeps its tag, even on a terrain's shader, so the mesh still hides
    /// what is behind it.
    /// </summary>
    private static void HideTerrain( Terrain[] terrains, HashSet<Material> meshMaterials )
    {
      var shaders = new HashSet<Shader>();
      foreach( var terrain in terrains )
        if( terrain != null && terrain.materialTemplate != null && terrain.materialTemplate.shader != null )
          shaders.Add( terrain.materialTemplate.shader );
      if( shaders.Count == 0 )
        return;
      foreach( var material in Resources.FindObjectsOfTypeAll<Material>() )
        if( material != null && shaders.Contains( material.shader ) && !meshMaterials.Contains( material ) )
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
