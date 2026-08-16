using System.Collections.Generic;

namespace Coti.Shared
{
  /// <summary>
  /// The night vision devices the COTI can clip to. The server injects a mod_coti slot into exactly
  /// these and the client names its F12 sections from the same table, so neither half can disagree
  /// about what is supported. A host missing from the database is skipped, not an error.
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

      /// <summary>Item template id of the night vision device.</summary>
      public string TemplateId { get; }

      /// <summary>Used for the F12 section heading and log lines. Not shown in game.</summary>
      public string DisplayName { get; }

      /// <summary>Matches the per-host default block, and labels the generated mask.</summary>
      public string MaskName { get; }
    }

    /// <summary>Adding a device is one line here.</summary>
    public static readonly IReadOnlyList<NvgHost> All = new List<NvgHost>
    {
      new NvgHost("57235b6f24597759bf5a30f1", "PVS-14", "pvs14"),
      new NvgHost("5c066e3a0db834001b7353f0", "N-15", "n15"),
      new NvgHost("5c0558060db834001b735271", "GPNVG-18", "gpnvg"),
      new NvgHost("689b889473ebd6871805edd6", "PVS-31A", "pvs31a"),
    };
  }
}
