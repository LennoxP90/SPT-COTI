// The wire format for one device. Must stay dependency-free - no SPTarkov type of any kind -
// because Coti.Tests source-links this file with no SPT reference.
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Coti.Shared;

namespace Coti.Server;

public class CotiHostTableDto
{
  [JsonPropertyName( "devices" )]
  public List<CotiDeviceDto> Devices { get; set; } = new();
}

/// <summary>
/// Response body for POST /coti/hosts/publish. Lives here rather than beside the route because
/// this file carries no SPTarkov reference, which is what lets Coti.Tests source-link it with no
/// server assembly present.
/// </summary>
public class CotiPublishResultDto
{
  [JsonPropertyName( "ok" )]
  public bool Ok { get; set; }

  [JsonPropertyName( "error" )]
  public string? Error { get; set; }

  [JsonPropertyName( "device" )]
  public CotiDeviceDto? Device { get; set; }

  /// <summary>
  /// One "&lt;hostId&gt;: &lt;outcome&gt;" entry per host InjectInto could not fit - only
  /// InvalidId (a malformed id in the payload) and NoSlotsCollection (a target item with a broken
  /// Slots collection) ever land here; NotInstalled is the normal case for a host the publishing
  /// player does not own, and AlreadyPresent is a silent no-op, so neither is a failure worth
  /// reporting. Populated even when Ok is true: the device file was written successfully - that is
  /// what Ok means - but the pose editor still needs to know a host it declared did not end up
  /// fitted, rather than being told the publish fully succeeded when only the write half did.
  /// </summary>
  [JsonPropertyName( "unfitHosts" )]
  public List<string> UnfitHosts { get; set; } = new();
}

public class CotiDeviceDto
{
  [JsonPropertyName( "schema" )]
  public int Schema { get; set; }

  [JsonPropertyName( "device" )]
  public string? Device { get; set; }

  [JsonPropertyName( "displayName" )]
  public string? DisplayName { get; set; }

  [JsonPropertyName( "requires" )]
  public string? Requires { get; set; }

  [JsonPropertyName( "tuned" )]
  public bool Tuned { get; set; }

  [JsonPropertyName( "hosts" )]
  public List<CotiHostRefDto> Hosts { get; set; } = new();

  [JsonPropertyName( "mask" )]
  public CotiMaskBlockDto Mask { get; set; } = new();

  [JsonPropertyName( "mount" )]
  public CotiMountBlockDto Mount { get; set; } = new();

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

    foreach( var host in source.Hosts )
      dto.Hosts.Add( CotiHostRefDto.FromShared( host ) );

    return dto;
  }

  /// <summary>
  /// Null-safe per member: nullable annotations are compile-time only, so an explicit "mask": null
  /// binds over the initialiser. Substituting a default here means the caller can name which
  /// member was wrong.
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

    if( Hosts == null )
      return shared;

    foreach( var host in Hosts )
    {
      if( host != null )
        shared.Hosts.Add( host.ToShared() );
    }

    return shared;
  }
}

public class CotiHostRefDto
{
  [JsonPropertyName( "id" )]
  public string? Id { get; set; }

  [JsonPropertyName( "prefab" )]
  public string? Prefab { get; set; }

  [JsonPropertyName( "label" )]
  public string? Label { get; set; }

  public static CotiHostRefDto FromShared( CotiHostRef source ) => new()
  {
    Id = source.Id,
    Prefab = source.Prefab,
    Label = source.Label,
  };

  public CotiHostRef ToShared() => new()
  {
    Id = Id,
    Prefab = Prefab,
    Label = Label,
  };
}

public class CotiMaskBlockDto
{
  [JsonPropertyName( "centerX" )]
  public float CenterX { get; set; }

  [JsonPropertyName( "centerY" )]
  public float CenterY { get; set; }

  [JsonPropertyName( "radius" )]
  public float Radius { get; set; }

  [JsonPropertyName( "feather" )]
  public float Feather { get; set; }

  public static CotiMaskBlockDto FromShared( CotiMaskBlock source ) => new()
  {
    CenterX = source.CenterX,
    CenterY = source.CenterY,
    Radius = source.Radius,
    Feather = source.Feather,
  };

  public CotiMaskBlock ToShared() => new()
  {
    CenterX = CenterX,
    CenterY = CenterY,
    Radius = Radius,
    Feather = Feather,
  };
}

public class CotiMountBlockDto
{
  [JsonPropertyName( "anchorBone" )]
  public string? AnchorBone { get; set; }

  [JsonPropertyName( "positionX" )]
  public float PositionX { get; set; }

  [JsonPropertyName( "positionY" )]
  public float PositionY { get; set; }

  [JsonPropertyName( "positionZ" )]
  public float PositionZ { get; set; }

  [JsonPropertyName( "rotationX" )]
  public float RotationX { get; set; }

  [JsonPropertyName( "rotationY" )]
  public float RotationY { get; set; }

  [JsonPropertyName( "rotationZ" )]
  public float RotationZ { get; set; }

  [JsonPropertyName( "rollDegrees" )]
  public float RollDegrees { get; set; }

  [JsonPropertyName( "pitchDegrees" )]
  public float PitchDegrees { get; set; }

  [JsonPropertyName( "yawDegrees" )]
  public float YawDegrees { get; set; }

  [JsonPropertyName( "scale" )]
  public float Scale { get; set; } = 1f;

  public static CotiMountBlockDto FromShared( CotiMountBlock source ) => new()
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

  public CotiMountBlock ToShared() => new()
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
