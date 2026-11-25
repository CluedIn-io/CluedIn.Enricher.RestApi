using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the OpenDART sample Process Script (docs/samples/opendart-south-korea) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class OpendartProcessScriptTests
{
    private const string Key = "organization.dartCorpCode";
    private const string Url = "https://opendart.fss.or.kr/api/company.json";
    private const string ApiKey = "test-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.opendart-process-script.js");

    // Shaped like OpenDART's documented company.json response
    private const string Samsung = """
        {
          "status": "000",
          "message": "정상",
          "corp_code": "00126380",
          "corp_name": "삼성전자(주)",
          "corp_name_eng": "SAMSUNG ELECTRONICS CO,.LTD",
          "stock_name": "삼성전자",
          "stock_code": "005930",
          "ceo_nm": "전영현, 노태문",
          "corp_cls": "Y",
          "jurir_no": "1301110006246",
          "bizr_no": "1248100998",
          "adres": "경기도 수원시 영통구  삼성로 129 (매탄동)",
          "hm_url": "www.samsung.com/sec",
          "ir_url": "",
          "phn_no": "02-2255-0114",
          "fax_no": "031-200-7538",
          "induty_code": "264",
          "est_dt": "19690113",
          "acc_mt": "12"
        }
        """;

    [Fact]
    public void RegisteredCompany_GetsOverviewAndReturnsDartProperties()
    {
        var http = new FakeHttp(_ => Ok(Samsung));

        var results = Run(Script, http, Values((Key, "00126380")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal($"{Url}?crtfc_key={ApiKey}&corp_code=00126380", request.Url);
        Assert.Equal("GET", request.Method);
        // OpenDART redirects requests without a User-Agent (.NET's HttpClient sends none) to an HTML error page
        Assert.False(string.IsNullOrWhiteSpace(request.Header("User-Agent")));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["dart.corpCode"] = "00126380",
            ["dart.name"] = "삼성전자(주)",
            ["dart.nameEn"] = "SAMSUNG ELECTRONICS CO,.LTD",
            ["dart.stockName"] = "삼성전자",
            ["dart.stockCode"] = "005930",
            ["dart.ceoName"] = "전영현, 노태문",
            ["dart.corporationClass"] = "KOSPI",
            ["dart.corporateRegistrationNumber"] = "1301110006246",
            ["dart.businessRegistrationNumber"] = "1248100998",
            ["dart.address"] = "경기도 수원시 영통구  삼성로 129 (매탄동)",
            ["dart.website"] = "www.samsung.com/sec",
            ["dart.phone"] = "02-2255-0114",
            ["dart.fax"] = "031-200-7538",
            ["dart.industryCode"] = "264",
            ["dart.establishmentDate"] = "1969-01-13",
            ["dart.fiscalYearEndMonth"] = "12",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["dart.establishmentDate"]);
    }

    [Theory]
    [InlineData("Y", "KOSPI")]
    [InlineData("K", "KOSDAQ")]
    [InlineData("N", "KONEX")]
    [InlineData("E", "Other")]
    public void MapsCorporationClass(string corpCls, string expected)
    {
        var http = new FakeHttp(_ => Ok($$"""{ "status": "000", "message": "정상", "corp_code": "00126380", "corp_name": "회사", "corp_cls": "{{corpCls}}" }"""));

        var result = Assert.Single(Run(Script, http, Values((Key, "00126380")), apiKey: ApiKey));

        Assert.Equal(expected, result.Data["dart.corporationClass"].ToString());
    }

    [Theory]
    [InlineData("00126380")]
    [InlineData("0012 6380")]
    [InlineData("0012-6380")]
    [InlineData(" 00126380 ")]
    public void NormalisesCorpCode(string corpCode)
    {
        var http = new FakeHttp(_ => Ok(Samsung));

        Run(Script, http, Values((Key, corpCode)), apiKey: ApiKey);

        Assert.EndsWith("&corp_code=00126380", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000000")]
    [InlineData("123456789")] // too long
    [InlineData("126380")] // too short: could be a stock code (005930) as easily as a corporation code that lost its zeros
    [InlineData("124-81-00998")] // business registration number, not a DART corporation code
    [InlineData("005930A")]
    public void NoValidCorpCode_ReturnsNoResultWithoutCallingTheApi(string corpCode)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, corpCode)), apiKey: ApiKey);

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

        var results = Run(Script, http, Values((Key, "00126380")), apiKey: apiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{ "status": "013", "message": "조회된 데이타가 없습니다." }""")]
    [InlineData("OK", """{ "status": "100", "message": "필드의 부적절한 값입니다." }""")]
    [InlineData("OK", """{ "status": "800", "message": "시스템 점검으로 인한 서비스가 중지 중입니다." }""")]
    [InlineData("OK", """{ "status": "900", "message": "정의되지 않은 오류가 발생하였습니다." }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "00126380")), cache, ApiKey));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same code calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "00126380")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("010")]
    [InlineData("011")]
    [InlineData("012")]
    [InlineData("901")]
    public void ApiKeyRejected_FailsTheRecordWithoutRetrying(string dartStatus)
    {
        // OpenDART reports key problems with HTTP 200 and its own status code
        var http = new FakeHttp(_ => Ok($$"""{ "status": "{{dartStatus}}", "message": "등록되지 않은 인증키입니다." }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "00126380")), apiKey: ApiKey));

        Assert.Contains("API key", ex.ToString());
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", """{ "status": "020", "message": "요청 제한을 초과하였습니다." }""")]
    [InlineData("TooManyRequests", "")]
    public void RequestLimitReached_ThrowsErrorTheEnricherRetries(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "00126380")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameCodeAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Samsung));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "00126380")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "0012 6380")), cache, ApiKey);

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
