using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the MCA company master data sample Process Script (docs/samples/mca-india) the same way the
/// V2 REST API enricher does, against a fake <c>http</c> object instead of the real data.gov.in API.
/// </summary>
public class McaProcessScriptTests
{
    private const string Key = "organization.cin";
    private const string Url = "https://api.data.gov.in/resource/4dbe5667-7b6b-41d7-82af-211562424d9a";
    private const string ApiKey = "test-key";
    private const string Cin = "L17110MH1973PLC019786";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.mca-process-script.js");

    // Shaped like a data.gov.in resource response, with the dataset's published column names
    private const string Reliance = """
        {
          "status": "ok",
          "total": 1,
          "count": 1,
          "limit": "1",
          "offset": "0",
          "records": [
            {
              "CIN": "L17110MH1973PLC019786",
              "CompanyName": "RELIANCE INDUSTRIES LIMITED",
              "CompanyROCcode": "RoC-Mumbai",
              "CompanyCategory": "Company limited by Shares",
              "CompanySubCategory": "Non-govt company",
              "CompanyClass": "Public",
              "AuthorizedCapital": "150000000000",
              "PaidupCapital": "135320000000",
              "CompanyRegistrationdate_date": "1973-05-08",
              "Registered_Office_Address": "3rd Floor, Maker Chambers IV,  222, Nariman Point, Mumbai, Maharashtra, 400021",
              "Listingstatus": "Listed",
              "CompanyStatus": "Active",
              "CompanyStateCode": "Maharashtra",
              "CompanyIndian_Foreign_Company": "NA",
              "nic_code": "17110",
              "CompanyIndustrialClassification": "Manufacturing (Textiles)"
            }
          ]
        }
        """;

    [Fact]
    public void RegisteredCompany_GetsRecordAndReturnsMcaProperties()
    {
        var http = new FakeHttp(_ => Ok(Reliance));

        var results = Run(Script, http, Values((Key, Cin)), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal($"{Url}?api-key={ApiKey}&format=json&limit=1&filters%5BCIN%5D={Cin}", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["mca.cin"] = Cin,
            ["mca.name"] = "RELIANCE INDUSTRIES LIMITED",
            ["mca.status"] = "Active",
            ["mca.class"] = "Public",
            ["mca.category"] = "Company limited by Shares",
            ["mca.subCategory"] = "Non-govt company",
            ["mca.rocCode"] = "RoC-Mumbai",
            ["mca.registrationDate"] = "1973-05-08",
            ["mca.registeredOfficeAddress"] = "3rd Floor, Maker Chambers IV, 222, Nariman Point, Mumbai, Maharashtra, 400021",
            ["mca.stateCode"] = "Maharashtra",
            ["mca.authorizedCapital"] = "150000000000",
            ["mca.paidUpCapital"] = "135320000000",
            ["mca.listingStatus"] = "Listed",
            ["mca.industrialClassification"] = "Manufacturing (Textiles)",
            ["mca.nicCode"] = "17110",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["mca.registrationDate"]);
    }

    [Theory]
    [InlineData("08-05-1973")]
    [InlineData("08/05/1973")]
    [InlineData("8-5-1973")]
    [InlineData("1973-05-08T00:00:00")]
    public void NormalisesRegistrationDate(string registrationDate)
    {
        var http = new FakeHttp(_ => Ok($$"""{ "status": "ok", "records": [ { "CIN": "{{Cin}}", "CompanyName": "X", "CompanyRegistrationdate_date": "{{registrationDate}}" } ] }"""));

        var result = Assert.Single(Run(Script, http, Values((Key, Cin)), apiKey: ApiKey));

        Assert.Equal("1973-05-08", result.Data["mca.registrationDate"].ToString());
    }

    [Fact]
    public void LowerCaseFieldNames_AreStillRead()
    {
        var http = new FakeHttp(_ => Ok($$"""{ "status": "ok", "records": [ { "cin": "{{Cin}}", "companyname": "RELIANCE INDUSTRIES LIMITED", "companystatus": "Active" } ] }"""));

        var result = Assert.Single(Run(Script, http, Values((Key, Cin)), apiKey: ApiKey));

        Assert.Equal(new Dictionary<string, string>
        {
            ["mca.cin"] = Cin,
            ["mca.name"] = "RELIANCE INDUSTRIES LIMITED",
            ["mca.status"] = "Active",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("L17110MH1973PLC019786")]
    [InlineData("l17110mh1973plc019786")]
    [InlineData(" L 17110 MH 1973 PLC 019786 ")]
    [InlineData("L17110-MH1973-PLC019786")]
    public void NormalisesCin(string cin)
    {
        var http = new FakeHttp(_ => Ok(Reliance));

        Run(Script, http, Values((Key, cin)), apiKey: ApiKey);

        Assert.EndsWith($"&filters%5BCIN%5D={Cin}", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("L17110MH1973PLC01978")] // 20 characters
    [InlineData("X17110MH1973PLC019786")] // must start with L or U
    [InlineData("AAA-1234")] // LLPIN, not a CIN
    [InlineData("27AAACR5055K1ZK")] // GSTIN
    public void NoValidCin_ReturnsNoResultWithoutCallingTheApi(string cin)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, cin)), apiKey: ApiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, Cin)), apiKey: apiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{ "status": "ok", "total": 0, "count": 0, "records": [] }""")]
    [InlineData("OK", """{ "status": "ok", "records": [ { "CIN": "U12345MH2000PTC123456", "CompanyName": "OTHER" } ] }""")]
    [InlineData("BadRequest", """{ "status": "error", "message": "Invalid filter" }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("ServiceUnavailable", "<html>Service Unavailable</html>")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, Cin)), cache, ApiKey));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same CIN calls the API again
        Assert.Empty(Run(Script, http, Values((Key, Cin)), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized", "")]
    [InlineData("Forbidden", """{ "error": "Key not authorised" }""")]
    [InlineData("OK", """{ "status": "error", "message": "Invalid API key" }""")]
    public void ApiKeyRejected_FailsTheRecordWithoutRetrying(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, Cin)), apiKey: ApiKey));

        Assert.Contains("API key", ex.ToString());
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, Cin)), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameCinAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Reliance));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, Cin)), cache, ApiKey);
        var second = Run(Script, http, Values((Key, Cin.ToLowerInvariant())), cache, ApiKey);

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
