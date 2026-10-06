using System.Buffers.Binary;
using Coti.Server;
using Coti.Shared;

namespace Coti.Server.Web;

/// <summary>
/// The mount editor's view of a device's tubes: one tab per label of its layout, the posed mounts, the text blocks, and
/// every circle on a game-sized screen. A v1 device is one tube under the empty label.
/// </summary>
public static class CotiViewerTubes
{
  /// <summary>The label a v1 device's one mount travels under, to the viewer and back.</summary>
  public const string V1Tube = "";

  /// <summary>1080 rows, as in game; the width follows the aspect.</summary>
  private static readonly (string Name, int Width, int Height)[] Screens =
  [
    ("16:9", 1920, 1080),
    ("16:10", 1728, 1080),
    ("21:9", 2520, 1080),
  ];

  /// <summary>One display text image, as the viewer loads it and as the text rule sizes it.</summary>
  public sealed record TextImage(string Url, int Width, int Height);

  /// <summary>Null for a v1 device. Validation has already turned an unknown layout into v1.</summary>
  public static CotiLayout? LayoutOf(CotiDeviceFile device) => CotiSlotInjector.LayoutOf(device);

  /// <summary>The tab the viewer opens on.</summary>
  public static string Home(CotiDeviceFile device) => LayoutOf(device)?.Home ?? V1Tube;

  /// <summary>Every posed tube's mount. A tube missing here mounts at the legacy mount in game.</summary>
  public static Dictionary<string, CotiMountBlockDto> Mounts(CotiDeviceFile device) =>
    LayoutOf(device) is null
      ? new Dictionary<string, CotiMountBlockDto> { [V1Tube] = CotiMountBlockDto.FromShared(device.Mount) }
      : device.Tubes!.ToDictionary(t => t.Key, t => CotiMountBlockDto.FromShared(t.Value.Mount));

  /// <summary>Each tube's text block as the file holds it, null where the rule places the text.</summary>
  public static Dictionary<string, CotiTextBlockDto?> Texts(CotiDeviceFile device)
  {
    var layout = LayoutOf(device);

    if (layout is null)
    {
      return new Dictionary<string, CotiTextBlockDto?> { [V1Tube] = CotiTextBlockDto.FromShared(device.Text) };
    }

    return layout.Tubes.ToDictionary(
      t => t.Label,
      t => CotiTextBlockDto.FromShared(device.Tubes!.TryGetValue(t.Label, out var tube) ? tube?.Text : null));
  }

  /// <summary>
  /// The text rule's numbers, so the viewer places and drags each copy as CotiDisplayLayout.TryRect does without
  /// restating them.
  /// </summary>
  public static object TextRule(TextImage? message) => new
  {
    texel = CotiDisplayLayout.ScreenHeightsPerTexel,
    margin = CotiDisplayLayout.ScreenMargin,
    edge = CotiDisplayLayout.OuterEdge,
    width = message?.Width ?? 0,
    height = message?.Height ?? 0,
  };

  /// <summary>One tab per label of the layout, left to right, with its slot, partner and pod. None for v1.</summary>
  public static object[] Tabs(CotiDeviceFile device)
  {
    var layout = LayoutOf(device);

    if (layout is null)
    {
      return [];
    }

    return layout.Tubes.Select(t =>
    {
      var partner = CotiTubes.PartnerOf(t.Label);
      device.Tubes!.TryGetValue(t.Label, out var tube);
      var pod = tube?.Pod;

      return (object)new
      {
        label = t.Label,
        name = CotiTubes.PanelName(t.Label, layout),
        slot = CotiTubes.SlotDisplayName(CotiTubes.SlotName(t.Label, layout)),
        partner = partner is not null && layout.Find(partner) is not null ? partner : null,
        // The pod bone defaults to the tube's anchor bone.
        pod = pod is null
          ? null
          : new
          {
            bone = string.IsNullOrEmpty(pod.Bone) ? tube!.Mount.AnchorBone : pod.Bone,
            downX = pod.DownX,
            downY = pod.DownY,
            downZ = pod.DownZ,
          },
      };
    }).ToArray();
  }

  /// <summary>
  /// Each screen with every circle, placed by the in-game rule, and the align its text takes when the file says none.
  /// The viewer places the text itself, from TextRule, because it moves while being dragged.
  /// </summary>
  public static object[] Preview(CotiDeviceFile device)
  {
    var layout = LayoutOf(device);

    return Screens.Select(screen =>
    {
      var aspect = (float)screen.Width / screen.Height;
      var circles = layout is null
        ? new[] { (Label: V1Tube, Circle: CotiCircles.FromMask(device.Mask)) }
        : layout.Tubes.Select(t => (Label: t.Label, Circle: CotiCircles.FromLayout(t, aspect))).ToArray();

      return (object)new
      {
        name = screen.Name,
        width = screen.Width,
        height = screen.Height,
        circles = circles
          .Select(c => new
          {
            label = c.Label,
            u = c.Circle.U,
            v = c.Circle.V,
            r = c.Circle.Radius,
            // The v1 tube travels under "", which is the null label the rule centres.
            align = CotiTextBlock.AlignName(CotiDisplayLayout.Resolve(null, c.Label == V1Tube ? null : c.Label).Align),
          })
          .ToArray(),
      };
    }).ToArray();
  }

  /// <summary>
  /// The widest display text image, which is the longest message. Copied into wwwroot/textures at build; null when
  /// the folder is missing, and the preview then draws no text.
  /// </summary>
  public static TextImage? LongestMessage()
  {
    var dir = Path.Combine(CotiHostMeshes.MeshRootPath(), "textures");

    return Directory.Exists(dir)
      ? Directory.GetFiles(dir, "coti_text_*.png").Select(ReadPng).OfType<TextImage>()
          .OrderByDescending(t => t.Width).FirstOrDefault()
      : null;
  }

  /// <summary>Width and height from the PNG header, big-endian at bytes 16 and 20.</summary>
  private static TextImage? ReadPng(string path)
  {
    try
    {
      var head = new byte[24];
      using var file = File.OpenRead(path);
      file.ReadExactly(head);

      return new TextImage(
        $"/coti-assets/textures/{Path.GetFileName(path)}",
        BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16)),
        BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20)));
    }
    catch (IOException)
    {
      return null;
    }
  }
}
