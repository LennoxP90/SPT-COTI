using System.Text;
using System.Text.Json;
using Coti.Shared;
using SharpCompress.Writers.SevenZip;

namespace Coti.Server.Web;

/// <summary>
/// Packages the device files for one source mod into a 7z addon archive. The JSON sits at the
/// archive root with no folder, alongside a generated README.
/// </summary>
public static class CotiAddonPackager
{
  private static readonly JsonSerializerOptions Pretty = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

  /// <summary>Devices grouped by the mod they depend on. Devices with no `requires` are skipped.</summary>
  public static Dictionary<string, List<CotiDeviceFile>> GroupBySourceMod(
    IEnumerable<CotiDeviceFile> devices)
  {
    var groups = new Dictionary<string, List<CotiDeviceFile>>(StringComparer.OrdinalIgnoreCase);

    foreach (var device in devices)
    {
      if (string.IsNullOrWhiteSpace(device.Requires))
      {
        continue;
      }

      if (!groups.TryGetValue(device.Requires, out var list))
      {
        groups[device.Requires] = list = new List<CotiDeviceFile>();
      }

      list.Add(device);
    }

    return groups;
  }

  /// <summary>`com.wtt.contentbackport` becomes `wtt-contentbackport`.</summary>
  public static string SlugFor(string requires)
  {
    var trimmed = requires.Trim();

    if (trimmed.StartsWith("com.", StringComparison.OrdinalIgnoreCase))
    {
      trimmed = trimmed[4..];
    }

    return trimmed.Replace('.', '-').ToLowerInvariant();
  }

  public static string FileNameFor(string requires) => $"coti-addon-{SlugFor(requires)}.7z";

  public static byte[] Build(string requires, IReadOnlyList<CotiDeviceFile> devices)
  {
    using var buffer = new MemoryStream();

    using (var writer = SevenZipWriter.OpenWriter(buffer, new SevenZipWriterOptions()))
    {
      foreach (var device in devices)
      {
        var name = $"{device.Device}.json";
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ToDto(device), Pretty));
        writer.Write(name, new MemoryStream(json), DateTime.UtcNow);
      }

      var readme = Encoding.UTF8.GetBytes(Readme(requires, devices));
      writer.Write("README.md", new MemoryStream(readme), DateTime.UtcNow);
    }

    return buffer.ToArray();
  }

  /// <summary>The DTO carries the camelCase names a device file is written in.</summary>
  private static object ToDto(CotiDeviceFile device) => CotiDeviceDto.FromShared(device);

  private static string Readme(string requires, IReadOnlyList<CotiDeviceFile> devices)
  {
    var slug = SlugFor(requires);
    var sb = new StringBuilder();

    sb.AppendLine($"# COTI addon: {slug}");
    sb.AppendLine();
    sb.AppendLine($"Adds COTI mounting support for the night vision devices from **{requires}**.");
    sb.AppendLine();
    sb.AppendLine("| File | Device |");
    sb.AppendLine("|---|---|");

    foreach (var device in devices)
    {
      sb.AppendLine($"| `{device.Device}.json` | {device.DisplayName ?? device.Device} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Installing");
    sb.AppendLine();
    sb.AppendLine("Copy the `.json` files into your server's COTI device folder:");
    sb.AppendLine();
    sb.AppendLine("```");
    sb.AppendLine("<SPT>/user/mods/LennoxP90-COTI/nvghostcompat/");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("Loose, no subfolder. Restart the server. Each supported device gains a");
    sb.AppendLine("`mod_coti` slot and the COTI mounts with the pose in the file.");
    sb.AppendLine();
    sb.AppendLine("The path differs between SPT versions: `SPT/user/mods/...` on 4.0 and");
    sb.AppendLine("`SPT_Runtime/user/mods/...` on 4.1.");
    sb.AppendLine();
    sb.AppendLine($"## If you do not have {requires}");
    sb.AppendLine();
    sb.AppendLine($"Nothing breaks. Every device here declares `requires: {requires}`, so COTI skips");
    sb.AppendLine("it with one line in the log rather than warning about items it cannot find.");
    sb.AppendLine();
    sb.AppendLine("## If you already played without this addon");
    sb.AppendLine();
    sb.AppendLine("COTI will have auto-discovered those devices and written a stub with a guessed");
    sb.AppendLine("pose, so they worked but sat wrong. Installing this takes precedence: a measured");
    sb.AppendLine("pose always beats a discovered guess, and the log names the superseded stub.");
    sb.AppendLine();
    sb.AppendLine("## Retuning a pose");
    sb.AppendLine();
    sb.AppendLine("Use the mount editor in the server's web UI, or the **COTI Pose** button in game.");
    sb.AppendLine("Either rewrites the file in place. `ADDONS.md` in the COTI source has the full");
    sb.AppendLine("field reference.");
    sb.AppendLine();
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine($"Exported from the COTI mount editor, {DateTime.UtcNow:yyyy-MM-dd}.");

    return sb.ToString();
  }
}
