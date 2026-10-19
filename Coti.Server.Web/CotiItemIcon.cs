namespace Coti.Server.Web;

/// <summary>
/// Item pictures for the ECOTI pages. The SPT server has no per item image (the game renders icons from the models
/// on the client), so the browser loads tarkov.dev's by template id. It has every retail id and none of a mod's;
/// those fall back to the page's placeholder.
/// </summary>
public static class CotiItemIcon
{
    public static string Url(string tplId) => $"https://assets.tarkov.dev/{tplId}-icon.webp";
}
