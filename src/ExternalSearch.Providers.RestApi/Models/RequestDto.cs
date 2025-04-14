using System.Collections.Generic;

namespace CluedIn.ExternalSearch.Providers.RestApi.Models;

/// <summary>
/// Represents the HTTP request.
/// </summary>
public class RequestDto
{
    /// <summary>
    /// The HTTP request method (e.g., GET or POST).
    /// </summary>
    public string Method { get; set; }
    /// <summary>
    /// The URL for the HTTP request.
    /// </summary>
    public string Url { get; set; }
    /// <summary>
    /// The list of headers to include in the HTTP request.
    /// </summary>
    public List<HeaderDto> Headers { get; set; }
    /// <summary>
    /// The API key for authenticating the HTTP request.
    /// </summary>
    public string ApiKey { get; set; }
    /// <summary>
    /// The body content of the HTTP request.
    /// </summary>
    public object Body { get; set; }
}

/// <summary>
/// Represents the body content of an HTTP request.
/// </summary>
public class BodyDto
{
    /// <summary>
    /// The list of key-value properties forming the request body.
    /// </summary>
    public List<PropertyDto> Properties { get; set; }
}

/// <summary>
/// Represents a key-value pair for a property in the HTTP request body.
/// </summary>
public class PropertyDto
{
    /// <summary>
    /// The key of the property.
    /// </summary>
    public string Key { get; set; }
    /// <summary>
    /// The value of the property.
    /// </summary>
    public string Value { get; set; }
}

/// <summary>
/// Represents a key-value pair for an HTTP request header.
/// </summary>
public class HeaderDto
{
    /// <summary>
    /// The key of the header.
    /// </summary>
    public string Key { get; set; }
    /// <summary>
    /// The value of the header.
    /// </summary>
    public string Value { get; set; }
}

