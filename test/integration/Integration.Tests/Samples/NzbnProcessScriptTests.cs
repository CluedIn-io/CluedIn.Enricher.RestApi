using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the NZBN sample Process Script (docs/samples/nzbn-new-zealand) the same way the V2 REST
/// API enricher does, against a fake <c>http</c> object instead of the real NZBN API.
/// </summary>
public class NzbnProcessScriptTests
{
    private const string Key = "organization.nzbn";
    private const string SubscriptionKey = "my-subscription-key";
    private const string Url = "https://api.business.govt.nz/gateway/nzbn/v5/entities/";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.nzbn-process-script.js");

    // Shortened from a real response for 9429040402543
    private const string AirNewZealand = """
        {
          "entityStatusCode": "50",
          "entityName": "AIR NEW ZEALAND LIMITED",
          "nzbn": "9429040402543",
          "entityTypeCode": "LTD",
          "entityTypeDescription": "NZ Limited Company",
          "entityStatusDescription": "Registered",
          "registrationDate": "1940-04-26T00:00:00.000+1200",
          "sourceRegister": "COMPANY",
          "sourceRegisterUniqueIdentifier": "104799",
          "addresses": {
            "links": [],
            "addressList": [
              { "startDate": "2020-03-11T10:52:50.000+1300", "endDate": null, "careOf": "Company Secretary", "address1": "Private Bag 92007", "address2": "Victoria Street West", "address3": "Auckland", "address4": null, "postCode": "1142", "countryCode": "NZ", "addressType": "POSTAL" },
              { "startDate": "2010-01-01T00:00:00.000+1300", "endDate": "2020-03-11T10:52:50.000+1300", "address1": "1 Old Street", "address3": "Auckland", "postCode": "1010", "countryCode": "NZ", "addressType": "REGISTERED" },
              { "startDate": "2020-03-11T10:52:50.000+1300", "endDate": null, "careOf": null, "address1": "185 Fanshawe Street", "address2": null, "address3": "Auckland", "address4": null, "postCode": "1010", "countryCode": "NZ", "addressType": "REGISTERED" }
            ]
          },
          "websites": [ { "url": "www.airnewzealand.co.nz", "websitePurpose": null } ],
          "industryClassifications": [
            { "classificationCode": "I490008", "classificationDescription": "Air transport under CAA Rules part 121, 125, or 129" },
            { "classificationCode": "I490020", "classificationDescription": "Airline operation - scheduled domestic" }
          ],
          "tradingNames": [
            { "name": "Air New Zealand Limited", "startDate": "2020-03-11T10:52:50.000+1300", "endDate": null },
            { "name": "Old Trading Name", "startDate": "2001-01-01T00:00:00.000+1300", "endDate": "2005-01-01T00:00:00.000+1300" }
          ]
        }
        """;

    [Fact]
    public void RegisteredEntity_CallsNzbnApiWithSubscriptionKeyAndReturnsNzbnProperties()
    {
        var http = new FakeHttp(_ => Ok(AirNewZealand));

        var results = Run(Script, http, Values((Key, "9429040402543")), apiKey: SubscriptionKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "9429040402543", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(SubscriptionKey, request.Header("Ocp-Apim-Subscription-Key"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["nzbn.nzbn"] = "9429040402543",
            ["nzbn.name"] = "AIR NEW ZEALAND LIMITED",
            ["nzbn.status"] = "Registered",
            ["nzbn.entityType"] = "NZ Limited Company",
            ["nzbn.entityTypeCode"] = "LTD",
            ["nzbn.registrationDate"] = "1940-04-26",
            ["nzbn.sourceRegister"] = "COMPANY",
            ["nzbn.sourceRegisterId"] = "104799",
            ["nzbn.registeredAddress"] = "185 Fanshawe Street, Auckland, 1010",
            ["nzbn.postalAddress"] = "Private Bag 92007, Victoria Street West, Auckland, 1142",
            ["nzbn.website"] = "www.airnewzealand.co.nz",
            ["nzbn.industryCode"] = "I490008",
            ["nzbn.industry"] = "Air transport under CAA Rules part 121, 125, or 129",
            ["nzbn.tradingNames"] = "Air New Zealand Limited",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["nzbn.registrationDate"]);
    }

    [Theory]
    [InlineData("9429040402543")]
    [InlineData("94 2904 0402 543")]
    [InlineData("9429-0404-02543")]
    public void NormalisesNzbn(string nzbn)
    {
        var http = new FakeHttp(_ => Ok(AirNewZealand));

        Run(Script, http, Values((Key, nzbn)), apiKey: SubscriptionKey);

        Assert.Equal(Url + "9429040402543", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("104799")] // company number, not an NZBN
    [InlineData("9429040402544")] // wrong check digit
    [InlineData("5012345678900")] // valid GS1 number, but not an NZBN
    public void NoValidNzbn_ReturnsNoResultWithoutCallingTheApi(string nzbn)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, nzbn)), apiKey: SubscriptionKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoSubscriptionKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "9429040402543")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void SubscriptionKeyRejected_ThrowsErrorThatFailsTheRecord(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, """{ "statusCode": 401, "message": "Access denied due to invalid subscription key." }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "9429040402543")), apiKey: SubscriptionKey));

        Assert.Contains("subscription key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("NotFound", """{ "status": "404", "errorDescription": "Entity not found" }""")]
    [InlineData("BadRequest", """{ "status": "400", "errorDescription": "Checksum is invalid" }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void NotFoundOrApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "9429040402543")), cache, SubscriptionKey));
        Assert.Empty(Run(Script, http, Values((Key, "9429040402543")), cache, SubscriptionKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void RateLimited_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", """{ "statusCode": 429, "message": "Rate limit is exceeded." }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "9429040402543")), apiKey: SubscriptionKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNzbnAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(AirNewZealand));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "9429040402543")), cache, SubscriptionKey);
        var second = Run(Script, http, Values((Key, "94 2904 0402 543")), cache, SubscriptionKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], SubscriptionKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
