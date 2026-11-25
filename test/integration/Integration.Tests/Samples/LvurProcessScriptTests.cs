using System;
using System.Collections.Generic;
using Jint.Runtime;
using Newtonsoft.Json.Linq;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Latvian Enterprise Register sample Process Script (docs/samples/enterprise-register-latvia)
/// the same way the V2 REST API enricher does, against a fake <c>http</c> object instead of data.gov.lv.
/// </summary>
public class LvurProcessScriptTests
{
    private const string Key = "organization.registrationNumber";
    private const string Url = "https://data.gov.lv/dati/api/3/action/datastore_search";
    private const string ResourceId = "25e80bf3-f107-4ab4-89ef-251b5b9374e9";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.lvur-process-script.js");

    // A real response for 40003032949
    private const string Latvenergo = """
        {
          "success": true,
          "result": {
            "resource_id": "25e80bf3-f107-4ab4-89ef-251b5b9374e9",
            "records": [{
              "_id": 457770, "regcode": 40003032949, "sepa": "LV61ZZZ40003032949",
              "name": "Akciju sabiedrība \"Latvenergo\"", "name_before_quotes": "Akciju sabiedrība", "name_in_quotes": "Latvenergo", "name_after_quotes": "",
              "without_quotes": 0, "regtype": "K", "regtype_text": "Komercreģistrs", "type": "AS", "type_text": "Akciju sabiedrība",
              "registered": "1991-10-08T00:00:00", "terminated": null, "closed": " ",
              "address": "Rīga, Pulkveža Brieža iela 12", "index": 1010, "addressid": 101971850, "region": 0, "city": 100003003, "atvk": 10000, "reregistration_term": ""
            }],
            "total": 1
          }
        }
        """;

    // A real response for 41202013815, a liquidated sole trader
    private const string Liquidated = """
        {
          "success": true,
          "result": {
            "records": [{
              "_id": 1, "regcode": 41202013815, "sepa": "LV53ZZZ41202013815", "name": "IK \"KRASTNIEKI A I\"",
              "regtype": "K", "regtype_text": "Komercreģistrs", "type": "IK", "type_text": "Individuālais komersants",
              "registered": "1998-02-26T00:00:00", "terminated": "2014-04-10T00:00:00", "closed": "L",
              "address": "Dundagas nov., Kolkas pag., Kolka, \"Krastnieki\"", "index": 3275
            }],
            "total": 1
          }
        }
        """;

    [Fact]
    public void RegisteredEntity_QueriesByExactNumberAndReturnsLvurProperties()
    {
        var http = new FakeHttp(_ => Ok(Latvenergo));

        var results = Run(Script, http, Values((Key, "40003032949")));

        var request = Assert.Single(http.Requests);
        Assert.Equal("GET", request.Method);
        var uri = new Uri(request.Url);
        Assert.Equal(Url, uri.GetLeftPart(UriPartial.Path));
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(ResourceId, query["resource_id"]);
        Assert.Equal("40003032949", (string)JObject.Parse(query["filters"])["regcode"]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["lvur.registrationNumber"] = "40003032949",
            ["lvur.name"] = "Akciju sabiedrība \"Latvenergo\"",
            ["lvur.legalForm"] = "Akciju sabiedrība",
            ["lvur.legalFormCode"] = "AS",
            ["lvur.register"] = "Komercreģistrs",
            ["lvur.status"] = "Registered",
            ["lvur.registrationDate"] = "1991-10-08",
            ["lvur.address"] = "Rīga, Pulkveža Brieža iela 12",
            ["lvur.postalCode"] = "LV-1010",
            ["lvur.sepaCreditorId"] = "LV61ZZZ40003032949",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["lvur.registrationDate"]);
    }

    [Fact]
    public void LiquidatedEntity_StoresStatusAndTerminationDate()
    {
        var http = new FakeHttp(_ => Ok(Liquidated));

        var result = Assert.Single(Run(Script, http, Values((Key, "41202013815"))));

        Assert.Equal("Liquidated", result.Data["lvur.status"].ToString());
        Assert.Equal("2014-04-10", result.Data["lvur.terminationDate"].ToString());
    }

    [Fact]
    public void ReorganisedEntity_StoresReorganisedStatus()
    {
        var http = new FakeHttp(_ => Ok(Liquidated.Replace("\"closed\": \"L\"", "\"closed\": \"R\"")));

        var result = Assert.Single(Run(Script, http, Values((Key, "41202013815"))));

        Assert.Equal("Reorganised", result.Data["lvur.status"].ToString());
    }

    [Theory]
    [InlineData("40003032949")]
    [InlineData("LV40003032949")]
    [InlineData("lv 4000 303 2949")]
    [InlineData("40003-032949")]
    public void NormalisesRegistrationNumber(string registrationNumber)
    {
        var http = new FakeHttp(_ => Ok(Latvenergo));

        Assert.Single(Run(Script, http, Values((Key, registrationNumber))));

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(Assert.Single(http.Requests).Url).Query);
        Assert.Equal("40003032949", (string)JObject.Parse(query["filters"])["regcode"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4000303294")] // 10 digits
    [InlineData("400030329491")] // 12 digits
    [InlineData("EE40003032949")]
    public void NoValidRegistrationNumber_ReturnsNoResultWithoutCallingTheApi(string registrationNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, registrationNumber))));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{ "success": true, "result": { "records": [], "total": 0 } }""")]
    [InlineData("BadRequest", """{ "success": false, "error": { "__type": "Validation Error" } }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiHasNoAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "40003032949")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "40003032949")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("ServiceUnavailable")]
    public void ApiBusy_ThrowsErrorTheEnricherRetries(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "40003032949"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Latvenergo));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "40003032949")), cache);
        var second = Run(Script, http, Values((Key, "LV40003032949")), cache);

        Assert.Single(http.Requests);
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
