namespace GUNRPG.WebClient.Helpers;

public static class AuthenticatedSseHelper
{
    public static bool TryGetHttpsBaseUrl(string? baseUrl, out string? httpsBaseUrl)
    {
        httpsBaseUrl = null;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        httpsBaseUrl = baseUrl;
        return true;
    }
}
