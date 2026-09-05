using System.Text;
using Coti.Shared;

namespace Coti.Server.Web;

/// <summary>
/// Cache key for the viewer's scripts and stylesheet.
///
/// Not the mod version, which only moves at release: an edit to cotiViewer.js between releases
/// was invisible to a browser that had already loaded the page, which went on running the old
/// module against a new server with nothing to say so.
///
/// Stamping the files means the key moves when they do, including a file copied onto a live
/// server.
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
    /// Read per page load, not cached: an asset copied onto a running server takes effect
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
