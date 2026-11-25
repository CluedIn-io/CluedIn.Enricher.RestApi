using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the CAC (Nigeria) sample Process Script (docs/samples/cac-nigeria) the same way the V2 REST
/// API enricher does, against a fake <c>http</c> object instead of the real VAS API.
/// </summary>
public class CacProcessScriptTests
{
    private const string Key = "organization.rcNumber";
    private const string Url = "https://vasapp.cac.gov.ng/api/vas/validation/company/rc";
    private const string ApiKey = "test-api-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.cac-process-script.js");

    // The "Get Company by RC Number" example response from CAC's VAS Validation API documentation
    private const string Company = """
        {
          "statusCode": 200,
          "status": "OK",
          "message": "company data",
          "data": {
            "rc_number": "7068861",
            "entity_name": "24 HOUSE OF GRAPHICS AND CAFE",
            "entity_type": "BUSINESS_NAME_FIRM",
            "address": " sultan Bello Road Unguwan Sarki Kaduna",
            "registration_date": "Wed Jul 26 12:32:04 GMT 2023",
            "entity_status": "ACTIVE",
            "line_of_business": [
              "Computer programming, consultancy and related activities"
            ]
          },
          "success": true,
          "count": 0
        }
        """;

    [Fact]
    public void RegisteredCompany_PostsRcNumberAndReturnsCacProperties()
    {
        var http = new FakeHttp(_ => Ok(Company));

        var results = Run(Script, http, Values((Key, "RC 7068861")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url, request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal(ApiKey, request.Header("X_API_KEY"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        Assert.Equal("7068861", (string)request.Body["rc_number"]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["cac.rcNumber"] = "7068861",
            ["cac.name"] = "24 HOUSE OF GRAPHICS AND CAFE",
            ["cac.entityType"] = "BUSINESS_NAME_FIRM",
            ["cac.status"] = "ACTIVE",
            ["cac.address"] = "sultan Bello Road Unguwan Sarki Kaduna",
            ["cac.registrationDate"] = "2023-07-26",
            ["cac.lineOfBusiness"] = "Computer programming, consultancy and related activities",
        }, Stringify(result.Data));
        Assert.IsType<string>(result.Data["cac.registrationDate"]);
    }

    [Fact]
    public void SeveralLinesOfBusinessAndSingleDigitDay_AreHandled()
    {
        var http = new FakeHttp(_ => Ok("""
            { "statusCode": 200, "data": { "rc_number": "80569782", "entity_name": "MyTest Ventures", "registration_date": "Thu Jul 2 13:50:56 GMT 2026",
              "line_of_business": ["GENERAL TRADING", " ", "Agriculture Forestry & Fishing"] } }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "80569782")), apiKey: ApiKey));

        Assert.Equal("2026-07-02", result.Data["cac.registrationDate"]);
        Assert.Equal("GENERAL TRADING; Agriculture Forestry & Fishing", result.Data["cac.lineOfBusiness"]);
        Assert.False(result.Data.ContainsKey("cac.status"));
    }

    [Theory]
    [InlineData("7068861", "7068861")]
    [InlineData("RC 7068861", "7068861")]
    [InlineData("rc-7068861", "7068861")]
    [InlineData("RC:7068861", "7068861")]
    [InlineData("BN 2345678", "2345678")]
    [InlineData("IT 12345", "12345")]
    [InlineData("0012345", "12345")]
    public void NormalisesRcNumber(string rcNumber, string expected)
    {
        var http = new FakeHttp(_ => Ok(Company));

        Run(Script, http, Values((Key, rcNumber)), apiKey: ApiKey);

        Assert.Equal(expected, (string)Assert.Single(http.Requests).Body["rc_number"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RC")]
    [InlineData("1234567890")]
    [InlineData("RC 12A45")]
    [InlineData("abc")]
    public void NoValidRcNumber_ReturnsNoResultWithoutCallingTheApi(string rcNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, rcNumber)), apiKey: ApiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "7068861")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("BadRequest", """{"statusCode":400,"status":"BAD_REQUEST","message":"invalid data","error":"company not found","success":false,"errors":["company not found"]}""")]
    [InlineData("BadRequest", """{"statusCode":400,"status":"BAD_REQUEST","message":"invalid data","error":"invalid rc_number passed","success":false,"errors":["invalid rc_number passed"]}""")]
    [InlineData("OK", """{"statusCode":200,"data":null,"success":true}""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "7068861")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((Key, "7068861")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiKeyRejected_ThrowsErrorThatIsNotRetried()
    {
        // The real 401 body, seen live
        var http = new FakeHttp(_ => Respond("Unauthorized", """{"timestamp":"2026-11-25T03:28:29.816Z","path":"/api/vas/validation/company/rc","status":401,"error":"Unauthorized","requestId":"f0cb6114-23797961"}"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "7068861")), apiKey: "wrong"));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void InsufficientBalance_ThrowsErrorThatSaysSo()
    {
        var http = new FakeHttp(_ => Respond("Forbidden", """{"statusCode":403,"status":"FORBIDDEN","message":"forbidden access","error":"insufficient balance to make this call.","success":false,"errors":null}"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "7068861")), apiKey: ApiKey));

        Assert.Contains("insufficient balance", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "7068861")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Company));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "7068861")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "RC 7068861")), cache, ApiKey);

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
