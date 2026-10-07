// The client half of the wire contract in Coti.Server/CotiDeviceDto.cs. Must stay dependency-free
// - no Unity, no BepInEx - because Coti.Tests source-links this file.
using System.Collections.Generic;
using Coti.Shared;
using Newtonsoft.Json;

namespace Coti.Client
{
  public class CotiHostTableDto
  {
    [JsonProperty( "devices" )]
    public List<CotiDeviceDto> Devices { get; set; } = new List<CotiDeviceDto>();
  }

  /// <summary>
  /// The response body for POST /coti/hosts/publish.
  /// </summary>
  public class CotiPublishResultDto
  {
    [JsonProperty( "ok" )]
    public bool Ok { get; set; }

    [JsonProperty( "error" )]
    public string? Error { get; set; }

    [JsonProperty( "device" )]
    public CotiDeviceDto? Device { get; set; }

    [JsonProperty( "unfitHosts" )]
    public List<string> UnfitHosts { get; set; } = new List<string>();
  }

  public class CotiDeviceDto
  {
    [JsonProperty( "schema" )]
    public int Schema { get; set; }

    [JsonProperty( "device" )]
    public string? Device { get; set; }

    [JsonProperty( "displayName" )]
    public string? DisplayName { get; set; }

    [JsonProperty( "requires" )]
    public string? Requires { get; set; }

    [JsonProperty( "tuned" )]
    public bool Tuned { get; set; }

    [JsonProperty( "hosts" )]
    public List<CotiHostRefDto> Hosts { get; set; } = new List<CotiHostRefDto>();

    [JsonProperty( "mask" )]
    public CotiMaskBlockDto Mask { get; set; } = new CotiMaskBlockDto();

    [JsonProperty( "mount" )]
    public CotiMountBlockDto Mount { get; set; } = new CotiMountBlockDto();

    // Omitted when null, so a v1 device carries no multi-tube keys on the publish route.
    [JsonProperty( "layout", NullValueHandling = NullValueHandling.Ignore )]
    public string? Layout { get; set; }

    [JsonProperty( "tubes", NullValueHandling = NullValueHandling.Ignore )]
    public Dictionary<string, CotiTubeDto?>? Tubes { get; set; }

    [JsonProperty( "text", NullValueHandling = NullValueHandling.Ignore )]
    public CotiTextBlockDto? Text { get; set; }

    public static CotiDeviceDto FromShared( CotiDeviceFile source )
    {
      var dto = new CotiDeviceDto
      {
        Schema = source.Schema,
        Device = source.Device,
        DisplayName = source.DisplayName,
        Requires = source.Requires,
        Tuned = source.Tuned,
        Mask = CotiMaskBlockDto.FromShared( source.Mask ),
        Mount = CotiMountBlockDto.FromShared( source.Mount ) ?? new CotiMountBlockDto(),
        Layout = source.Layout,
        Tubes = CotiTubeDto.FromShared( source.Tubes ),
        Text = CotiTextBlockDto.FromShared( source.Text ),
      };

      foreach ( var host in source.Hosts )
        dto.Hosts.Add( CotiHostRefDto.FromShared( host ) );

      return dto;
    }

    /// <summary>
    /// Null-safe on every member, like the server half: Newtonsoft assigns null over a "= new()"
    /// initialiser for an explicit null just as System.Text.Json does, and this side parses two
    /// payloads it does not author - the server's /coti/hosts response and the publish result.
    /// </summary>
    public CotiDeviceFile ToShared()
    {
      var shared = new CotiDeviceFile
      {
        Schema = Schema,
        Device = Device,
        DisplayName = DisplayName,
        Requires = Requires,
        Tuned = Tuned,
        Mask = Mask?.ToShared() ?? new CotiMaskBlock(),
        Mount = Mount?.ToShared() ?? new CotiMountBlock(),
        Layout = Layout,
        Tubes = CotiTubeDto.ToShared( Tubes ),
        Text = Text?.ToShared(),
      };

      if ( Hosts == null )
        return shared;

      foreach ( var host in Hosts )
      {
        if ( host != null )
          shared.Hosts.Add( host.ToShared() );
      }

      return shared;
    }
  }

  public class CotiHostRefDto
  {
    [JsonProperty( "id" )]
    public string? Id { get; set; }

    [JsonProperty( "prefab" )]
    public string? Prefab { get; set; }

    [JsonProperty( "label" )]
    public string? Label { get; set; }

    public static CotiHostRefDto FromShared( CotiHostRef source )
    {
      return new CotiHostRefDto { Id = source.Id, Prefab = source.Prefab, Label = source.Label };
    }

    public CotiHostRef ToShared()
    {
      return new CotiHostRef { Id = Id, Prefab = Prefab, Label = Label };
    }
  }

  public class CotiMaskBlockDto
  {
    [JsonProperty( "centerX" )]
    public float CenterX { get; set; }

    [JsonProperty( "centerY" )]
    public float CenterY { get; set; }

    [JsonProperty( "radius" )]
    public float Radius { get; set; }

    [JsonProperty( "feather" )]
    public float Feather { get; set; }

    public static CotiMaskBlockDto FromShared( CotiMaskBlock source )
    {
      return new CotiMaskBlockDto
      {
        CenterX = source.CenterX,
        CenterY = source.CenterY,
        Radius = source.Radius,
        Feather = source.Feather,
      };
    }

    public CotiMaskBlock ToShared()
    {
      return new CotiMaskBlock { CenterX = CenterX, CenterY = CenterY, Radius = Radius, Feather = Feather };
    }
  }

  public class CotiMountBlockDto
  {
    [JsonProperty( "anchorBone" )]
    public string? AnchorBone { get; set; }

    [JsonProperty( "positionX" )]
    public float PositionX { get; set; }

    [JsonProperty( "positionY" )]
    public float PositionY { get; set; }

    [JsonProperty( "positionZ" )]
    public float PositionZ { get; set; }

    [JsonProperty( "rotationX" )]
    public float RotationX { get; set; }

    [JsonProperty( "rotationY" )]
    public float RotationY { get; set; }

    [JsonProperty( "rotationZ" )]
    public float RotationZ { get; set; }

    [JsonProperty( "rollDegrees" )]
    public float RollDegrees { get; set; }

    [JsonProperty( "pitchDegrees" )]
    public float PitchDegrees { get; set; }

    [JsonProperty( "yawDegrees" )]
    public float YawDegrees { get; set; }

    [JsonProperty( "scale" )]
    public float Scale { get; set; } = 1f;

    public static CotiMountBlockDto? FromShared( CotiMountBlock? source )
    {
      if ( source == null )
        return null;

      return new CotiMountBlockDto
      {
        AnchorBone = source.AnchorBone,
        PositionX = source.PositionX,
        PositionY = source.PositionY,
        PositionZ = source.PositionZ,
        RotationX = source.RotationX,
        RotationY = source.RotationY,
        RotationZ = source.RotationZ,
        RollDegrees = source.RollDegrees,
        PitchDegrees = source.PitchDegrees,
        YawDegrees = source.YawDegrees,
        Scale = source.Scale,
      };
    }

    public CotiMountBlock ToShared()
    {
      return new CotiMountBlock
      {
        AnchorBone = AnchorBone,
        PositionX = PositionX,
        PositionY = PositionY,
        PositionZ = PositionZ,
        RotationX = RotationX,
        RotationY = RotationY,
        RotationZ = RotationZ,
        RollDegrees = RollDegrees,
        PitchDegrees = PitchDegrees,
        YawDegrees = YawDegrees,
        Scale = Scale,
      };
    }
  }

  public class CotiTubeDto
  {
    [JsonProperty( "mount" )]
    public CotiMountBlockDto? Mount { get; set; }

    [JsonProperty( "pod" )]
    public CotiPodBlockDto? Pod { get; set; }

    [JsonProperty( "text", NullValueHandling = NullValueHandling.Ignore )]
    public CotiTextBlockDto? Text { get; set; }

    public static Dictionary<string, CotiTubeDto?>? FromShared( Dictionary<string, CotiTube>? tubes )
    {
      if ( tubes == null )
        return null;

      var dtos = new Dictionary<string, CotiTubeDto?>();
      foreach ( var kv in tubes )
      {
        dtos[kv.Key] = kv.Value == null ? null : new CotiTubeDto
        {
          Mount = CotiMountBlockDto.FromShared( kv.Value.Mount ),
          Pod = kv.Value.Pod == null ? null : CotiPodBlockDto.FromShared( kv.Value.Pod ),
          Text = CotiTextBlockDto.FromShared( kv.Value.Text ),
        };
      }

      return dtos;
    }

    /// <summary>
    /// A null tube or mount stays null, as on the server half: CotiTubeValidation drops it and names it.
    /// </summary>
    public static Dictionary<string, CotiTube>? ToShared( Dictionary<string, CotiTubeDto?>? tubes )
    {
      if ( tubes == null )
        return null;

      var shared = new Dictionary<string, CotiTube>();
      foreach ( var kv in tubes )
      {
        shared[kv.Key] = kv.Value == null
            ? null!
            : new CotiTube { Mount = kv.Value.Mount?.ToShared()!, Pod = kv.Value.Pod?.ToShared(), Text = kv.Value.Text?.ToShared() };
      }

      return shared;
    }
  }

  public class CotiPodBlockDto
  {
    [JsonProperty( "bone" )]
    public string? Bone { get; set; }

    [JsonProperty( "downX" )]
    public float DownX { get; set; }

    [JsonProperty( "downY" )]
    public float DownY { get; set; }

    [JsonProperty( "downZ" )]
    public float DownZ { get; set; }

    public static CotiPodBlockDto FromShared( CotiPodBlock source )
    {
      return new CotiPodBlockDto { Bone = source.Bone, DownX = source.DownX, DownY = source.DownY, DownZ = source.DownZ };
    }

    public CotiPodBlock ToShared()
    {
      return new CotiPodBlock { Bone = Bone, DownX = DownX, DownY = DownY, DownZ = DownZ };
    }
  }

  public class CotiTextBlockDto
  {
    [JsonProperty( "align", NullValueHandling = NullValueHandling.Ignore )]
    public string? Align { get; set; }

    [JsonProperty( "edge", NullValueHandling = NullValueHandling.Ignore )]
    public float? Edge { get; set; }

    [JsonProperty( "y", NullValueHandling = NullValueHandling.Ignore )]
    public float? Y { get; set; }

    public static CotiTextBlockDto? FromShared( CotiTextBlock? source )
    {
      return source == null ? null : new CotiTextBlockDto { Align = source.Align, Edge = source.Edge, Y = source.Y };
    }

    public CotiTextBlock ToShared()
    {
      return new CotiTextBlock { Align = Align, Edge = Edge, Y = Y };
    }
  }
}
