using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CluedIn.ExternalSearch.Providers.RestApi.Models
{
    public class ScriptHttpClient
    {
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

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


            using var client = new HttpClient();
            client.Timeout = _timeout;

            using var message = new HttpRequestMessage(new HttpMethod(method), uri);

            if (requestDto.Headers != null)
            {
                foreach (var header in requestDto.Headers.Where(header => !string.IsNullOrWhiteSpace(header.Key)))
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (method.Equals(HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase)
                && requestDto.Body != null)
            {
                var json = JsonConvert.SerializeObject(requestDto.Body);
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = client.SendAsync(message)
                                 .GetAwaiter()
                                 .GetResult();
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("HTTP request timed out", ex);
            }

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
    }
}
