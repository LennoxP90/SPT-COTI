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
        Mount = CotiMountBlockDto.FromShared( source.Mount ),
      };

      foreach ( var host in source.Hosts )
        dto.Hosts.Add( CotiHostRefDto.FromShared( host ) );

      return dto;
    }

    /// <summary>
    /// Null-safe on every member, for the same reason the server half is (see its own comment):
    /// Newtonsoft assigns null over a "= new()" initialiser for an explicit null just as
    /// System.Text.Json does, and this side parses two payloads it does not author - the server's
    /// /coti/hosts response and the publish result. Keeping the two halves of one wire contract
    /// null-safe together is the point; the original defect was exactly that knowledge on one side
    /// of the boundary not crossing it.
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

    public static CotiMountBlockDto FromShared( CotiMountBlock source )
    {
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
}
