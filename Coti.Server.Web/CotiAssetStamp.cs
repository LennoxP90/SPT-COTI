using System.Text;
using Coti.Shared;

namespace Coti.Server.Web;

/// <summary>
/// Cache key for the viewer's scripts and stylesheet.
///
/// Derived from the files rather than the mod version, which only changes at release, so a
/// browser that already loaded the page picks up an edited script instead of running the old
/// module against a new server. The key changes whenever the files do, including a file copied
/// onto a live server.
/// </summary>
internal static class CotiAssetStamp
{
    private static readonly string[] Assets =
    [
        Path.Combine("js", "cotiViewer.js"),
        Path.Combine("js", "interop.js"),
        Path.Combine("js", "viewCube.js"),
        Path.Combine("css", "coti-viewer.css"),
    ];

    /// <summary>
    /// Read per page load rather than cached, so an asset copied onto a running server takes effect
    /// without a restart.
    /// </summary>
    public static string Current
    {
        get
        {
            var root = Path.Combine(
                Path.GetDirectoryName(typeof(CotiAssetStamp).Assembly.Location) ?? string.Empty,
                "wwwroot");

            var builder = new StringBuilder();

            foreach (var asset in Assets)
            {
                var file = new FileInfo(Path.Combine(root, asset));

                if (file.Exists)
                {
                    builder.Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks).Append(';');
                }
            }

            // Nothing readable: fall back to the version rather than serving one constant key.
            return builder.Length == 0
                ? CotiVersion.Current
                : Convert.ToHexString(
                    System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..12];
        }
    }
}
