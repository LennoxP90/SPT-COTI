using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// One physical night vision device. SCHEMA 1 IS PERMANENT: SPT 4.0's 2.0.0 is the last
  /// release of that line, so a 4.0 addon can never be re-issued against a newer shape.
  /// Fields may be ADDED; none may be removed or repurposed.
  ///
  /// No serializer attributes here deliberately - Coti.Shared must reference neither
  /// System.Text.Json nor Newtonsoft. Each half owns its own attributed DTO and maps across,
  /// and CotiWireContractTests pins the two together.
  /// </summary>
  public class CotiDeviceFile
  {
    public const int CurrentSchema = 1;

    public int Schema { get; set; }
    public string? Device { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>Mod guid this device's hosts come from. Null for vanilla devices.</summary>
    public string? Requires { get; set; }

    /// <summary>False on an auto-generated stub, true once a human has posed it.</summary>
    public bool Tuned { get; set; }

    public List<CotiHostRef> Hosts { get; set; } = new List<CotiHostRef>();
    public CotiMaskBlock Mask { get; set; } = new CotiMaskBlock();
    public CotiMountBlock Mount { get; set; } = new CotiMountBlock();
  }

  public class CotiHostRef
  {
    public string? Id { get; set; }

    /// <summary>
    /// Prefab path, the fallback identity. The pose is a function of the MESH, not of the id,
    /// so this survives a host mod renumbering its items.
    /// </summary>
    public string? Prefab { get; set; }

    /// <summary>Variant name, for log lines only. Optional.</summary>
    public string? Label { get; set; }
  }

  public class CotiMaskBlock
  {
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Radius { get; set; }
    public float Feather { get; set; }
  }

  public class CotiMountBlock
  {
    public string? AnchorBone { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float RollDegrees { get; set; }
    public float PitchDegrees { get; set; }
    public float YawDegrees { get; set; }
    public float Scale { get; set; } = 1f;
  }
}
