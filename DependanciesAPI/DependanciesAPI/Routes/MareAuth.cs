namespace MareSynchronos.API.Routes;

public class MareAuth
{
    public const string OAuth = "/oauth";
    public const string Auth = "/auth";
    public const string Auth_CreateIdent = "createWithIdent";
    public const string Auth_RenewToken = "renewToken";
    public const string OAuth_GetUIDsBasedOnSecretKeys = "getUIDsViaSecretKey";
    public const string OAuth_CreateOAuth = "createWithOAuth";
    public const string OAuth_RenewOAuthToken = "renewToken";
    public const string OAuth_GetDiscordOAuthEndpoint = "getDiscordOAuthEndpoint";
    public const string OAuth_GetUIDs = "getUIDs";
    public const string OAuth_GetDiscordOAuthToken = "getDiscordOAuthToken";
    public static Uri AuthFullPath(Uri baseUri) => new Uri(baseUri, Auth + "/" + Auth_CreateIdent);
    public static Uri AuthWithOauthFullPath(Uri baseUri) => new Uri(baseUri, OAuth + "/" + OAuth_CreateOAuth);
    public static Uri RenewTokenFullPath(Uri baseUri) => new Uri(baseUri, Auth + "/" + Auth_RenewToken);
    public static Uri RenewOAuthTokenFullPath(Uri baseUri) => new Uri(baseUri, OAuth + "/" + OAuth_RenewOAuthToken);
    public static Uri GetUIDsBasedOnSecretKeyFullPath(Uri baseUri) => new Uri(baseUri, OAuth + "/" + OAuth_GetUIDsBasedOnSecretKeys);
    public static Uri GetDiscordOAuthEndpointFullPath(Uri baseUri) => new Uri(baseUri, OAuth + "/" + OAuth_GetDiscordOAuthEndpoint);
    public static Uri GetDiscordOAuthTokenFullPath(Uri baseUri, string sessionId) => new Uri(baseUri, OAuth + "/" + OAuth_GetDiscordOAuthToken + "?sessionId=" + sessionId);
    public static Uri GetUIDsFullPath(Uri baseUri) => new Uri(baseUri, OAuth + "/" + OAuth_GetUIDs);
}