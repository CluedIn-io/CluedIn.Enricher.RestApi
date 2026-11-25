using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the CIPC sample Process Script (docs/samples/cipc-south-africa) the same way the V2 REST
/// API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class CipcProcessScriptTests
{
    private const string Key = "organization.registrationNumber";
    private const string Url = "https://apim.cipc.co.za/companies-api/v1/information";
    private const string ApiKey = "test-subscription-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.cipc-process-script.js");

    // Shaped like the response schema published on the CIPC APIVerse Hub; the values are made up
    private const string Example = """
        {
          "Enterprise": [{
            "enterprise_number": "2020/939681/07",
            "country_code": "ZA",
            "enterprise_name": "EXAMPLE TRADING (PTY) LTD",
            "enterprise_type_description": "Private Company",
            "sic_description": "Retail trade",
            "registration_date": "2020-11-23T00:00:00",
            "enterprise_status_description": "In Business",
            "financial_year_end": "February",
            "financial_year_effective_date": null,
            "physical_address_1": "1 Main Road",
            "physical_address_2": "Sandton",
            "physical_address_3": "Johannesburg",
            "physical_address_4": "",
            "physical_code": "2196",
            "postal_address_1": "PO Box 1",
            "postal_address_2": "Sandton",
            "postal_address_3": "",
            "postal_address_4": "",
            "postal_code": "2146",
            "tax_number": "9000000000",
            "business_start_date": "2020/11/23"
          }]
        }
        """;

    [Fact]
    public void KnownEnterprise_PostsNumberAndReturnsCipcProperties()
    {
        var http = new FakeHttp(_ => Ok(Example));

        var results = Run(Script, http, Values((Key, "2020/939681/07")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url, request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal(ApiKey, request.Header("Ocp-Apim-Subscription-Key"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        Assert.Equal("2020/939681/07", (string)request.Body["enterprise_number"]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["cipc.enterpriseNumber"] = "2020/939681/07",
            ["cipc.name"] = "EXAMPLE TRADING (PTY) LTD",
            ["cipc.enterpriseType"] = "Private Company",
            ["cipc.status"] = "In Business",
            ["cipc.industry"] = "Retail trade",
            ["cipc.registrationDate"] = "2020-11-23",
            ["cipc.businessStartDate"] = "2020-11-23",
            ["cipc.financialYearEnd"] = "February",
            ["cipc.physicalAddress"] = "1 Main Road, Sandton, Johannesburg, 2196",
            ["cipc.postalAddress"] = "PO Box 1, Sandton, 2146",
            ["cipc.countryCode"] = "ZA",
            ["cipc.taxNumber"] = "9000000000",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["cipc.registrationDate"]);
    }

    [Theory]
    [InlineData("2020/939681/07")]
    [InlineData("2020-939681-07")]
    [InlineData("2020 939681 07")]
    [InlineData("202093968107")]
    [InlineData(" 2020/939681/07 ")]
    [InlineData("K2020/939681/07")]
    public void NormalisesEnterpriseNumber(string number)
    {
        var http = new FakeHttp(_ => Ok(Example));

        Run(Script, http, Values((Key, number)), apiKey: ApiKey);

        Assert.Equal("2020/939681/07", (string)Assert.Single(http.Requests).Body["enterprise_number"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2020/939681")]
    [InlineData("1720/939681/07")]
    [InlineData("4000123456")]
    [InlineData("ABC2020/939681/07")]
    public void NoValidEnterpriseNumber_ReturnsNoResultWithoutCallingTheApi(string number)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, number)), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void NoKey_ReturnsNoResultWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "2020/939681/07")), apiKey: ""));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Unauthorized", """{ "statusCode": 401, "message": "Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription." }""")]
    [InlineData("Forbidden", "")]
    public void RejectedKey_FailsTheRecordWithoutRetrying(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "2020/939681/07")), apiKey: ApiKey));

        Assert.Contains("rejected the subscription key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", """{ "Enterprise": [] }""")]
    [InlineData("OK", """{ "Enterprise": [{ "enterprise_number": "2019/000001/07", "enterprise_name": "OTHER" }] }""")]
    [InlineData("OK", "")]
    [InlineData("NotFound", """{ "statusCode": 404, "message": "Resource not found" }""")]
    [InlineData("InternalServerError", "error")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "2020/939681/07")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((Key, "2020/939681/07")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void SingleEnterpriseObject_IsAccepted()
    {
        var http = new FakeHttp(_ => Ok("""{ "Enterprise": { "enterprise_number": "2020/939681/07", "enterprise_name": "EXAMPLE TRADING (PTY) LTD" } }"""));

        var result = Assert.Single(Run(Script, http, Values((Key, "2020/939681/07")), apiKey: ApiKey));

        Assert.Equal("EXAMPLE TRADING (PTY) LTD", result.Data["cipc.name"].ToString());
    }

    [Fact]
    public void RateLimitReached_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", """{ "statusCode": 429, "message": "Rate limit is exceeded." }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "2020/939681/07")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Example));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "2020/939681/07")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "202093968107")), cache, ApiKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], ApiKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
