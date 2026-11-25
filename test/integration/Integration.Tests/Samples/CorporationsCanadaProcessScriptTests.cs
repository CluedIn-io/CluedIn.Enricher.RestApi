using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Corporations Canada sample Process Script (docs/samples/corporations-canada) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class CorporationsCanadaProcessScriptTests
{
    private const string CorporationNumberKey = "organization.canadaCorporationNumber";
    private const string BusinessNumberKey = "organization.canadaBusinessNumber";
    private const string ApiKey = "my-ised-api-key";
    private const string Url = "https://apigateway-passerelledapi.ised-isde.canada.ca/corporations/api/v1/corporations/";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.corporations-canada-process-script.js");

    // The English example response from the API's OpenAPI specification
    private const string Abbotsford = """
        [
          {
            "corporationId": "1007",
            "act": "Boards of Trade Act - Part II",
            "status": "Active",
            "corporationNames": [
              { "CorporationName": { "name": "ABBOTSFORD-MATSQUI CHAMBER OF COMMERCE", "nameType": "Primary", "current": false, "effectiveDate": "1991-12-27", "expiryDate": "1995-02-06" } },
              { "CorporationName": { "name": "Abbotsford Chamber of Commerce", "nameType": "Primary", "current": true, "effectiveDate": "1995-02-06" } }
            ],
            "adresses": [
              { "address": { "addressLine": ["207 - 32900 SOUTH FRASER WAY"], "city": "ABBOTSFORD", "postalCode": "V2S 5A1", "provinceCode": "BC", "countryCode": "CA" } }
            ],
            "directorLimits": { "minimum": 3, "maximum": 30 },
            "businessNumbers": { "businessNumber": "106679285" },
            "annualReturns": [
              { "annualReturn": { "annualMeetingdate": "2018-03-15", "yearOfFiling": "2018" } },
              { "annualReturn": { "annualMeetingdate": "2019-03-27", "yearOfFiling": "2019" } }
            ],
            "activities": [
              { "activity": { "activity": "Incorporation", "date": "1947-01-10" } },
              { "activity": { "activity": "Ordinary By-Laws Filing", "date": "2005-12-20" } }
            ]
          }
        ]
        """;

    [Fact]
    public void FederalCorporation_CallsApiWithUserKeyAndReturnsCorporationsCanadaProperties()
    {
        var http = new FakeHttp(_ => Ok(Abbotsford));

        var results = Run(Script, http, Values((CorporationNumberKey, "000100-7")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "1007.json?lang=eng", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header("user-key"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["corporationscanada.corporationNumber"] = "1007",
            ["corporationscanada.name"] = "Abbotsford Chamber of Commerce",
            ["corporationscanada.status"] = "Active",
            ["corporationscanada.act"] = "Boards of Trade Act - Part II",
            ["corporationscanada.businessNumber"] = "106679285",
            ["corporationscanada.address"] = "207 - 32900 SOUTH FRASER WAY, ABBOTSFORD, BC, V2S 5A1",
            ["corporationscanada.city"] = "ABBOTSFORD",
            ["corporationscanada.province"] = "BC",
            ["corporationscanada.postalCode"] = "V2S 5A1",
            ["corporationscanada.incorporationDate"] = "1947-01-10",
            ["corporationscanada.lastAnnualReturn"] = "2019",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["corporationscanada.incorporationDate"]);
    }

    [Theory]
    [InlineData("000100-7", null, "1007")]
    [InlineData("1007", null, "1007")]
    [InlineData("123 456 7", "106679285", "1234567")] // corporation number wins
    [InlineData(null, "106679285", "106679285")]
    [InlineData("", "106 679 285", "106679285")]
    [InlineData(null, "106679285RC0001", "106679285")]
    [InlineData("not a number", "106679285rc0001", "106679285")]
    public void UsesCorporationNumberOrElseBusinessNumber(string corporationNumber, string businessNumber, string expected)
    {
        var http = new FakeHttp(_ => Ok(Abbotsford));

        Run(Script, http, Values((CorporationNumberKey, corporationNumber), (BusinessNumberKey, businessNumber)), apiKey: ApiKey);

        Assert.Equal(Url + expected + ".json?lang=eng", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("12345678", null)] // too long for a corporation number
    [InlineData("0000000", null)]
    [InlineData(null, "106679286")] // fails the check digit
    [InlineData(null, "10667928")]
    [InlineData(null, "106679285RC01")]
    public void NoValidNumber_ReturnsNoResultWithoutCallingTheApi(string corporationNumber, string businessNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((CorporationNumberKey, corporationNumber), (BusinessNumberKey, businessNumber)), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((CorporationNumberKey, "1007")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Forbidden", "Authentication failed")]
    [InlineData("Unauthorized", "")]
    public void ApiKeyRejected_ThrowsErrorThatFailsTheRecord(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((CorporationNumberKey, "1007")), apiKey: ApiKey));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    // A corporation that isn't found returns OK with messages instead of a corporation
    [InlineData("OK", """["could not find corporation 123","Corporation 123 est inconnu."]""")]
    [InlineData("OK", "[]")]
    [InlineData("NotFound", "")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void NotFoundOrApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((CorporationNumberKey, "123")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((CorporationNumberKey, "123")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void RateLimited_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((CorporationNumberKey, "1007")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Abbotsford));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((CorporationNumberKey, "1007")), cache, ApiKey);
        var second = Run(Script, http, Values((CorporationNumberKey, "000100-7")), cache, ApiKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [CorporationNumberKey, BusinessNumberKey], ApiKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
