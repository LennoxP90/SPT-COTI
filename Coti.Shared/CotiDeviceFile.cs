using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// One physical night vision device. Schema 1 is permanent: the SPT 4.0 line receives no
  /// further releases, so a 4.0 addon can never be re-issued against a newer shape.
  /// Fields may be added; none may be removed or repurposed.
  ///
  /// No serializer attributes: Coti.Shared must reference neither System.Text.Json nor
  /// Newtonsoft. Each half owns its own attributed DTO and maps across, and
  /// CotiWireContractTests pins the two together.
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

    /// <summary>
    /// Mask and Mount are the legacy pair, mandatory on every file: releases before 3.3.0 read only these and skip a
    /// file without them. On a multi-tube file they hold the home tube's 16:9 circle and mount.
    /// </summary>
    public CotiMaskBlock Mask { get; set; } = new CotiMaskBlock();
    public CotiMountBlock Mount { get; set; } = new CotiMountBlock();

    /// <summary>A CotiLayouts name. Null on a v1 file.</summary>
    public string? Layout { get; set; }

    /// <summary>Keyed by CotiTubes label. A layout tube missing here mounts at the legacy Mount.</summary>
    public Dictionary<string, CotiTube>? Tubes { get; set; }

    /// <summary>Where a file without tubes places its one circle's messages. Null keeps the rule.</summary>
    public CotiTextBlock? Text { get; set; }

    /// <summary>Trust it only after CotiTubeValidation.Normalise, which clears both unless the pair is usable.</summary>
    public bool IsMultiTube => Layout != null && Tubes != null;

    /// <summary>
    /// A deep copy. MemberwiseClone first, so a field added later is carried rather than silently dropped, as a
    /// field-by-field rebuild drops it.
    /// </summary>
    public CotiDeviceFile Copy()
    {
      var copy = (CotiDeviceFile)MemberwiseClone();
      copy.Hosts = Hosts.ConvertAll( h => h?.Copy()! );
      copy.Mask = Mask.Copy();
      copy.Mount = Mount.Copy();
      copy.Text = Text?.Copy();

      if( Tubes != null )
      {
        copy.Tubes = new Dictionary<string, CotiTube>();
        foreach( var kv in Tubes )
          copy.Tubes[kv.Key] = kv.Value?.Copy()!;
      }

      return copy;
    }
  }

  public class CotiHostRef
  {
    public string? Id { get; set; }

    /// <summary>
    /// Prefab path, the fallback identity. The pose depends on the mesh rather than the id, so
    /// this survives a host mod renumbering its items.
    /// </summary>
    public string? Prefab { get; set; }

    /// <summary>Variant name, for log lines only. Optional.</summary>
    public string? Label { get; set; }

    // Flat, so a member-wise clone is a full copy; likewise the blocks below.
    public CotiHostRef Copy() => (CotiHostRef)MemberwiseClone();
  }

  public class CotiMaskBlock
  {
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Radius { get; set; }
    public float Feather { get; set; }

    public CotiMaskBlock Copy() => (CotiMaskBlock)MemberwiseClone();
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

    public CotiMountBlock Copy() => (CotiMountBlock)MemberwiseClone();
  }

  /// <summary>One tube of a multi-tube device: where its COTI mounts, and the pod that can close it.</summary>
  public class CotiTube
  {
    public CotiMountBlock Mount { get; set; } = new CotiMountBlock();

    /// <summary>Null when the tube has no pod of its own and simply follows the goggles.</summary>
    public CotiPodBlock? Pod { get; set; }

    /// <summary>Where this tube's messages sit. Null keeps the rule.</summary>
    public CotiTextBlock? Text { get; set; }

    public CotiTube Copy() => new CotiTube { Mount = Mount?.Copy()!, Pod = Pod?.Copy(), Text = Text?.Copy() };
  }

  /// <summary>
  /// Where a circle's display messages sit, as CotiDisplayLayout.Resolve reads it. Every field is optional and a missing
  /// one keeps the rule: Align is left, right or center, the side of the text that is anchored; Edge is that edge's
  /// distance from the circle's centre in radii (for center, the text middle's offset, positive right); Y is a vertical
  /// offset in radii, positive up.
  /// </summary>
  public class CotiTextBlock
  {
    public string? Align { get; set; }
    public float? Edge { get; set; }
    public float? Y { get; set; }

    public CotiTextBlock Copy() => (CotiTextBlock)MemberwiseClone();

    public static bool TryParseAlign( string? name, out CotiTubeSide align )
    {
      switch( name )
      {
        case "left": align = CotiTubeSide.Left; return true;
        case "right": align = CotiTubeSide.Right; return true;
        case "center": align = CotiTubeSide.Center; return true;
        default: align = CotiTubeSide.Center; return false;
      }
    }

    public static string AlignName( CotiTubeSide align ) =>
        align == CotiTubeSide.Left ? "left" : align == CotiTubeSide.Right ? "right" : "center";
  }

  /// <summary>
  /// A pod's deployed pose: the pod bone's localRotation when down, in Unity Euler degrees, the convention of
  /// CotiMountBlock's RotationX/Y/Z. Bone defaults to the tube's anchor bone.
  /// </summary>
  public class CotiPodBlock
  {
    public string? Bone { get; set; }
    public float DownX { get; set; }
    public float DownY { get; set; }
    public float DownZ { get; set; }

    public CotiPodBlock Copy() => (CotiPodBlock)MemberwiseClone();
  }
}
