using System;
using System.Text;

namespace CluedIn.ExternalSearch.Providers.RestApi.Helper;

public class StringEncodingHelper
{
    public static string ToBase64(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string FromBase64(string base64)
    {
        if (base64 == null) return null;

        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    public static string UrlEncode(string value)
    {
        return value == null ? null : Uri.EscapeDataString(value);
    }

    public static string UrlDecode(string value)
    {
        return value == null ? null : Uri.UnescapeDataString(value);
    }
}