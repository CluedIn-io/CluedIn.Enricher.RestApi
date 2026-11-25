using System.Collections.Generic;
using System.Linq;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Philippine SEC sample Process Script (docs/samples/sec-philippines) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class SecphProcessScriptTests
{
    private const string Key = "organization.secRegistrationNumber";
    private const string ApiUrl = "https://gwwso2.sec.gov.ph/secnumber/1.0.0/client_sec_no_status.php";
    private const string TokenUrl = "https://portal.sec.gov.ph/oauth2/token";
    private const string ConsumerKeyAndSecret = "consumer-key:consumer-secret";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.secph-process-script.js");

    // Shaped like the response in the SEC's "Using the SEC API Marketplace" guide; the values are made up
    private const string Company = """
        {
          "success": true,
          "data": [{
            "sec_no": "A199600179",
            "company_name": "EXAMPLE HOLDINGS, INC.",
            "date_approved": "1996-01-15",
            "licenses": "None",
            "status": "Registered"
          }]
        }
        """;

    /// <summary>Answers the token endpoint with a new token each time, and the API with <paramref name="api"/>.</summary>
    private static FakeHttp Http(System.Func<SentRequest, ResponseDto> api)
    {
        var tokens = 0;
        return new FakeHttp(r => r.Url == TokenUrl
            ? Ok($$"""{ "access_token": "token-{{++tokens}}", "token_type": "Bearer", "expires_in": 3600 }""")
            : api(r));
    }

    [Fact]
    public void ConsumerKeyAndSecret_GetsTokenThenLooksUpCompany()
    {
        var http = Http(_ => Ok(Company));

        var results = Run(Script, http, Values((Key, "A199600179")), apiKey: ConsumerKeyAndSecret);

        Assert.Equal(2, http.Requests.Count);
        var tokenRequest = http.Requests[0];
        Assert.Equal(TokenUrl, tokenRequest.Url);
        Assert.Equal("POST", tokenRequest.Method);
        Assert.Equal("application/x-www-form-urlencoded", tokenRequest.Header("Content-Type"));
        Assert.Equal("grant_type=client_credentials&client_id=consumer-key&client_secret=consumer-secret&scope=openid", (string)tokenRequest.Body);

        var apiRequest = http.Requests[1];
        Assert.Equal($"{ApiUrl}?sec_no=A199600179", apiRequest.Url);
        Assert.Equal("GET", apiRequest.Method);
        Assert.Equal("Bearer token-1", apiRequest.Header("Authorization"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["secph.secNumber"] = "A199600179",
            ["secph.name"] = "EXAMPLE HOLDINGS, INC.",
            ["secph.dateApproved"] = "1996-01-15",
            ["secph.status"] = "Registered",
            ["secph.licenses"] = "None",
        }, Stringify(result.Data));
    }

    [Fact]
    public void AccessToken_IsUsedDirectlyWithoutCallingTheTokenEndpoint()
    {
        var http = new FakeHttp(_ => Ok(Company));

        Assert.Single(Run(Script, http, Values((Key, "A199600179")), apiKey: "my-access-token"));

        var request = Assert.Single(http.Requests);
        Assert.Equal("Bearer my-access-token", request.Header("Authorization"));
    }

    [Fact]
    public void Token_IsReusedForTheNextRecord()
    {
        var http = Http(r => Ok(Company.Replace("A199600179", r.Url.Split('=')[1])));
        var cache = new FakeCache();

        Run(Script, http, Values((Key, "A199600179")), cache, ConsumerKeyAndSecret);
        Run(Script, http, Values((Key, "CS201912345")), cache, ConsumerKeyAndSecret);

        Assert.Single(http.Requests, r => r.Url == TokenUrl);
        Assert.All(http.Requests.Where(r => r.Url != TokenUrl), r => Assert.Equal("Bearer token-1", r.Header("Authorization")));
    }

    [Fact]
    public void ExpiredCachedToken_IsReplacedOnce()
    {
        var http = Http(r => r.Header("Authorization") == "Bearer token-1" ? Respond("Unauthorized", """{ "code": "900901", "message": "Invalid Credentials" }""") : Ok(Company));

        var result = Assert.Single(Run(Script, http, Values((Key, "A199600179")), apiKey: ConsumerKeyAndSecret));

        Assert.Equal("EXAMPLE HOLDINGS, INC.", result.Data["secph.name"].ToString());
        Assert.Equal(new[] { TokenUrl, ApiUrl, TokenUrl, ApiUrl }, http.Requests.Select(r => r.Url.Split('?')[0]));
        Assert.Equal("Bearer token-2", http.Requests[3].Header("Authorization"));
    }

    [Theory]
    [InlineData("A199600179", "A199600179")]
    [InlineData("a1996-00179", "A199600179")]
    [InlineData("CS 2019 12345", "CS201912345")]
    [InlineData(" PG200900123 ", "PG200900123")]
    [InlineData("0000012345", "0000012345")]
    public void NormalisesSecNumber(string number, string expected)
    {
        var http = new FakeHttp(_ => Ok(Company.Replace("A199600179", expected)));

        Run(Script, http, Values((Key, number)), apiKey: "my-access-token");

        Assert.Equal($"{ApiUrl}?sec_no={expected}", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A123")]
    [InlineData("ABCDE12345")]
    [InlineData("12345678901234")]
    public void NoValidSecNumber_ReturnsNoResultWithoutCallingTheApi(string number)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, number)), apiKey: ConsumerKeyAndSecret));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void NoCredentials_ReturnsNoResultWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "A199600179")), apiKey: ""));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Unauthorized", """{ "error": "invalid_client", "error_description": "Client Authentication failed." }""")]
    [InlineData("BadRequest", """{ "error": "invalid_request" }""")]
    public void RejectedConsumerKeyAndSecret_FailsTheRecord(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "A199600179")), apiKey: ConsumerKeyAndSecret));

        Assert.Contains("rejected the consumer key and secret", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("Unauthorized", """{ "code": "900901", "message": "Invalid Credentials", "description": "Invalid Credentials. Make sure you have provided the correct security credentials" }""")]
    [InlineData("Forbidden", """{ "code": "900908", "message": "Resource forbidden ", "description": "User is NOT authorized to access the Resource. API Subscription validation failed." }""")]
    public void RejectedAccessToken_FailsTheRecord(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "A199600179")), apiKey: "my-access-token"));

        Assert.Contains("rejected the credentials", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", """{ "success": true, "data": [] }""")]
    [InlineData("OK", """{ "success": false, "message": "No record found" }""")]
    [InlineData("OK", """{ "success": true, "data": [{ "sec_no": "CS201912345", "company_name": "OTHER" }] }""")]
    [InlineData("Forbidden", "<!DOCTYPE html><html><head><title>SEC Website Maintenance</title></head></html>")]
    [InlineData("InternalServerError", "error")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "A199600179")), cache, "my-access-token"));
        Assert.Empty(Run(Script, http, Values((Key, "A199600179")), cache, "my-access-token"));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void TokenEndpointUnavailable_ReturnsNoResult()
    {
        var http = new FakeHttp(_ => Respond("Forbidden", "<!DOCTYPE html><html><head><title>SEC Website Maintenance</title></head></html>"));

        Assert.Empty(Run(Script, http, Values((Key, "A199600179")), apiKey: ConsumerKeyAndSecret));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("TooManyRequests", "")]
    public void ApiBusy_ThrowsErrorTheEnricherRetries(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "A199600179")), apiKey: "my-access-token"));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("01/15/1996", "1996-01-15")]
    [InlineData("1/5/1996", "1996-01-05")]
    [InlineData("1996-01-15 00:00:00", "1996-01-15")]
    [InlineData("January 15, 1996", "January 15, 1996")]
    public void DateApproved_IsStoredAsDateOnly(string dateApproved, string expected)
    {
        var http = new FakeHttp(_ => Ok(Company.Replace("1996-01-15", dateApproved)));

        var result = Assert.Single(Run(Script, http, Values((Key, "A199600179")), apiKey: "my-access-token"));

        Assert.Equal(expected, result.Data["secph.dateApproved"].ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Company));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "A199600179")), cache, "my-access-token");
        var second = Run(Script, http, Values((Key, "a1996-00179")), cache, "my-access-token");

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], ConsumerKeyAndSecret);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
