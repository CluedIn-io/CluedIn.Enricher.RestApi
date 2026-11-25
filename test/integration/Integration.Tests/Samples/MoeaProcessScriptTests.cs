using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Taiwan Ministry of Economic Affairs sample Process Script (docs/samples/moea-taiwan) the
/// same way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class MoeaProcessScriptTests
{
    private const string Key = "organization.unifiedBusinessNumber";
    private const string Url = "https://data.gcis.nat.gov.tw/od/data/api/5F64D864-61CB-4D0D-8AD9-492047CC1EA6?$format=json&$filter=Business_Accounting_NO%20eq%20";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.moea-process-script.js");

    // Real response for 22099131
    private const string Tsmc = """
        [{"Business_Accounting_NO":"22099131","Company_Status_Desc":"核准設立","Company_Name":"台灣積體電路製造股份有限公司","Capital_Stock_Amount":280500000000,"Paid_In_Capital_Amount":259323700670,"Share_Val":10,"Equity_Amt":25932370067,"Responsible_Name":"魏哲家","Company_Location":"新竹科學園區新竹市力行六路8號","Register_Organization_Desc":"國家科學及技術委員會新竹科學園區管理局","Company_Setup_Date":"0760221","Change_Of_Approval_Data":"1150821","Revoke_App_Date":"","Case_Status":"","Case_Status_Desc":"","Sus_App_Date":"","Sus_Beg_Date":"","Sus_End_Date":""}]
        """;

    [Fact]
    public void RegisteredCompany_FiltersByNumberAndReturnsMoeaProperties()
    {
        var http = new FakeHttp(_ => Ok(Tsmc));

        var results = Run(Script, http, Values((Key, "22099131")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "22099131&$skip=0&$top=1", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["moea.unifiedBusinessNumber"] = "22099131",
            ["moea.name"] = "台灣積體電路製造股份有限公司",
            ["moea.status"] = "核准設立",
            ["moea.responsiblePerson"] = "魏哲家",
            ["moea.address"] = "新竹科學園區新竹市力行六路8號",
            ["moea.registrationAuthority"] = "國家科學及技術委員會新竹科學園區管理局",
            ["moea.capital"] = "280500000000",
            ["moea.paidInCapital"] = "259323700670",
            ["moea.setupDate"] = "1987-02-21",
            ["moea.lastChangeDate"] = "2026-08-21",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["moea.setupDate"]);
    }

    [Fact]
    public void RevokedCompany_StoresRevocationAndSuspensionDates()
    {
        var http = new FakeHttp(_ => Ok("""
            [{"Business_Accounting_NO":"04595257","Company_Status_Desc":"撤銷","Company_Name":"擎天金屬股份有限公司","Capital_Stock_Amount":150000000,"Paid_In_Capital_Amount":150000000,"Responsible_Name":"","Company_Location":"桃園市桃園區民生北路１６２號","Register_Organization_Desc":"經濟部商業司","Company_Setup_Date":"0650923","Change_Of_Approval_Data":"0810213","Revoke_App_Date":"0810213","Sus_Beg_Date":"0800101","Sus_End_Date":"0801231"}]
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "04595257"))));

        Assert.Equal("1976-09-23", result.Data["moea.setupDate"].ToString());
        Assert.Equal("1992-02-13", result.Data["moea.revocationDate"].ToString());
        Assert.Equal("1991-01-01", result.Data["moea.suspensionStartDate"].ToString());
        Assert.Equal("1991-12-31", result.Data["moea.suspensionEndDate"].ToString());
        Assert.False(result.Data.ContainsKey("moea.responsiblePerson"));
    }

    [Theory]
    [InlineData("22099131")]
    [InlineData("2209 9131")]
    [InlineData("22-099-131")]
    public void NormalisesNumber(string number)
    {
        var http = new FakeHttp(_ => Ok(Tsmc));

        Run(Script, http, Values((Key, number)));

        Assert.StartsWith(Url + "22099131&", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData("04595257")]
    [InlineData("86517510")]
    // 7th digit 7: valid only because its product may count as 1
    [InlineData("10458574")]
    public void AcceptsValidNumbers(string number)
    {
        var http = new FakeHttp(_ => Ok("[]"));

        Run(Script, http, Values((Key, number)));

        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678")] // fails the check
    [InlineData("2209913")]
    [InlineData("220991311")]
    [InlineData("TW22099131")]
    public void NoValidNumber_ReturnsNoResultWithoutCallingTheApi(string number)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, number)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", "")] // what the API returns for a number that isn't a registered company
    [InlineData("OK", "[]")]
    [InlineData("OK", """[{"Business_Accounting_NO":"86517510","Company_Name":"Another company"}]""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void NotFoundOrApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "22099131")), cache));
        Assert.Single(http.Requests);

        Assert.Empty(Run(Script, http, Values((Key, "22099131")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "22099131"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Tsmc));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "22099131")), cache);
        var second = Run(Script, http, Values((Key, "2209 9131")), cache);

        Assert.Single(http.Requests);
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
