using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CluedIn.ExternalSearch.Providers.RestApi.Models;

public class ScriptHttpClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// Sends an HTTP request based on the supplied request object and returns a structured response.
    /// </summary>
    /// <param name="request">
    /// An object that can be serialized into a <see cref="RequestDto"/>, containing the URL,
    /// HTTP method, headers, and optional body to be sent.
    /// </param>
    /// <returns>
    /// A <see cref="ResponseDto"/> containing the HTTP status, response content, headers,
    /// and content type returned by the remote server.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the request object cannot be deserialized into a <see cref="RequestDto"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the request URL is missing or invalid.</exception>
    /// <exception cref="TimeoutException">Thrown when the HTTP request exceeds the configured timeout.</exception>
    public ResponseDto Send(object request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var requestJson = JsonConvert.SerializeObject(request);

        var requestDto = JsonConvert.DeserializeObject<RequestDto>(requestJson)
                         ?? throw new InvalidOperationException("Failed to deserialize request");

        if (string.IsNullOrWhiteSpace(requestDto.Url))
            throw new ArgumentException("Request URL is required");

        if (!Uri.TryCreate(requestDto.Url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {requestDto.Url}");

        var method = string.IsNullOrWhiteSpace(requestDto.Method)
            ? HttpMethod.Get.Method
            : requestDto.Method;


        using var message = new HttpRequestMessage(new HttpMethod(method), uri);

        if (requestDto.Headers != null)
        {
            foreach (var header in requestDto.Headers.Where(header => !string.IsNullOrWhiteSpace(header.Key)))
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (!method.Equals(HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase)
            && requestDto.Body != null)
        {
            var json = JsonConvert.SerializeObject(requestDto.Body);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = Client.SendAsync(message)
                .GetAwaiter()
                .GetResult();
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new ResponseDto
            {
                HttpStatus = response.StatusCode.ToString(),
                Content = content,
                ContentType = response.Content?.Headers.ContentType?.ToString(),
                Headers = response.Headers
                    .Select(h => new HeaderDto
                    {
                        Key = h.Key,
                        Value = string.Join(",", h.Value)
                    })
                    .ToList()
            };
        }
        catch (TaskCanceledException ex)
        {
            throw new TimeoutException("HTTP request timed out", ex);
        }
    }
}