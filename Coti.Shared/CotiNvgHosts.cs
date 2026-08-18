using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// The night vision devices the COTI can clip to. The server injects a mod_coti slot into exactly
  /// these and the client seeds its per-host settings from the same table, so neither half can
  /// disagree about what is supported. A host missing from the database is skipped, not an error -
  /// several hosts come from optional mods.
  ///
  /// Supporting a device means its pose has been tuned in a raid, which is why this is a table and
  /// not a config file: an id on its own places the COTI somewhere arbitrary on an unknown mesh.
  /// </summary>
  public static class CotiNvgHosts
  {
    public class NvgHost
    {
      public NvgHost( string templateId, string displayName, string maskName )
      {
        TemplateId = templateId;
        DisplayName = displayName;
        MaskName = maskName;
      }

      /// <summary>
      /// Item template id of the night vision device.
      /// </summary>
      public string TemplateId { get; }

      /// <summary>
      /// Used for the F12 section heading and log lines. Not shown in game.
      /// </summary>
      public string DisplayName { get; }

      /// <summary>
      /// Matches the per-host default block, and labels the generated mask.
      /// </summary>
      public string MaskName { get; }
    }

    /// <summary>
    /// Adding a device is one line here plus its block in coti-defaults.json, which
    /// CotiNvgHostsTests pairs.
    ///
    /// APPEND, never insert: CotiF12Config.FirstHost reads the shared image defaults off whichever
    /// entry comes first, so reordering silently moves them to a different device.
    /// </summary>
    public static readonly IReadOnlyList<NvgHost> All = new List<NvgHost>
    {
      new NvgHost("57235b6f24597759bf5a30f1", "PVS-14", "pvs14"),
      new NvgHost("5c066e3a0db834001b7353f0", "N-15", "n15"),
      new NvgHost("5c0558060db834001b735271", "GPNVG-18", "gpnvg"),
      new NvgHost("689b889473ebd6871805edd6", "PVS-31A", "pvs31a"),

      // WTT Clothing and Gear. Two items sharing one mesh (nvg_actinblack_dtnvg.bundle), so the
      // pose and mask geometry are deliberately identical - only the phosphor colour differs, and
      // the COTI does not care about that. Ids verified against CAG's own goggles.json on both its
      // 4.0 and 4.1-dev branches, so they survive the 4.1 update.
      new NvgHost("6974ce066e50d4be623b8d9b", "DTNVS (White Phos.)", "dtnvs_white"),
      new NvgHost("6974cf52ee1fb8a0683b8d9d", "DTNVS (Green Phos.)", "dtnvs_green"),
    };
  }
}
