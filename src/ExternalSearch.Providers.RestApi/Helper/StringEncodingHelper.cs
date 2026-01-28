using System;
using System.Text;

namespace CluedIn.ExternalSearch.Providers.RestApi.Helper;

public class StringEncodingHelper
{
    public string ToBase64(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value), "Value to be converted to base64 cannot be empty.");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public string FromBase64(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentNullException(nameof(base64), "Base64 value cannot be empty.");

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            throw new ArgumentNullException(nameof(base64), "Base64 value is in an incorrect format");
        }
    }

    public string UrlEncode(string value)
    {
        return value == null ? null : Uri.EscapeDataString(value);
    }

    public string UrlDecode(string value)
    {
        return value == null ? null : Uri.UnescapeDataString(value);
    }
}