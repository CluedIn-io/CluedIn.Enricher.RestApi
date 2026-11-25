using System.Collections.Generic;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Hong Kong Companies Registry sample Process Script (docs/samples/companies-registry-hong-kong)
/// the same way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class HkcrProcessScriptTests
{
    private const string Key = "organization.brNumber";
    private const string LocalUrl = "https://data.cr.gov.hk/cr/api/api/v1/api_builder/json/local/search";
    private const string ForeignUrl = "https://data.cr.gov.hk/cr/api/api/v1/api_builder/json/foreign/search";
    private const string Query = "?query%5B0%5D%5Bkey1%5D=Brn&query%5B0%5D%5Bkey2%5D=equal&query%5B0%5D%5Bkey3%5D=";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.hkcr-process-script.js");

    // Real response for 80029004
    private const string LocalCompany = """
        [{"Brn":"80029004","Chinese_Company_Name":"NULL","English_Company_Name":"CLPe eMobility Limited","Address_of_Registered_Office":"CLP HEADQUARTERS, 43 SHING KAI ROAD, KAI TAK, HONG KONG","Company_Type":"Private company limited by shares","Date_of_Incorporation":"23-03-2026","Re-domiciliation_Date":null}]
        """;

    // Real response for 75698252
    private const string NonHongKongCompany = """
        [{"Brn":"75698252","Corporate_Name":"中鐵大橋勘測設計院集團有限公司","Approved_name_for_carrying_on_business_in_H.K._Corporate":null,"Other_Corporate_Name_s":"China Railway Major Bridge Reconnaissance & Design Institute Co., Ltd.","Approved_name_for_carrying_on_business_in_H.K._Other_Corporate":null,"Principal_Place_of_Business_in_H.K.":"RM 06, 13A/F SOUTH TOWER WORLD, FINANCE CTR HARBOUR CITY 17 CANTON RD, TST KLN, HONG KONG","Place_of_Incorporation":"CHINA","Company_Type":"Registered non-Hong Kong company","Date_of_Registration":"12-09-2023"}]
        """;

    // What the API returns for a number that isn't in the list
    private static ResponseDto NotFound() => Respond("BadRequest", "<!DOCTYPE html><html><head><meta name=\"description\" content=\"Maintenance Page For CR Portal\" /></head></html>");

    [Fact]
    public void LocalCompany_SearchesLocalListAndReturnsHkcrProperties()
    {
        var http = new FakeHttp(_ => Ok(LocalCompany));

        var results = Run(Script, http, Values((Key, "80029004")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(LocalUrl + Query + "80029004", request.Url);
        Assert.Equal("GET", request.Method);
        // data.cr.gov.hk answers 403 Forbidden to requests without a User-Agent
        Assert.Equal("CluedIn REST API enricher", request.Header("User-Agent"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        // "NULL" Chinese name and the null re-domiciliation date are left out
        Assert.Equal(new Dictionary<string, string>
        {
            ["hkcr.brNumber"] = "80029004",
            ["hkcr.name"] = "CLPe eMobility Limited",
            ["hkcr.companyType"] = "Private company limited by shares",
            ["hkcr.address"] = "CLP HEADQUARTERS, 43 SHING KAI ROAD, KAI TAK, HONG KONG",
            ["hkcr.incorporationDate"] = "2026-03-23",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["hkcr.incorporationDate"]);
    }

    [Fact]
    public void LocalCompanyWithChineseName_StoresBothNames()
    {
        var http = new FakeHttp(_ => Ok("""
            [{"Brn":"C1572528","Chinese_Company_Name":"中國文藝復興教育基金有限公司","English_Company_Name":"China Renaissance Education Foundation Limited","Address_of_Registered_Office":"UNIT 3302, 33/F., CABLE TV TOWER, NO.9 HOI SHING ROAD, TSUEN WAN, NT, HONG KONG","Company_Type":"Company limited by guarantee","Date_of_Incorporation":"14-03-2011","Re-domiciliation_Date":null}]
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "c1572528"))));

        Assert.Equal(LocalUrl + Query + "C1572528", Assert.Single(http.Requests).Url);
        Assert.Equal("China Renaissance Education Foundation Limited", result.Data["hkcr.name"].ToString());
        Assert.Equal("中國文藝復興教育基金有限公司", result.Data["hkcr.chineseName"].ToString());
    }

    [Fact]
    public void NotALocalCompany_SearchesNonHongKongCompanies()
    {
        var http = new FakeHttp(r => r.Url.StartsWith(LocalUrl) ? NotFound() : Ok(NonHongKongCompany));

        var results = Run(Script, http, Values((Key, "75698252")));

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(LocalUrl + Query + "75698252", http.Requests[0].Url);
        Assert.Equal(ForeignUrl + Query + "75698252", http.Requests[1].Url);
        Assert.All(http.Requests, r => Assert.Equal("CluedIn REST API enricher", r.Header("User-Agent")));
        Assert.Equal(new Dictionary<string, string>
        {
            ["hkcr.brNumber"] = "75698252",
            ["hkcr.name"] = "中鐵大橋勘測設計院集團有限公司",
            ["hkcr.otherName"] = "China Railway Major Bridge Reconnaissance & Design Institute Co., Ltd.",
            ["hkcr.companyType"] = "Registered non-Hong Kong company",
            ["hkcr.address"] = "RM 06, 13A/F SOUTH TOWER WORLD, FINANCE CTR HARBOUR CITY 17 CANTON RD, TST KLN, HONG KONG",
            ["hkcr.registrationDate"] = "2023-09-12",
            ["hkcr.placeOfIncorporation"] = "CHINA",
        }, Stringify(Assert.Single(results).Data));
    }

    [Theory]
    [InlineData("80029004")]
    [InlineData("8002 9004")]
    [InlineData("80029004-000-03-26-A")]
    [InlineData(" 80029004 ")]
    public void NormalisesBrNumber(string brNumber)
    {
        var http = new FakeHttp(_ => Ok(LocalCompany));

        Run(Script, http, Values((Key, brNumber)));

        Assert.Equal(LocalUrl + Query + "80029004", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("AB123456")]
    [InlineData("80029004&x=1")]
    public void NoValidBrNumber_ReturnsNoResultWithoutCallingTheApi(string brNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, brNumber)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void NotFoundInEitherList_ReturnsNoResultAndIsNotCached()
    {
        var http = new FakeHttp(_ => NotFound());
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "12345678")), cache));
        Assert.Equal(2, http.Requests.Count);

        Assert.Empty(Run(Script, http, Values((Key, "12345678")), cache));
        Assert.Equal(4, http.Requests.Count);
    }

    [Theory]
    [InlineData("Forbidden", "<html><body>403 Forbidden</body></html>")]
    [InlineData("InternalServerError", "")]
    [InlineData("GatewayTimeout", "Gateway Timeout")]
    [InlineData("OK", "not json")]
    [InlineData("OK", "[]")]
    [InlineData("OK", """[{"Brn":"99999999","English_Company_Name":"Another company"}]""")]
    public void ApiCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        Assert.Empty(Run(Script, http, Values((Key, "80029004"))));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "80029004"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(r => r.Url.StartsWith(LocalUrl) ? NotFound() : Ok(NonHongKongCompany));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "75698252")), cache);
        var second = Run(Script, http, Values((Key, "7569 8252")), cache);

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key]);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
