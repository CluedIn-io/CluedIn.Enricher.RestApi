using System;
using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the ACRA sample Process Script (docs/samples/acra-singapore) the same way the V2 REST API
/// enricher does, against a fake <c>http</c> object instead of the real data.gov.sg API.
/// </summary>
public class AcraProcessScriptTests
{
    private const string Key = "organization.uen";
    private const string Url = "https://data.gov.sg/api/action/datastore_search?resource_id=d_3f960c10fed6145404ca7b821f263b87&filters=";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.acra-process-script.js");

    // Shortened from a real response for 196800306E
    private const string Dbs = """
        {
          "success": true,
          "result": {
            "resource_id": "d_3f960c10fed6145404ca7b821f263b87",
            "records": [
              { "_id": 1383192, "uen": "196800306E", "issuance_agency_desc": "ACRA", "uen_status_desc": "Registered", "entity_name": "DBS BANK LTD.", "entity_type_desc": "Local Company", "uen_issue_date": "1968-07-16", "reg_street_name": "MARINA BOULEVARD", "reg_postal_code": "018982" }
            ],
            "total": 1,
            "limit": 1
          }
        }
        """;

    private const string NoRecords = """{ "success": true, "result": { "records": [], "total": 0, "limit": 1 } }""";

    private static string ExpectedUrl(string uen) => Url + Uri.EscapeDataString($"{{\"uen\":\"{uen}\"}}") + "&limit=1";

    [Fact]
    public void RegisteredUen_SearchesDatasetAndReturnsAcraProperties()
    {
        var http = new FakeHttp(_ => Ok(Dbs));

        var results = Run(Script, http, Values((Key, "196800306E")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(ExpectedUrl("196800306E"), request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Header("x-api-key"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["acra.uen"] = "196800306E",
            ["acra.name"] = "DBS BANK LTD.",
            ["acra.status"] = "Registered",
            ["acra.entityType"] = "Local Company",
            ["acra.registrationDate"] = "1968-07-16",
            ["acra.streetName"] = "MARINA BOULEVARD",
            ["acra.postalCode"] = "018982",
            ["acra.issuanceAgency"] = "ACRA",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["acra.registrationDate"]);
    }

    [Fact]
    public void ApiKeySet_IsSentInXApiKeyHeader()
    {
        var http = new FakeHttp(_ => Ok(Dbs));

        Run(Script, http, Values((Key, "196800306E")), apiKey: "my-data-gov-sg-key");

        Assert.Equal("my-data-gov-sg-key", Assert.Single(http.Requests).Header("x-api-key"));
    }

    [Theory]
    [InlineData("196800306E", "196800306E")]
    [InlineData("1968 00306 e", "196800306E")]
    [InlineData("53012345D", "53012345D")]
    [InlineData("t08-ll-0001a", "T08LL0001A")]
    public void NormalisesUen(string uen, string expected)
    {
        var http = new FakeHttp(_ => Ok(NoRecords));

        Run(Script, http, Values((Key, uen)));

        Assert.Equal(ExpectedUrl(expected), Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1968003060E")]
    [InlineData("X08LL0001A")]
    public void NoValidUen_ReturnsNoResultWithoutCallingTheApi(string uen)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, uen))));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", NoRecords)]
    [InlineData("OK", """{ "success": true, "result": { "records": [ { "uen": "196800307E", "entity_name": "OTHER" } ] } }""")]
    [InlineData("OK", """{ "success": false, "error": { "message": "Invalid filters" } }""")]
    [InlineData("NotFound", "404 Not Found")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void NotFoundOrApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "196800306E")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "196800306E")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void ApiKeyRejected_ThrowsErrorThatFailsTheRecord(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "196800306E")), apiKey: "wrong"));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void RateLimited_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "196800306E"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameUenAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Dbs));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "196800306E")), cache);
        var second = Run(Script, http, Values((Key, "1968 00306 e")), cache);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], "my-data-gov-sg-key");

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
