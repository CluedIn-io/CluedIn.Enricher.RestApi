using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the KVK sample Process Script (docs/samples/kvk-netherlands) the same way the V2 REST API
/// enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class KvkProcessScriptTests
{
    private const string Key = "organization.kvkNumber";
    private const string Url = "https://api.kvk.nl/api/v1/basisprofielen/";
    private const string ApiKey = "test-api-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.kvk-process-script.js");

    // A real response from KVK's test environment for 68750110, without the links
    private const string TestBvDonald = """
        {
          "kvkNummer": "68750110",
          "indNonMailing": "Ja",
          "naam": "Test BV Donald",
          "formeleRegistratiedatum": "20170519",
          "materieleRegistratie": {
            "datumAanvang": "20170519"
          },
          "totaalWerkzamePersonen": 1,
          "statutaireNaam": "Test BV Donald",
          "handelsnamen": [
            {
              "naam": "Test BV Donald",
              "volgorde": 0
            },
            {
              "naam": "Test BV Donald Nevenvestiging",
              "volgorde": 1
            }
          ],
          "sbiActiviteiten": [
            {
              "sbiCode": "01241",
              "sbiOmschrijving": "Teelt van appels en peren",
              "indHoofdactiviteit": "Ja"
            }
          ],
          "_embedded": {
            "eigenaar": {
              "rsin": "857587973",
              "rechtsvorm": "Besloten vennootschap",
              "uitgebreideRechtsvorm": "Besloten vennootschap"
            },
            "hoofdvestiging": {
              "vestigingsnummer": "000037178598",
              "kvkNummer": "68750110",
              "formeleRegistratiedatum": "20200210",
              "materieleRegistratie": {
                "datumAanvang": "20140210"
              },
              "eersteHandelsnaam": "Test BV Donald",
              "indHoofdvestiging": "Ja",
              "indCommercieleVestiging": "Ja",
              "totaalWerkzamePersonen": 1,
              "adressen": [
                {
                  "type": "correspondentieadres",
                  "indAfgeschermd": "Nee",
                  "volledigAdres": "Postbus 200                                 1000AE Rommeldam",
                  "straatnaam": "Postbus",
                  "postcode": "1000AE",
                  "postbusnummer": 200,
                  "plaats": "Rommeldam",
                  "land": "Nederland"
                },
                {
                  "type": "bezoekadres",
                  "indAfgeschermd": "Nee",
                  "volledigAdres": "Hizzaarderlaan 3 A                                 8823SJ Lollum",
                  "straatnaam": "Hizzaarderlaan",
                  "huisnummer": 3,
                  "huisnummerToevoeging": "A",
                  "postcode": "8823SJ",
                  "plaats": "Lollum",
                  "land": "Nederland"
                }
              ]
            }
          }
        }
        """;

    [Fact]
    public void RegisteredCompany_GetsBasicProfileAndReturnsKvkProperties()
    {
        var http = new FakeHttp(_ => Ok(TestBvDonald));

        var results = Run(Script, http, Values((Key, "68750110")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "68750110", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header("apikey"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["kvk.kvkNumber"] = "68750110",
            ["kvk.name"] = "Test BV Donald",
            ["kvk.statutoryName"] = "Test BV Donald",
            ["kvk.tradeNames"] = "Test BV Donald; Test BV Donald Nevenvestiging",
            ["kvk.legalForm"] = "Besloten vennootschap",
            ["kvk.rsin"] = "857587973",
            ["kvk.registrationDate"] = "2017-05-19",
            ["kvk.startDate"] = "2017-05-19",
            ["kvk.employees"] = "1",
            ["kvk.mainActivityCode"] = "01241",
            ["kvk.mainActivity"] = "Teelt van appels en peren",
            ["kvk.establishmentNumber"] = "000037178598",
            ["kvk.visitingAddress"] = "Hizzaarderlaan 3 A, 8823SJ Lollum",
            ["kvk.postalAddress"] = "Postbus 200, 1000AE Rommeldam",
            ["kvk.city"] = "Lollum",
            ["kvk.postalCode"] = "8823SJ",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["kvk.registrationDate"]);
    }

    [Fact]
    public void WebsiteAndPartialDates_AreHandled()
    {
        var http = new FakeHttp(_ => Ok("""
            {
              "kvkNummer": "68750110",
              "naam": "Example BV",
              "formeleRegistratiedatum": "20170500",
              "_embedded": { "hoofdvestiging": { "websites": ["www.example.nl", "www.example.com"] } }
            }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "68750110")), apiKey: ApiKey));

        Assert.Equal(new Dictionary<string, string>
        {
            ["kvk.kvkNumber"] = "68750110",
            ["kvk.name"] = "Example BV",
            ["kvk.website"] = "www.example.nl",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("68750110", "68750110")]
    [InlineData("6875 0110", "68750110")]
    [InlineData("68.75.01.10", "68750110")]
    [InlineData("KVK 68750110", "68750110")]
    [InlineData("kvk68750110", "68750110")]
    [InlineData("1234567", "01234567")]
    public void NormalisesKvkNumber(string kvkNumber, string expected)
    {
        var http = new FakeHttp(_ => Ok(TestBvDonald));

        Run(Script, http, Values((Key, kvkNumber)), apiKey: ApiKey);

        Assert.Equal(Url + expected, Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789")]
    [InlineData("NL123456789B01")]
    [InlineData("abc")]
    public void NoValidKvkNumber_ReturnsNoResultWithoutCallingTheApi(string kvkNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, kvkNumber)), apiKey: ApiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, "68750110")), apiKey: apiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("NotFound", """{"fout":[{"code":"IPD0005","omschrijving":"Op basis van het KVK-nummer 12345678 kan het product niet worden geleverd."}]}""")]
    [InlineData("BadRequest", """{"fout":[{"code":"IPD0004","omschrijving":"Het KVK-nummer is niet valide."}]}""")]
    [InlineData("InternalServerError", "")]
    [InlineData("ServiceUnavailable", "Service Unavailable")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "12345678")), cache, ApiKey));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same number calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "12345678")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void ApiKeyRejected_ThrowsErrorThatIsNotRetried(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "68750110")), apiKey: "wrong"));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "68750110")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(TestBvDonald));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "68750110")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "6875 0110")), cache, ApiKey);

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
