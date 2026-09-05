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

  /// <summary>
  /// Where the files land, relative to an SPT 4.1 install root. The archive carries the whole
  /// path so extracting it over an install puts every file where it belongs.
  ///
  /// 4.1 only: Coti.Server.Web is absent from the 4.0 distribution, so nobody on 4.0 can reach
  /// the export button.
  /// </summary>
  public const string InstallPath = "SPT_Runtime/user/mods/LennoxP90-COTI/nvghostcompat";

  public static byte[] Build(string requires, IReadOnlyList<CotiDeviceFile> devices)
  {
    using var buffer = new MemoryStream();

    using (var writer = SevenZipWriter.OpenWriter(buffer, new SevenZipWriterOptions()))
    {
      foreach (var device in devices)
      {
        var name = $"{InstallPath}/{device.Device}.json";
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ToDto(device), Pretty));
        writer.Write(name, new MemoryStream(json), DateTime.UtcNow);
      }

      // Every addon installs into one shared nvghostcompat folder, so a plain README.md would be
      // overwritten by the next addon extracted, and at the archive root it would land in the
      // install root.
      var readme = Encoding.UTF8.GetBytes(Readme(requires, devices));
      writer.Write($"{InstallPath}/README-{SlugFor(requires)}.md", new MemoryStream(readme), DateTime.UtcNow);
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
    sb.AppendLine("Extract this archive over your SPT install, keeping the folder structure. The");
    sb.AppendLine("files land in:");
    sb.AppendLine();
    sb.AppendLine("```");
    sb.AppendLine($"{InstallPath}/");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("Restart the server. Each supported device gains a `mod_coti` slot and the COTI");
    sb.AppendLine("mounts with the pose in the file.");
    sb.AppendLine();
    sb.AppendLine("On SPT 4.0 the same files go in `user/mods/LennoxP90-COTI/nvghostcompat/`, with");
    sb.AppendLine("no `SPT_Runtime`, so copy them out rather than extracting over the install.");
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
    sb.AppendLine($"Device file schema {CotiDeviceFile.CurrentSchema}. A COTI too old to read it");
    sb.AppendLine("says so in the log rather than loading the device.");
    sb.AppendLine();
    sb.AppendLine(
        $"Exported from the COTI {CotiVersion.Current} mount editor, {DateTime.UtcNow:yyyy-MM-dd}.");

    return sb.ToString();
  }
}
