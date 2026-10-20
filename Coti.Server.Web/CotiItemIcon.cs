namespace Coti.Server.Web;

/// <summary>
/// The fallback item picture for the ECOTI pages, when no game client has sent the icon yet (CotiIconStore): tarkov.dev
/// by template id. It has every retail id and none of a mod's; those fall back to the page's placeholder.
/// </summary>
public static class CotiItemIcon
{
    public static string Url(string tplId) => $"https://assets.tarkov.dev/{tplId}-icon.webp";
}
