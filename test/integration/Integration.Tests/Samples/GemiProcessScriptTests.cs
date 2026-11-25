using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the GEMI Open Data sample Process Script (docs/samples/gemi-greece) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class GemiProcessScriptTests
{
    private const string GemiKey = "organization.gemiNumber";
    private const string AfmKey = "organization.afm";
    private const string Url = "https://opendata-api.businessportal.gr/api/opendata/v1/companies";
    private const string ApiKey = "test-api-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.gemi-process-script.js");

    // Follows the Company schema in the API's own specification (/api-docs); values are illustrative
    private const string Company = """
        {
          "arGemi": 121201000,
          "afm": "094019245",
          "coNameEl": "ΟΡΓΑΝΙΣΜΟΣ ΤΗΛΕΠΙΚΟΙΝΩΝΙΩΝ ΤΗΣ ΕΛΛΑΔΟΣ Α.Ε.",
          "coNamesEn": ["HELLENIC TELECOMMUNICATIONS ORGANIZATION S.A."],
          "coTitlesEl": ["ΟΤΕ Α.Ε.", "ΟΤΕ"],
          "coTitlesEn": ["OTE S.A."],
          "municipality": { "id": 1, "descr": "ΑΜΑΡΟΥΣΙΟΥ" },
          "prefecture": { "id": 2, "descr": "ΑΤΤΙΚΗΣ" },
          "city": "ΜΑΡΟΥΣΙ",
          "street": "ΚΗΦΙΣΙΑΣ",
          "streetNumber": "99",
          "zipCode": "15124",
          "url": "www.cosmote.gr",
          "email": "info@ote.gr",
          "isBranch": false,
          "legalType": { "id": 3, "descr": "Ανώνυμη Εταιρεία", "descrEn": "Societe Anonyme" },
          "gemiOffice": { "id": 4, "descr": "ΕΠΙΜΕΛΗΤΗΡΙΟ ΑΘΗΝΩΝ", "descrEn": "ATHENS CHAMBER" },
          "incorporationDate": "1949-04-23",
          "lastStatusChange": "26/05/2011",
          "status": { "id": 1, "descr": "Ενεργή", "descrEn": "Active", "isActive": true },
          "activities": [
            { "activity": { "id": "61.10", "descr": "Old activity" }, "type": "Δευτερεύουσα", "dtTo": "2010-01-01" },
            { "activity": { "id": "61.20", "descr": "Secondary" }, "type": "Δευτερεύουσα" },
            { "activity": { "id": "61.10.10", "descr": "Υπηρεσίες ενσύρματων τηλεπικοινωνιών" }, "type": "Κύρια" }
          ]
        }
        """;

    [Fact]
    public void GemiNumber_GetsCompanyAndReturnsGemiProperties()
    {
        var http = new FakeHttp(_ => Ok(Company));

        var results = Run(Script, http, Values((GemiKey, "000121201000")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "/121201000", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header("api_key"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["gemi.gemiNumber"] = "121201000",
            ["gemi.afm"] = "094019245",
            ["gemi.name"] = "ΟΡΓΑΝΙΣΜΟΣ ΤΗΛΕΠΙΚΟΙΝΩΝΙΩΝ ΤΗΣ ΕΛΛΑΔΟΣ Α.Ε.",
            ["gemi.nameEn"] = "HELLENIC TELECOMMUNICATIONS ORGANIZATION S.A.",
            ["gemi.distinctiveTitle"] = "ΟΤΕ Α.Ε.; ΟΤΕ",
            ["gemi.status"] = "Active",
            ["gemi.active"] = "True",
            ["gemi.legalForm"] = "Societe Anonyme",
            ["gemi.gemiOffice"] = "ATHENS CHAMBER",
            ["gemi.address"] = "ΚΗΦΙΣΙΑΣ 99, 15124 ΜΑΡΟΥΣΙ",
            ["gemi.municipality"] = "ΑΜΑΡΟΥΣΙΟΥ",
            ["gemi.prefecture"] = "ΑΤΤΙΚΗΣ",
            ["gemi.website"] = "www.cosmote.gr",
            ["gemi.email"] = "info@ote.gr",
            ["gemi.incorporationDate"] = "1949-04-23",
            ["gemi.lastStatusChange"] = "2011-05-26",
            ["gemi.isBranch"] = "False",
            ["gemi.mainActivityCode"] = "61.10.10",
            ["gemi.mainActivity"] = "Υπηρεσίες ενσύρματων τηλεπικοινωνιών",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["gemi.incorporationDate"]);
    }

    [Theory]
    [InlineData("121201000", "121201000")]
    [InlineData("000121201000", "121201000")]
    [InlineData("121 201 000", "121201000")]
    [InlineData("1212010-00", "121201000")]
    public void NormalisesGemiNumber(string input, string expected)
    {
        var http = new FakeHttp(_ => Ok(Company));

        Run(Script, http, Values((GemiKey, input)), apiKey: ApiKey);

        Assert.Equal($"{Url}/{expected}", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData("094019245")]
    [InlineData("EL094019245")]
    [InlineData("GR 094 019 245")]
    [InlineData("94019245")]
    public void AfmWithoutGemiNumber_SearchesByAfmAndPrefersTheHeadOffice(string afm)
    {
        var http = new FakeHttp(_ => Ok($$"""
            { "searchMetadata": { "totalCount": 2 }, "searchResults": [
              { "arGemi": 555000, "afm": "094019245", "coNameEl": "ΥΠΟΚΑΤΑΣΤΗΜΑ", "isBranch": true },
              {{Company}}
            ] }
            """));

        var result = Assert.Single(Run(Script, http, Values((AfmKey, afm)), apiKey: ApiKey));

        Assert.Equal($"{Url}?afm=094019245&resultsSize=10", Assert.Single(http.Requests).Url);
        Assert.Equal("121201000", result.Data["gemi.gemiNumber"].ToString());
    }

    [Fact]
    public void GemiNumberWins_OverAfm()
    {
        var http = new FakeHttp(_ => Ok(Company));

        Run(Script, http, Values((GemiKey, "121201000"), (AfmKey, "094019245")), apiKey: ApiKey);

        Assert.Equal(Url + "/121201000", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("12345", null)]          // too short for a GEMI number
    [InlineData("1234567890123", null)]  // too long
    [InlineData("ABC123456", null)]
    [InlineData(null, "094019246")]      // wrong AFM check digit
    [InlineData(null, "000000000")]
    [InlineData(null, "DE123456789")]
    public void NoValidIdentifier_ReturnsNoResultWithoutCallingTheApi(string gemiNumber, string afm)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((GemiKey, gemiNumber), (AfmKey, afm)), apiKey: ApiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void MissingApiKey_ReturnsNoResultWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((GemiKey, "121201000")), apiKey: ""));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Unauthorized", """{ "message": "Unauthorized", "request_id": "x" }""")]
    [InlineData("Unauthorized", """{ "message": "No API key found in request", "request_id": "x" }""")]
    [InlineData("Forbidden", "")]
    public void RejectedKey_FailsTheRecordWithoutRetrying(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((GemiKey, "121201000")), apiKey: ApiKey));

        Assert.Contains("api_key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("NotFound", "")]
    [InlineData("BadRequest", """[{ "code": "1", "message": "bad arGemi" }]""")]
    [InlineData("InternalServerError", """[{ "code": "500", "message": "error" }]""")]
    [InlineData("ServiceUnavailable", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndCachesNothing(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((GemiKey, "121201000")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((GemiKey, "121201000")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void AfmNotFound_ReturnsNoResult()
    {
        var http = new FakeHttp(_ => Ok("""{ "searchMetadata": { "totalCount": 0 }, "searchResults": [] }"""));

        Assert.Empty(Run(Script, http, Values((AfmKey, "094019245")), apiKey: ApiKey));
    }

    [Fact]
    public void RateLimited_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", """{ "message": "API rate limit exceeded" }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((GemiKey, "121201000")), apiKey: ApiKey));

        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameCompanyAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Company));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((GemiKey, "121201000")), cache, ApiKey);
        var second = Run(Script, http, Values((GemiKey, "000121201000")), cache, ApiKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [GemiKey, AfmKey], ApiKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
