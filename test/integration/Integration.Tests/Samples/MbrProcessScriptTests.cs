using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Malta Business Registry sample Process Script (docs/samples/mbr-malta) the same way the
/// V2 REST API enricher does, against a fake <c>http</c> object. The published script is a
/// template with MBR_URL and API_KEY_HEADER empty; most tests fill them in the way a user would.
/// </summary>
public class MbrProcessScriptTests
{
    private const string Key = "organization.registrationNumber";
    private const string ApiKey = "test-api-key";
    private const string TestUrl = "https://mbr.example/companies/{registrationNumber}/basic";
    private const string TestHeader = "X-Subscription-Key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Template = ReadEmbeddedScript("Samples.mbr-process-script.js");

    private static readonly string Script = Configure(TestUrl, TestHeader);

    // Illustrative Basic Company Details answer, with the fields MBR lists for the package
    private const string Company = """
        {
          "registrationNumber": "C 12345",
          "companyName": "EXAMPLE HOLDINGS LIMITED",
          "registrationDate": "2010-05-12T00:00:00",
          "registeredAddress": { "line1": "1, Example Street", "locality": "Valletta", "postCode": "VLT 1000" },
          "state": "Active"
        }
        """;

    private static string Configure(string url, string header)
    {
        var script = Template.Replace("const MBR_URL = \"\";", $"const MBR_URL = \"{url}\";")
                             .Replace("const API_KEY_HEADER = \"\";", $"const API_KEY_HEADER = \"{header}\";");
        Assert.NotEqual(Template, script);
        return script;
    }

    [Fact]
    public void PublishedTemplate_IsNotConfiguredAndMakesNoCall()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Template, http, Values((Key, "C 12345")), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void Company_GetsBasicDetailsAndReturnsMbrProperties()
    {
        var http = new FakeHttp(_ => Ok(Company));

        var results = Run(Script, http, Values((Key, "c12345")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://mbr.example/companies/C%2012345/basic", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header(TestHeader));
        Assert.False(string.IsNullOrWhiteSpace(request.Header("User-Agent")));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["mbr.registrationNumber"] = "C 12345",
            ["mbr.name"] = "EXAMPLE HOLDINGS LIMITED",
            ["mbr.registrationDate"] = "2010-05-12",
            ["mbr.registeredAddress"] = "1, Example Street, Valletta, VLT 1000",
            ["mbr.state"] = "Active",
        }, Stringify(result.Data));
    }

    [Fact]
    public void OtherFieldNamesAndWrappers_AreRead()
    {
        var http = new FakeHttp(_ => Ok("""
            { "data": [ { "CompanyName": "EXAMPLE LTD", "Status": "Struck Off", "Address": "1, Example Street, Sliema" } ] }
            """));
        var script = Configure("https://mbr.example/basic?regNo=", TestHeader);

        var result = Assert.Single(Run(script, http, Values((Key, "OC 987")), apiKey: ApiKey));

        Assert.Equal("https://mbr.example/basic?regNo=OC%20987", Assert.Single(http.Requests).Url);
        Assert.Equal(new Dictionary<string, string>
        {
            ["mbr.registrationNumber"] = "OC 987",
            ["mbr.name"] = "EXAMPLE LTD",
            ["mbr.registeredAddress"] = "1, Example Street, Sliema",
            ["mbr.state"] = "Struck Off",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("C 12345", "C%2012345")]
    [InlineData("c12345", "C%2012345")]
    [InlineData("C-12345", "C%2012345")]
    [InlineData("12345", "C%2012345")]
    [InlineData("p 123", "P%20123")]
    [InlineData("OC 1234", "OC%201234")]
    public void NormalisesRegistrationNumber(string registrationNumber, string expected)
    {
        var http = new FakeHttp(_ => Ok(Company));

        Run(Script, http, Values((Key, registrationNumber)), apiKey: ApiKey);

        Assert.Equal($"https://mbr.example/companies/{expected}/basic", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("C 1234567")]
    [InlineData("ABCD 123")]
    [InlineData("MT12345678")]
    public void NoValidRegistrationNumber_ReturnsNoResultWithoutCallingTheApi(string registrationNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, registrationNumber)), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "C 12345")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("NotFound", "")]
    [InlineData("OK", "[]")]
    [InlineData("OK", """{ "data": null }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "C 12345")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((Key, "C 12345")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void ApiKeyRejected_ThrowsErrorThatIsNotRetried(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "C 12345")), apiKey: "wrong"));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "C 12345")), apiKey: ApiKey));

        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Company));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "C 12345")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "c-12345")), cache, ApiKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi(bool configured)
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(configured ? Script : Template, http, [Key], ApiKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
