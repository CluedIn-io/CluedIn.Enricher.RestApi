using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the gBizINFO sample Process Script (docs/samples/gbizinfo-japan) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class GbizinfoProcessScriptTests
{
    private const string Key = "organization.corporateNumber";
    private const string Url = "https://api.info.gbiz.go.jp/hojin/v2/hojin/";
    private const string ApiKey = "test-token";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.gbizinfo-process-script.js");

    // Shaped like HojinInfoResponseV2 in gBizINFO's OpenAPI definition
    private const string Toyota = """
        {
          "id": null,
          "message": "200 - OK.",
          "errors": [],
          "hojin-infos": [
            {
              "corporate_number": "1180301018771",
              "name": "トヨタ自動車株式会社",
              "name_en": "TOYOTA MOTOR CORPORATION",
              "kana": "トヨタジドウシャ",
              "location": "愛知県豊田市トヨタ町1番地",
              "postal_code": "4718571",
              "status": "",
              "representative_name": "佐藤恒治",
              "capital_stock": 635401000000,
              "employee_number": 70056,
              "date_of_establishment": "1937-08-28T00:00:00+09:00",
              "founding_year": 1937,
              "company_url": "https://global.toyota/",
              "business_summary": "自動車の製造・販売",
              "industry": ["製造業", ""],
              "update_date": "2026-10-01T00:00:00+09:00"
            }
          ]
        }
        """;

    [Fact]
    public void RegisteredCorporation_GetsCorporationAndReturnsGbizProperties()
    {
        var http = new FakeHttp(_ => Ok(Toyota));

        var results = Run(Script, http, Values((Key, "1180301018771")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "1180301018771", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header("X-hojinInfo-api-token"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["gbiz.corporateNumber"] = "1180301018771",
            ["gbiz.name"] = "トヨタ自動車株式会社",
            ["gbiz.nameEn"] = "TOYOTA MOTOR CORPORATION",
            ["gbiz.kana"] = "トヨタジドウシャ",
            ["gbiz.location"] = "愛知県豊田市トヨタ町1番地",
            ["gbiz.postalCode"] = "4718571",
            ["gbiz.representativeName"] = "佐藤恒治",
            ["gbiz.capitalStock"] = "635401000000",
            ["gbiz.employeeNumber"] = "70056",
            ["gbiz.dateOfEstablishment"] = "1937-08-28",
            ["gbiz.foundingYear"] = "1937",
            ["gbiz.companyUrl"] = "https://global.toyota/",
            ["gbiz.businessSummary"] = "自動車の製造・販売",
            ["gbiz.industry"] = "製造業",
            ["gbiz.updateDate"] = "2026-10-01",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["gbiz.dateOfEstablishment"]);
    }

    [Fact]
    public void ClosedCorporation_StoresStatusAndCloseDate()
    {
        var http = new FakeHttp(_ => Ok("""
            { "hojin-infos": [ { "corporate_number": "7000012050002", "name": "旧株式会社", "status": "閉鎖", "close_date": "2020-03-31T00:00:00+09:00", "close_cause": "清算の結了" } ] }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "7000012050002")), apiKey: ApiKey));

        Assert.Equal(new Dictionary<string, string>
        {
            ["gbiz.corporateNumber"] = "7000012050002",
            ["gbiz.name"] = "旧株式会社",
            ["gbiz.status"] = "閉鎖",
            ["gbiz.closeDate"] = "2020-03-31",
            ["gbiz.closeCause"] = "清算の結了",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("1180301018771")]
    [InlineData("1-1803-0101-8771")]
    [InlineData(" 1180 3010 18771 ")]
    [InlineData("１１８０３０１０１８７７１")] // full-width digits
    public void NormalisesCorporateNumber(string corporateNumber)
    {
        var http = new FakeHttp(_ => Ok(Toyota));

        Run(Script, http, Values((Key, corporateNumber)), apiKey: ApiKey);

        Assert.Equal(Url + "1180301018771", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("118030101877")] // 12 digits
    [InlineData("2180301018771")] // wrong check digit
    [InlineData("T1180301018771")] // invoice registration number, not a corporate number
    [InlineData("トヨタ")]
    public void NoValidCorporateNumber_ReturnsNoResultWithoutCallingTheApi(string corporateNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, corporateNumber)), apiKey: ApiKey);

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

        var results = Run(Script, http, Values((Key, "1180301018771")), apiKey: apiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{ "id": null, "message": "200 - OK.", "errors": [], "hojin-infos": [] }""")]
    [InlineData("NotFound", """{ "id": null, "message": "404 - Not Found.", "errors": [] }""")]
    [InlineData("BadRequest", """{ "id": null, "message": "400 - Bad Request.", "errors": [ { "item": "corporate_number", "message": "invalid" } ] }""")]
    [InlineData("InternalServerError", """{ "id": null, "message": "500 - Internal Server Error.", "errors": [] }""")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "1180301018771")), cache, ApiKey));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same number calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "1180301018771")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void ApiKeyRejected_FailsTheRecordWithoutRetrying(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, """{ "id": null, "message": "401 - Unauthorized", "errors": [] }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "1180301018771")), apiKey: ApiKey));

        Assert.Contains("API token", ex.ToString());
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "1180301018771")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Toyota));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "1180301018771")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "1-1803-0101-8771")), cache, ApiKey);

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
