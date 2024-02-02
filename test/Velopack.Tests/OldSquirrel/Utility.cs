namespace Velopack.Tests.OldSquirrel;

internal static class Utility
{
    public static bool IsHttpUrl(string urlOrPath)
    {
        var uri = default(Uri);
        if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out uri)) {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
