using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Estonian e-Business Register sample Process Script (docs/samples/e-business-register-estonia)
/// the same way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real service.
/// </summary>
public class AriregisterProcessScriptTests
{
    private const string Key = "organization.registryCode";
    private const string Url = "https://ariregister.rik.ee/eng/api/autocomplete?q=";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.ariregister-process-script.js");

    // Real response for 10137319
    private const string TallinnaSadam = """
        {"status": "OK", "data": [{"company_id": 2000004900, "reg_code": 10137319, "name": "aktsiaselts TALLINNA SADAM", "historical_names": [], "status": "R", "legal_address": "Harju maakond, Tallinn, Kesklinna linnaosa, Sadama tn 25", "zip_code": "15051", "legal_form": "1", "url": "https://ariregister.rik.ee/eng/company/10137319/aktsiaselts-TALLINNA-SADAM"}]}
        """;

    [Fact]
    public void RegisteredCompany_SearchesByRegistryCodeAndReturnsAriregisterProperties()
    {
        var http = new FakeHttp(_ => Ok(TallinnaSadam));

        var results = Run(Script, http, Values((Key, "10137319")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "10137319", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["ariregister.registryCode"] = "10137319",
            ["ariregister.name"] = "aktsiaselts TALLINNA SADAM",
            ["ariregister.statusCode"] = "R",
            ["ariregister.status"] = "Registered",
            ["ariregister.legalAddress"] = "Harju maakond, Tallinn, Kesklinna linnaosa, Sadama tn 25",
            ["ariregister.postalCode"] = "15051",
            ["ariregister.legalFormCode"] = "1",
            ["ariregister.url"] = "https://ariregister.rik.ee/eng/company/10137319/aktsiaselts-TALLINNA-SADAM",
        }, Stringify(result.Data));
    }

    [Fact]
    public void SeveralMatches_PicksTheCompanyWithTheSameRegistryCode()
    {
        // The search also matches names and the start of other registry codes
        var http = new FakeHttp(_ => Ok("""
            {"status": "OK", "data": [
              {"reg_code": 16405561, "name": "101 Lammast UÜ", "historical_names": ["Grematamera UÜ"], "status": "R", "legal_address": "Tartu maakond, Tartu linn, Pihva küla, Kurvitsa tee 22", "zip_code": "61407", "legal_form": "4"},
              {"reg_code": 10100027, "name": "MERKONI TÖÖRÕIVA OSAÜHING", "historical_names": [], "status": "L", "legal_address": "Harju maakond, Harku vald", "zip_code": "", "legal_form": "5"}
            ]}
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "10100027"))));

        Assert.Equal(new Dictionary<string, string>
        {
            ["ariregister.registryCode"] = "10100027",
            ["ariregister.name"] = "MERKONI TÖÖRÕIVA OSAÜHING",
            ["ariregister.statusCode"] = "L",
            ["ariregister.status"] = "In liquidation",
            ["ariregister.legalAddress"] = "Harju maakond, Harku vald",
            ["ariregister.legalFormCode"] = "5",
        }, Stringify(result.Data));
    }

    [Fact]
    public void PreviousNamesAndUnknownStatusCode_AreStoredAsReturned()
    {
        var http = new FakeHttp(_ => Ok("""
            {"status": "OK", "data": [{"reg_code": 16405561, "name": "101 Lammast UÜ", "historical_names": ["Grematamera UÜ", "Old Name UÜ"], "status": "N", "legal_address": "Tartu maakond", "zip_code": "61407", "legal_form": "4"}]}
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "16405561"))));

        Assert.Equal("Grematamera UÜ; Old Name UÜ", result.Data["ariregister.previousNames"].ToString());
        Assert.Equal("N", result.Data["ariregister.statusCode"].ToString());
        Assert.False(result.Data.ContainsKey("ariregister.status"));
    }

    [Theory]
    [InlineData("10137319")]
    [InlineData("10 137 319")]
    [InlineData("EE10137319")]
    [InlineData("ee 10137319")]
    public void NormalisesRegistryCode(string registryCode)
    {
        var http = new FakeHttp(_ => Ok(TallinnaSadam));

        Run(Script, http, Values((Key, registryCode)));

        Assert.Equal(Url + "10137319", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1013731")]
    [InlineData("101373190")]
    [InlineData("Tallinna Sadam")]
    [InlineData("EE100000000")] // an Estonian VAT number, not a registry code
    public void NoValidRegistryCode_ReturnsNoResultWithoutCallingTheService(string registryCode)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, registryCode)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{"status": "OK", "data": []}""")]
    [InlineData("InternalServerError", "")]
    [InlineData("ServiceUnavailable", "error code: 1015")]
    [InlineData("OK", "not json")]
    public void NotFoundOrServiceCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "10137319")), cache));
        Assert.Single(http.Requests);

        Assert.Empty(Run(Script, http, Values((Key, "10137319")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ServiceBusy_ThrowsErrorTheEnricherRetries()
    {
        // The service answers HTTP 429 "error code: 1015" when it is rate limiting
        var http = new FakeHttp(_ => Respond("TooManyRequests", "error code: 1015"));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "10137319"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameRegistryCodeAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(TallinnaSadam));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "10137319")), cache);
        var second = Run(Script, http, Values((Key, "EE 10137319")), cache);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheService()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key]);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
