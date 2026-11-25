using System.Collections.Generic;
using System.Linq;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Slovak Register of Legal Entities sample Process Script (docs/samples/rpo-slovakia) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the RPO API.
/// </summary>
public class RpoProcessScriptTests
{
    private const string Key = "organization.ico";
    private const string Url = "https://api.statistics.sk/rpo/v1";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.rpo-process-script.js");

    // Shortened from real responses for 31322832
    private const string SlovnaftSearch = """
        {
          "results": [{
            "id": 1003617,
            "identifiers": [{ "value": "31322832", "validFrom": "1992-04-29" }],
            "fullNames": [
              { "value": "SLOVNAFT a.s.", "validFrom": "1992-04-29", "validTo": "1998-11-29" },
              { "value": "SLOVNAFT, a.s.", "validFrom": "2008-05-30" }
            ],
            "addresses": [
              { "validFrom": "2006-06-16", "street": "Vlčie hrdlo", "regNumber": 0, "buildingNumber": "1", "postalCodes": ["82412"], "municipality": { "value": "Bratislava" }, "country": { "value": "Slovenská republika", "code": "703" } }
            ],
            "establishment": "1992-05-01"
          }],
          "license": "CC BY 4.0"
        }
        """;

    private const string SlovnaftEntity = """
        {
          "id": 1003617,
          "identifiers": [{ "value": "31322832", "validFrom": "1992-04-29" }],
          "fullNames": [{ "value": "SLOVNAFT, a.s.", "validFrom": "2008-05-30" }],
          "addresses": [
            { "validFrom": "2006-06-16", "street": "Vlčie hrdlo", "regNumber": 0, "buildingNumber": "1", "postalCodes": ["82412"], "municipality": { "value": "Bratislava" }, "country": { "value": "Slovenská republika", "code": "703" } }
          ],
          "legalForms": [{ "value": { "value": "Akciová spoločnosť", "code": "121", "codelistCode": "CL000056" }, "validFrom": "1992-04-29" }],
          "establishment": "1992-05-01",
          "equities": [
            { "validFrom": "2009-04-02", "value": 684757602.8, "currency": { "value": "euro", "code": "EUR" } },
            { "validFrom": "2009-04-02", "valuePaid": 684757602.8, "currency": { "value": "euro", "code": "EUR" } }
          ],
          "sourceRegister": {
            "value": { "value": "Obchodný register", "code": "1" },
            "registrationOffices": [{ "value": "Mestský súd Bratislava III", "validFrom": "1992-04-29" }],
            "registrationNumbers": [{ "value": "Sa/426/B", "validFrom": "1992-04-29" }]
          },
          "statisticalCodes": {
            "mainActivity": { "value": "Výroba rafinovaných ropných produktov a produktov z fosílnych palív", "code": "1920" }
          }
        }
        """;

    // Shortened from real responses for 44354258, a company dissolved by merger
    private const string DissolvedSearch = """
        {
          "results": [{
            "id": 1431111,
            "identifiers": [{ "value": "44354258", "validFrom": "2008-08-26" }],
            "fullNames": [
              { "value": "CM European Power Slovakia, s. r. o.", "validFrom": "2008-08-26", "validTo": "2017-12-31" }
            ],
            "addresses": [
              { "validFrom": "2008-08-26", "validTo": "2017-12-31", "street": "Vlčie hrdlo", "regNumber": 0, "buildingNumber": "1", "postalCodes": ["82412"], "municipality": { "value": "Bratislava" }, "country": { "value": "Slovenská republika" } }
            ],
            "establishment": "2008-08-26",
            "termination": "2018-01-01"
          }]
        }
        """;

    private const string DissolvedEntity = """
        {
          "id": 1431111,
          "identifiers": [{ "value": "44354258", "validFrom": "2008-08-26" }],
          "fullNames": [],
          "addresses": [],
          "legalForms": [{ "value": { "value": "Spoločnosť s ručením obmedzeným", "code": "112" }, "validFrom": "2008-08-26" }],
          "legalStatuses": [{ "value": { "value": "Zaniknutá spoločnosť", "code": "17" }, "validFrom": "2018-01-01" }],
          "establishment": "2008-08-26",
          "termination": "2018-01-01",
          "sourceRegister": {
            "value": { "value": "Obchodný register", "code": "1" },
            "registrationOffices": [{ "value": "Okresný súd Bratislava I", "validFrom": "2008-08-26" }],
            "registrationNumbers": [{ "value": "Sro/54105/B", "validFrom": "2008-08-26" }]
          }
        }
        """;

    private static FakeHttp Api(string search, string entity) => new(r =>
        r.Url.StartsWith(Url + "/search") ? Ok(search) : Ok(entity));

    [Fact]
    public void RegisteredEntity_SearchesByIcoThenGetsEntityAndReturnsRpoProperties()
    {
        var http = Api(SlovnaftSearch, SlovnaftEntity);

        var results = Run(Script, http, Values((Key, "31322832")));

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal($"{Url}/search?identifier=31322832", http.Requests[0].Url);
        Assert.Equal($"{Url}/entity/1003617?showHistoricalData=false&showOrganizationUnits=false", http.Requests[1].Url);
        Assert.All(http.Requests, r => Assert.Equal("GET", r.Method));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["rpo.ico"] = "31322832",
            ["rpo.name"] = "SLOVNAFT, a.s.",
            ["rpo.legalForm"] = "Akciová spoločnosť",
            ["rpo.legalFormCode"] = "121",
            ["rpo.address"] = "Vlčie hrdlo 1, 82412 Bratislava",
            ["rpo.municipality"] = "Bratislava",
            ["rpo.postalCode"] = "82412",
            ["rpo.country"] = "Slovenská republika",
            ["rpo.establishmentDate"] = "1992-05-01",
            ["rpo.mainActivity"] = "Výroba rafinovaných ropných produktov a produktov z fosílnych palív",
            ["rpo.mainActivityCode"] = "1920",
            ["rpo.sourceRegister"] = "Obchodný register",
            ["rpo.registrationOffice"] = "Mestský súd Bratislava III",
            ["rpo.registrationNumber"] = "Sa/426/B",
            ["rpo.registeredCapital"] = 684757602.8.ToString(),
            ["rpo.registeredCapitalCurrency"] = "EUR",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["rpo.establishmentDate"]);
    }

    [Fact]
    public void DissolvedEntity_UsesLastNameAndAddressFromHistoryAndStoresTermination()
    {
        var http = Api(DissolvedSearch, DissolvedEntity);

        var result = Assert.Single(Run(Script, http, Values((Key, "44354258"))));

        Assert.Equal("CM European Power Slovakia, s. r. o.", result.Data["rpo.name"].ToString());
        Assert.Equal("Vlčie hrdlo 1, 82412 Bratislava", result.Data["rpo.address"].ToString());
        Assert.Equal("Zaniknutá spoločnosť", result.Data["rpo.legalStatus"].ToString());
        Assert.Equal("2018-01-01", result.Data["rpo.terminationDate"].ToString());
    }

    [Fact]
    public void AddressWithRegisterAndBuildingNumber_IsWrittenAsRegisterSlashBuilding()
    {
        var http = Api(SlovnaftSearch, SlovnaftEntity.Replace("\"regNumber\": 0", "\"regNumber\": 2500"));

        var result = Assert.Single(Run(Script, http, Values((Key, "31322832"))));

        Assert.Equal("Vlčie hrdlo 2500/1, 82412 Bratislava", result.Data["rpo.address"].ToString());
    }

    [Theory]
    [InlineData("31322832", "31322832")]
    [InlineData("31 322 832", "31322832")]
    [InlineData("IČO: 31 322 832", "31322832")]
    [InlineData("ičo 31322832", "31322832")]
    [InlineData("151742", "00151742")] // leading zeros left out
    [InlineData("00151742", "00151742")]
    public void NormalisesIco(string ico, string expected)
    {
        var http = new FakeHttp(_ => Ok("""{ "results": [] }"""));

        Run(Script, http, Values((Key, ico)));

        Assert.Equal($"{Url}/search?identifier={expected}", http.Requests.First().Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("31322833")] // wrong check digit
    [InlineData("12345")] // too short
    [InlineData("313228320")] // too long
    [InlineData("SK2020273893")]
    public void NoValidIco_ReturnsNoResultWithoutCallingTheApi(string ico)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, ico))));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void IcoNotInRegister_ReturnsNoResultWithoutGettingAnEntity()
    {
        var http = new FakeHttp(_ => Ok("""{ "results": [], "license": "CC BY 4.0" }"""));

        Assert.Empty(Run(Script, http, Values((Key, "31322832"))));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void SearchResultForAnotherIco_IsNotUsed()
    {
        var http = Api(SlovnaftSearch, SlovnaftEntity);

        // 00151742 is valid, but the (fake) search result is for 31322832
        Assert.Empty(Run(Script, http, Values((Key, "00151742"))));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("NotFound", """{ "message": "No record found" }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void EntityHasNoAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(r => r.Url.Contains("/search") ? Ok(SlovnaftSearch) : Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "31322832")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "31322832")), cache));
        Assert.Equal(4, http.Requests.Count);
    }

    [Theory]
    [InlineData("InternalServerError", "")]
    [InlineData("BadRequest", """{ "message": "Bad request" }""")]
    public void SearchHasNoAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        Assert.Empty(Run(Script, http, Values((Key, "31322832"))));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("ServiceUnavailable")]
    public void ApiBusy_ThrowsErrorTheEnricherRetries(string httpStatus)
    {
        var http = new FakeHttp(_ => new ResponseDto { HttpStatus = httpStatus, Content = "" });

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "31322832"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameIcoAgain_UsesCachedProperties()
    {
        var http = Api(SlovnaftSearch, SlovnaftEntity);
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "31322832")), cache);
        var second = Run(Script, http, Values((Key, "31 322 832")), cache);

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Equal("[]", TestConnection(Script, http, [Key]));
        Assert.Empty(http.Requests);
    }
}
