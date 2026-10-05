using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Coti.Shared
{
  /// <summary>
  /// How a lit lamp's heat lies over its parts, for the lamps listed in lamps.json (every other lamp stays cold): the
  /// glow (what the lamp's own materials light) hot; around each glowing spot a cube a few times its size, where the
  /// lamp's other parts are warm; beyond it, its pole, stand and mounts cold. The glowing spots are found from the glow
  /// itself, once per lamp, because a lamp's light components are placed to light the room (a metre below a ceiling
  /// panel, metres ahead of a searchlight, between a tripod light's two heads), not at its bulbs.
  /// </summary>
  public static class CotiLampHeat
  {
    /// <summary>The most glowing spots one lamp's parts carry into the shader.</summary>
    public const int MaxBulbs = 8;

    /// <summary>Glowing points closer than this belong to one spot: a tube's length joins, two heads do not.</summary>
    public const float SameSpotMetres = 0.15f;

    /// <summary>A spot's cube against its own size: the housing a bulb or lens sits in is a few times larger.</summary>
    public const float CubeScale = 2.5f;

    public const float SmallestCubeMetres = 0.1f;
    public const float LargestCubeMetres = 1f;

    /// <summary>A cube around a light component, for a lamp whose glow could not be found.</summary>
    public const float LightCubeMetres = 0.25f;

    /// <summary>How warm the housing gets at a bulb, against the glow itself.</summary>
    public const float HousingWarmth = 0.6f;

    /// <summary>
    /// A lamp's type, as lamps.json names it: its object's name without "(Clone)" or a
    /// copy number, so every lamp of one kind on a map ("Streetlight_lamp_06_A_source_on (12)") is the one type. Digits
    /// that are part of the name ("Searchlight_03_source_on", "lamp03_destroyable") stay.
    /// </summary>
    public static string TypeOf( string lampName )
    {
      if( string.IsNullOrEmpty( lampName ) )
        return "";
      return Regex.Replace( lampName.Replace( "(Clone)", "" ), @"(\s*\(\d+\))+\s*$", "" ).Trim();
    }

    /// <summary>A spot's cube half-width for its size (the farthest glowing point from its middle), within bounds.</summary>
    public static float CubeMetres( float spotRadius )
    {
      return Math.Max( SmallestCubeMetres, Math.Min( LargestCubeMetres, spotRadius * CubeScale ) );
    }

    public struct Spot
    {
      public float X, Y, Z;

      /// <summary>The farthest glowing point from the middle.</summary>
      public float Radius;

      public int Points;
    }

    /// <summary>
    /// Glowing points (x, y, z in turn) grouped into spots: points within SameSpotMetres of each other, directly or
    /// through others, are one. The largest spots first, at most MaxBulbs.
    /// </summary>
    public static List<Spot> Spots( IList<float> xyz )
    {
      var count = xyz.Count / 3;
      var parent = new int[count];
      for( var i = 0; i < count; i++ )
        parent[i] = i;

      // Cells one link wide, so a point's neighbours within the link are in its own cell or the 26 around it.
      var cells = new Dictionary<long, List<int>>();
      for( var i = 0; i < count; i++ )
      {
        var key = Cell( xyz, i );
        if( !cells.TryGetValue( key, out var list ) )
          cells[key] = list = new List<int>();
        list.Add( i );
      }

      var link = SameSpotMetres * SameSpotMetres;
      for( var i = 0; i < count; i++ )
      {
        Coordinates( xyz, i, out var cx, out var cy, out var cz );
        for( var dx = -1; dx <= 1; dx++ )
          for( var dy = -1; dy <= 1; dy++ )
            for( var dz = -1; dz <= 1; dz++ )
            {
              if( !cells.TryGetValue( Key( cx + dx, cy + dy, cz + dz ), out var near ) )
                continue;
              foreach( var j in near )
                if( j > i && Square( xyz, i, j ) < link )
                  Union( parent, i, j );
            }
      }

      var groups = new Dictionary<int, List<int>>();
      for( var i = 0; i < count; i++ )
      {
        var root = Find( parent, i );
        if( !groups.TryGetValue( root, out var members ) )
          groups[root] = members = new List<int>();
        members.Add( i );
      }

      var spots = new List<Spot>();
      foreach( var members in groups.Values )
      {
        double x = 0, y = 0, z = 0;
        foreach( var m in members )
        {
          x += xyz[m * 3];
          y += xyz[m * 3 + 1];
          z += xyz[m * 3 + 2];
        }
        var spot = new Spot { X = (float)( x / members.Count ), Y = (float)( y / members.Count ), Z = (float)( z / members.Count ), Points = members.Count };
        foreach( var m in members )
        {
          float ex = xyz[m * 3] - spot.X, ey = xyz[m * 3 + 1] - spot.Y, ez = xyz[m * 3 + 2] - spot.Z;
          spot.Radius = Math.Max( spot.Radius, (float)Math.Sqrt( ex * ex + ey * ey + ez * ez ) );
        }
        spots.Add( spot );
      }
      spots.Sort( ( a, b ) => b.Points.CompareTo( a.Points ) );
      if( spots.Count > MaxBulbs )
        spots.RemoveRange( MaxBulbs, spots.Count - MaxBulbs );
      return spots;
    }

    private static void Coordinates( IList<float> xyz, int i, out int x, out int y, out int z )
    {
      x = (int)Math.Floor( xyz[i * 3] / SameSpotMetres );
      y = (int)Math.Floor( xyz[i * 3 + 1] / SameSpotMetres );
      z = (int)Math.Floor( xyz[i * 3 + 2] / SameSpotMetres );
    }

    private static long Cell( IList<float> xyz, int i )
    {
      Coordinates( xyz, i, out var x, out var y, out var z );
      return Key( x, y, z );
    }

    // 21 bits a coordinate: cells 15 cm wide reach 157 km either way, past any map.
    private static long Key( int x, int y, int z )
    {
      const long mask = ( 1L << 21 ) - 1;
      return ( ( x & mask ) << 42 ) | ( ( y & mask ) << 21 ) | ( z & mask );
    }

    private static float Square( IList<float> xyz, int i, int j )
    {
      float dx = xyz[i * 3] - xyz[j * 3], dy = xyz[i * 3 + 1] - xyz[j * 3 + 1], dz = xyz[i * 3 + 2] - xyz[j * 3 + 2];
      return dx * dx + dy * dy + dz * dz;
    }

    private static int Find( int[] parent, int i )
    {
      while( parent[i] != i )
        i = parent[i] = parent[parent[i]];
      return i;
    }

    private static void Union( int[] parent, int a, int b )
    {
      a = Find( parent, a );
      b = Find( parent, b );
      if( a != b )
        parent[b] = a;
    }
  }
}
