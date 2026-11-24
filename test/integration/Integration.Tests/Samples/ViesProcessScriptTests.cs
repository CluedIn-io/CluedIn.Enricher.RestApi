using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using Jint;
using Jint.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the VIES VAT validation sample Process Script (docs/samples/vies-vat-validation) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real VIES service.
/// </summary>
public class ViesProcessScriptTests
{
    private const string ViesUrl = "https://ec.europa.eu/taxation_customs/vies/rest-api/check-vat-number";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.vies-process-script.js");

    [Fact]
    public void ValidNumberWithCountryPrefix_PostsToViesAndReturnsViesProperties()
    {
        var http = new FakeHttp(_ => Ok(new
        {
            countryCode = "DE",
            vatNumber = "100",
            requestDate = "2026-11-24T03:44:18.999Z",
            valid = true,
            requestIdentifier = "",
            name = "John Doe",
            address = "123 Main St, Anytown, UK",
        }));

        var results = Run(http, Vocabularies(("organization.vat", "de 100")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(ViesUrl, request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Contains(request.Headers, h => h.Key == "Content-Type" && h.Value == "application/json");
        Assert.Equal("DE", (string)request.Body["countryCode"]);
        Assert.Equal("100", (string)request.Body["vatNumber"]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["vies.valid"] = "True",
            ["vies.countryCode"] = "DE",
            ["vies.vatNumber"] = "100",
            ["vies.requestDate"] = "2026-11-24",
            ["vies.name"] = "John Doe",
            ["vies.address"] = "123 Main St, Anytown, UK",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["vies.requestDate"]);
    }

    [Theory]
    [InlineData("DE 123.456-789", "", "DE", "123456789")]
    [InlineData("123456789", "de", "DE", "123456789")]
    [InlineData("GR 094014201", "", "EL", "094014201")]
    [InlineData("094014201", "GR", "EL", "094014201")]
    [InlineData("EL094014201", "", "EL", "094014201")]
    [InlineData("IE6388047V", "FR", "IE", "6388047V")]
    [InlineData("XI123456789", "", "XI", "123456789")]
    public void NormalisesVatNumberAndCountry(string vatNumber, string countryCode, string expectedCountry, string expectedNumber)
    {
        var http = new FakeHttp(r => Ok(new { countryCode = (string)r.Body["countryCode"], vatNumber = (string)r.Body["vatNumber"], valid = true }));

        Run(http, Vocabularies(("organization.vat", vatNumber), ("organization.address.countryCode", countryCode)));

        var request = Assert.Single(http.Requests);
        Assert.Equal(expectedCountry, (string)request.Body["countryCode"]);
        Assert.Equal(expectedNumber, (string)request.Body["vatNumber"]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "DE")]
    [InlineData(" - ", "DE")]
    [InlineData("DE", "")]
    [InlineData("123456789", "")]
    [InlineData("123456789", "Denmark")]
    [InlineData("US123456789", "")]
    [InlineData("123456789", "US")]
    public void NoUsableEuVatNumber_ReturnsNoResultWithoutCallingVies(string vatNumber, string countryCode)
    {
        var http = new FakeHttp(_ => throw new InvalidOperationException("VIES must not be called"));
        var vocabularies = new List<PropertyDto>();
        if (vatNumber != null) vocabularies.Add(new PropertyDto { Key = "organization.vat", Value = vatNumber });
        if (countryCode != null) vocabularies.Add(new PropertyDto { Key = "organization.address.countryCode", Value = countryCode });

        var results = Run(http, vocabularies);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void InvalidNumber_StoresValidFalseAndDropsPlaceholderValues()
    {
        var http = new FakeHttp(_ => Ok(new
        {
            countryCode = "DE",
            vatNumber = "200",
            requestDate = "2026-11-24T03:42:54.489Z",
            valid = false,
            name = "---",
            address = "---",
        }));

        var result = Assert.Single(Run(http, Vocabularies(("organization.vat", "DE200"))));

        Assert.Equal("False", result.Data["vies.valid"].ToString());
        Assert.False(result.Data.ContainsKey("vies.name"));
        Assert.False(result.Data.ContainsKey("vies.address"));
        Assert.Equal("200", result.Data["vies.vatNumber"].ToString());
    }

    [Fact]
    public void MultiLineAddress_IsStoredOnOneLine()
    {
        var http = new FakeHttp(_ => Ok(new { countryCode = "IE", vatNumber = "6388047V", valid = true, name = "GOOGLE IRELAND LIMITED", address = "3RD FLOOR, GORDON HOUSE, BARROW STREET\nDUBLIN 4 \n" }));

        var result = Assert.Single(Run(http, Vocabularies(("organization.vat", "IE6388047V"))));

        Assert.Equal("3RD FLOOR, GORDON HOUSE, BARROW STREET, DUBLIN 4", result.Data["vies.address"].ToString());
    }

    [Theory]
    [InlineData("BadRequest", "{\"actionSucceed\":false,\"errorWrappers\":[{\"error\":\"INVALID_INPUT\",\"message\":\"The provided CountryCode is invalid or the VAT number is empty\"}]}")]
    [InlineData("InternalServerError", "{\"actionSucceed\":false,\"errorWrappers\":[{\"error\":\"MS_UNAVAILABLE\"}]}")]
    [InlineData("OK", "{\"actionSucceed\":false,\"errorWrappers\":[{\"error\":\"TIMEOUT\"}]}")]
    [InlineData("GatewayTimeout", "Gateway Timeout")]
    [InlineData("OK", "")]
    public void ViesCannotAnswer_ReturnsNoResultInsteadOfValidFalse(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => new ResponseDto { HttpStatus = httpStatus, Content = content });

        var results = Run(http, Vocabularies(("organization.vat", "DE100")));

        Assert.Empty(results);
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("InternalServerError", "{\"actionSucceed\":false,\"errorWrappers\":[{\"error\":\"MS_MAX_CONCURRENT_REQ\"}]}")]
    [InlineData("InternalServerError", "{\"actionSucceed\":false,\"errorWrappers\":[{\"error\":\"GLOBAL_MAX_CONCURRENT_REQ\"}]}")]
    [InlineData("TooManyRequests", "")]
    public void ViesBusy_ThrowsErrorTheEnricherRetries(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => new ResponseDto { HttpStatus = httpStatus, Content = content });

        var ex = Assert.Throws<JavaScriptException>(() => Run(http, Vocabularies(("organization.vat", "DE100"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameVatNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(new { countryCode = "DE", vatNumber = "100", valid = true, name = "John Doe" }));
        var cache = new FakeCache();

        var first = Run(http, Vocabularies(("organization.vat", "DE100")), cache);
        var second = Run(http, Vocabularies(("organization.vat", "de 100")), cache);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void FailedCheck_IsNotCached()
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls == 1
            ? new ResponseDto { HttpStatus = "InternalServerError", Content = "{\"errorWrappers\":[{\"error\":\"MS_UNAVAILABLE\"}]}" }
            : Ok(new { countryCode = "DE", vatNumber = "100", valid = true }));
        var cache = new FakeCache();

        Assert.Empty(Run(http, Vocabularies(("organization.vat", "DE100")), cache));
        Assert.Single(Run(http, Vocabularies(("organization.vat", "DE100")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingVies()
    {
        // VerifyConnectionV2 passes "DummyTestValue" for every configured key and allows only 2 seconds
        var http = new FakeHttp(_ => throw new InvalidOperationException("VIES must not be called"));

        var response = Execute(http,
            Vocabularies(("organization.vat", "DummyTestValue"), ("organization.address.countryCode", "DummyTestValue")),
            new FakeCache(),
            TimeSpan.FromSeconds(2));

        Assert.False(string.IsNullOrWhiteSpace(response));
        Assert.Empty(JsonConvert.DeserializeObject<ResultsDto[]>(response));
        Assert.Empty(http.Requests);
    }

    private static ResultsDto[] Run(FakeHttp http, List<PropertyDto> vocabularies, FakeCache cache = null)
    {
        var response = Execute(http, vocabularies, cache ?? new FakeCache(), TimeSpan.FromSeconds(120));

        // Same as RestApiExternalSearchProvider.InternalExecuteSearchV2
        return JsonConvert.DeserializeObject<ResultsDto[]>(response);
    }

    private static string Execute(FakeHttp http, List<PropertyDto> vocabularies, FakeCache cache, TimeSpan timeout)
    {
        // Same engine limits and globals as RestApiExternalSearchProvider.InternalExecuteSearchV2
        using var engine = new Engine(options =>
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

        engine.Execute(Script);

        return engine.GetValue("response").IsUndefined() ? string.Empty : engine.GetValue("response").ToString();
    }

    private static string ReadEmbeddedScript(string name)
    {
        using var stream = typeof(ViesProcessScriptTests).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<PropertyDto> Vocabularies(params (string Key, string Value)[] values)
        => values.Select(x => new PropertyDto { Key = x.Key, Value = x.Value }).ToList();

    private static ResponseDto Ok(object body) => new() { HttpStatus = "OK", Content = JsonConvert.SerializeObject(body) };

    private static Dictionary<string, string> Stringify(Dictionary<string, object> data)
        => data.ToDictionary(x => x.Key, x => x.Value?.ToString());

    public class SentRequest
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public List<HeaderDto> Headers { get; set; }
        public JObject Body { get; set; }
    }

    /// <summary>Stands in for ScriptHttpClient, reading the script's request object the same way it does.</summary>
    public class FakeHttp(Func<SentRequest, ResponseDto> handler)
    {
        public List<SentRequest> Requests { get; } = [];

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
