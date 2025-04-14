using System.Collections.Generic;

namespace CluedIn.ExternalSearch.Providers.RestApi.Models;

/// <summary>
/// Represents the response from an HTTP request.
/// </summary>
public class ResponseDto
{
    /// <summary>
    /// The HTTP status code of the response (e.g., "200 OK").
    /// </summary>
    public string HttpStatus { get; set; }
    /// <summary>
    /// The content of the response body.
    /// </summary>
    public string Content { get; set; }
    /// <summary>
    /// The content type of the response (e.g., "application/json").
    /// </summary>
    public string ContentType { get; set; }
    /// <summary>
    /// The list of headers included in the response.
    /// </summary>
    public List<HeaderDto> Headers { get; set; }
}

/// <summary>
/// Represents the processed results from a response.
/// </summary>
public class ResultsDto
{
    /// <summary>
    /// The key-value data extracted from the response.
    /// </summary>
    public Dictionary<string, object> Data { get; set; }
    /// <summary>
    /// The score associated with the results, indicating relevance or confidence.
    /// </summary>
    public double Score { get; set; }
}


