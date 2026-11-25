using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using Jint;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs a sample V2 Process Script from docs/samples the same way the REST API enricher does
/// (RestApiExternalSearchProvider.InternalExecuteSearchV2 and VerifyConnectionV2), against a fake
/// <c>http</c> object instead of the real external service.
/// </summary>
public static class SampleScriptRunner
{
    /// <summary>The value VerifyConnectionV2 passes for every configured vocabulary key.</summary>
    public const string TestConnectionPlaceholder = "DummyTestValue";

    /// <summary>Reads a script embedded by the test project with <c>LogicalName="Samples.&lt;file&gt;.js"</c>.</summary>
    public static string ReadEmbeddedScript(string name)
    {
        using var stream = typeof(SampleScriptRunner).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Runs the script as enrichment does (120 seconds) and parses its results as the provider does.</summary>
    public static ResultsDto[] Run(string script, FakeHttp http, List<PropertyDto> vocabularies, FakeCache cache = null, string apiKey = null)
    {
        var response = Execute(script, http, vocabularies, cache ?? new FakeCache(), TimeSpan.FromSeconds(120), apiKey);

        // Same as RestApiExternalSearchProvider.InternalExecuteSearchV2
        return JsonConvert.DeserializeObject<ResultsDto[]>(response);
    }

    /// <summary>
    /// Runs the script as Test connection does: placeholder values for every key, and a 2 second limit.
    /// Returns the raw <c>response</c> value.
    /// </summary>
    public static string TestConnection(string script, FakeHttp http, IEnumerable<string> keys, string apiKey = null)
    {
        var vocabularies = keys.Select(x => new PropertyDto { Key = x, Value = TestConnectionPlaceholder }).ToList();
        return Execute(script, http, vocabularies, new FakeCache(), TimeSpan.FromSeconds(2), apiKey);
    }

    public static string Execute(string script, FakeHttp http, List<PropertyDto> vocabularies, FakeCache cache, TimeSpan timeout, string apiKey = null)
    {
        // Same token replacement as RestApiExternalSearchProvider.ReplaceTokens: {APIKey} becomes the
        // API Key field value (an empty string when the field is empty).
        var processScript = script.Contains("{APIKey}") ? script.Replace("{APIKey}", apiKey) : script;

        // Same engine limits and globals as RestApiExternalSearchProvider.InternalExecuteSearchV2
        using var engine = new Jint.Engine(options =>
        {
            options
                .LimitRecursion(64)
                .MaxStatements(10_000)
                .TimeoutInterval(timeout);
        });

        engine
            .SetValue("log", new Action<object>(_ => { }))
            .SetValue("cache", cache)
            .SetValue("vocabularies", vocabularies)
            .SetValue("http", http);

        engine.Execute(processScript);

        return engine.GetValue("response").IsUndefined() ? string.Empty : engine.GetValue("response").ToString();
    }

    public static List<PropertyDto> Values(params (string Key, string Value)[] values)
        => values.Where(x => x.Value != null).Select(x => new PropertyDto { Key = x.Key, Value = x.Value }).ToList();

    public static ResponseDto Ok(object body) => Respond("OK", body);

    public static ResponseDto Respond(string httpStatus, object body)
        => new() { HttpStatus = httpStatus, Content = body as string ?? JsonConvert.SerializeObject(body) };

    public static Dictionary<string, string> Stringify(Dictionary<string, object> data)
        => data.ToDictionary(x => x.Key, x => x.Value?.ToString());

    public class SentRequest
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public List<HeaderDto> Headers { get; set; }
        public JToken Body { get; set; }

        public string Header(string key)
            => Headers?.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>Stands in for ScriptHttpClient, reading the script's request object the same way it does.</summary>
    public class FakeHttp(Func<SentRequest, ResponseDto> handler)
    {
        public List<SentRequest> Requests { get; } = [];

        public static FakeHttp MustNotBeCalled()
            => new(_ => throw new InvalidOperationException("The external service must not be called"));

        public ResponseDto Send(object request)
        {
            var sent = JsonConvert.DeserializeObject<SentRequest>(JsonConvert.SerializeObject(request));
            Requests.Add(sent);
            return handler(sent);
        }
    }

    /// <summary>Stands in for Helper.Cache (which needs a running CluedIn application context).</summary>
    public class FakeCache
    {
        private readonly Dictionary<string, object> _items = new();

        public object Get(string key) => _items.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, object value, double expiration) => _items.TryAdd(key, value);
    }
}
