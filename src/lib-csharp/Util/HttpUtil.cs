using System;
using System.Collections.Generic;

namespace Velopack.Util
{
    internal static class HttpUtil
    {
        public static Uri AddQueryParamsToUri(Uri uri, IEnumerable<KeyValuePair<string, string>> newQuery)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            foreach (var entry in newQuery) {
                query[entry.Key] = entry.Value;
            }

            var builder = new UriBuilder(uri);
            builder.Query = query.ToString();

            return builder.Uri;
        }
        
        public static bool IsHttpUrl(string urlOrPath)
        {
            if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out Uri? uri)) {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }
        
        public static Sources.IFileDownloader CreateDefaultDownloader()
        {
            return new Sources.HttpClientFileDownloader();
        }

        public static Uri AppendPathToUri(Uri uri, string path)
        {
            var builder = new UriBuilder(uri);
            if (!builder.Path.EndsWith("/")) {
                builder.Path += "/";
            }

            builder.Path += path;
            return builder.Uri;
        }

        public static Uri EnsureTrailingSlash(Uri uri)
        {
            return AppendPathToUri(uri, "");
        }
    }
}