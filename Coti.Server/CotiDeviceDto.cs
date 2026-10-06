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
  /// One "&lt;hostId&gt;: &lt;outcome&gt;" entry per host InjectInto could not fit. Only
  /// InvalidId (a malformed id in the payload) and NoSlotsCollection (a target item with a broken
  /// Slots collection) land here; NotInstalled is normal for a host the publishing player does not
  /// own, and AlreadyPresent is a no-op. Populated even when Ok is true: Ok means the device file
  /// was written, and the pose editor still needs to know a declared host was not fitted.
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

  // Omitted when null, so a v1 device keeps 3.2.0's shape whatever options the writer uses.
  [JsonPropertyName( "layout" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public string? Layout { get; set; }

  [JsonPropertyName( "tubes" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public Dictionary<string, CotiTubeDto?>? Tubes { get; set; }

  [JsonPropertyName( "text" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
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
      Mount = CotiMountBlockDto.FromShared( source.Mount ),
      Layout = source.Layout,
      Tubes = CotiTubeDto.FromShared( source.Tubes ),
      Text = CotiTextBlockDto.FromShared( source.Text ),
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
      Layout = Layout,
      Tubes = CotiTubeDto.ToShared( Tubes ),
      Text = Text?.ToShared(),
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

public class CotiTubeDto
{
  [JsonPropertyName( "mount" )]
  public CotiMountBlockDto? Mount { get; set; }

  [JsonPropertyName( "pod" )]
  public CotiPodBlockDto? Pod { get; set; }

  [JsonPropertyName( "text" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public CotiTextBlockDto? Text { get; set; }

  public static Dictionary<string, CotiTubeDto?>? FromShared( Dictionary<string, CotiTube>? tubes )
  {
    if( tubes == null )
      return null;

    var dtos = new Dictionary<string, CotiTubeDto?>();
    foreach( var kv in tubes )
    {
      dtos[kv.Key] = new CotiTubeDto
      {
        Mount = CotiMountBlockDto.FromShared( kv.Value.Mount ),
        Pod = kv.Value.Pod == null ? null : CotiPodBlockDto.FromShared( kv.Value.Pod ),
        Text = CotiTextBlockDto.FromShared( kv.Value.Text ),
      };
    }

    return dtos;
  }

  /// <summary>
  /// A null tube or mount stays null instead of becoming a default, which would seat the COTI at the anchor's origin:
  /// CotiTubeValidation drops it and names it.
  /// </summary>
  public static Dictionary<string, CotiTube>? ToShared( Dictionary<string, CotiTubeDto?>? tubes )
  {
    if( tubes == null )
      return null;

    var shared = new Dictionary<string, CotiTube>();
    foreach( var kv in tubes )
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
  [JsonPropertyName( "bone" )]
  public string? Bone { get; set; }

  [JsonPropertyName( "downX" )]
  public float DownX { get; set; }

  [JsonPropertyName( "downY" )]
  public float DownY { get; set; }

  [JsonPropertyName( "downZ" )]
  public float DownZ { get; set; }

  public static CotiPodBlockDto FromShared( CotiPodBlock source ) => new()
  {
    Bone = source.Bone,
    DownX = source.DownX,
    DownY = source.DownY,
    DownZ = source.DownZ,
  };

  public CotiPodBlock ToShared() => new()
  {
    Bone = Bone,
    DownX = DownX,
    DownY = DownY,
    DownZ = DownZ,
  };
}

/// <summary>Each field is written only when set, so a file keeps only what differs from the rule.</summary>
public class CotiTextBlockDto
{
  [JsonPropertyName( "align" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public string? Align { get; set; }

  [JsonPropertyName( "edge" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public float? Edge { get; set; }

  [JsonPropertyName( "y" )]
  [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
  public float? Y { get; set; }

  public static CotiTextBlockDto? FromShared( CotiTextBlock? source ) =>
    source == null ? null : new() { Align = source.Align, Edge = source.Edge, Y = source.Y };

  public CotiTextBlock ToShared() => new() { Align = Align, Edge = Edge, Y = Y };
}
