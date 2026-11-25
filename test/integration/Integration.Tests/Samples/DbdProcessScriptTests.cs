using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Open-DBD (Thailand) sample Process Script (docs/samples/dbd-thailand) the same way the
/// V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class DbdProcessScriptTests
{
    private const string Key = "organization.juristicId";
    private const string Url = "https://openapi.dbd.go.th/api/v1/juristic_person/";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.dbd-process-script.js");

    // A response for 0107544000108 (PTT Public Company Limited), in the shape the API returns
    private const string Ptt = """
        {
          "status": { "code": "1000", "description": "Success" },
          "data": [
            {
              "cd:OrganizationJuristicPerson": {
                "cd:OrganizationJuristicID": "0107544000108",
                "cd:OrganizationJuristicNameTH": "บริษัท ปตท. จำกัด (มหาชน)",
                "cd:OrganizationJuristicNameEN": "PTT PUBLIC COMPANY LIMITED",
                "cd:OrganizationJuristicType": "บริษัทมหาชนจำกัด",
                "cd:OrganizationJuristicRegisterDate": "20011001",
                "cd:OrganizationJuristicStatus": "ยังดำเนินกิจการอยู่",
                "cd:OrganizationJuristicObjective": {
                  "td:JuristicObjective": {
                    "td:JuristicObjectiveCode": "71209",
                    "td:JuristicObjectiveTextTH": "การทดสอบและวิเคราะห์ทางเทคนิคอื่นๆซึ่งมิได้จัดประเภท ไว้ในที่อื่น",
                    "td:JuristicObjectiveTextEN": "Other technical testing and analysis, not elsewhere classified"
                  }
                },
                "cd:OrganizationJuristicRegisterCapital": "28562996250.0",
                "cd:OrganizationJuristicPaidUpCapital": "28562996250.00",
                "cd:OrganizationJuristicBranchName": "สำนักงานใหญ่",
                "cd:OrganizationJuristicAddress": {
                  "cr:AddressType": {
                    "cd:Address": "555 ถนนวิภาวดีรังสิต ",
                    "cd:Building": null,
                    "cd:AddressNo": "555",
                    "cd:Road": "วิภาวดีรังสิต",
                    "cd:CitySubDivision": { "cr:CitySubDivisionCode": "10300500", "cr:CitySubDivisionTextTH": "จตุจักร" },
                    "cd:City": { "cr:CityCode": "1030", "cr:CityTextTH": "เขตจตุจักร" },
                    "cd:CountrySubDivision": { "cr:CountrySubDivisionCode": "TH-10", "cr:CountrySubDivisionTextTH": "กรุงเทพมหานคร" }
                  }
                }
              }
            }
          ]
        }
        """;

    private const string IncapsulaPage = """<html style="height:100%"><head><META NAME="ROBOTS" CONTENT="NOINDEX, NOFOLLOW"></head><body><iframe>Request unsuccessful. Incapsula incident ID: 1</iframe></body></html>""";

    [Fact]
    public void RegisteredJuristicPerson_GetsItAndReturnsDbdProperties()
    {
        var http = new FakeHttp(_ => Ok(Ptt));

        var results = Run(Script, http, Values((Key, "0107544000108")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "0107544000108", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.False(string.IsNullOrWhiteSpace(request.Header("User-Agent")));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["dbd.juristicId"] = "0107544000108",
            ["dbd.name"] = "บริษัท ปตท. จำกัด (มหาชน)",
            ["dbd.nameEn"] = "PTT PUBLIC COMPANY LIMITED",
            ["dbd.juristicType"] = "บริษัทมหาชนจำกัด",
            ["dbd.status"] = "ยังดำเนินกิจการอยู่",
            ["dbd.registrationDate"] = "2001-10-01",
            ["dbd.registeredCapital"] = "28562996250",
            ["dbd.paidUpCapital"] = "28562996250",
            ["dbd.objectiveCode"] = "71209",
            ["dbd.objective"] = "Other technical testing and analysis, not elsewhere classified",
            ["dbd.branchName"] = "สำนักงานใหญ่",
            ["dbd.address"] = "555 ถนนวิภาวดีรังสิต, จตุจักร, เขตจตุจักร, กรุงเทพมหานคร",
            ["dbd.province"] = "กรุงเทพมหานคร",
            ["dbd.provinceCode"] = "TH-10",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["dbd.registrationDate"]);
    }

    [Fact]
    public void BuddhistEraDate_IsConvertedAndMissingEnglishTextFallsBackToThai()
    {
        var http = new FakeHttp(_ => Ok("""
            {
              "status": { "code": "1000", "description": "Success" },
              "data": [ { "cd:OrganizationJuristicPerson": {
                "cd:OrganizationJuristicID": "0107544000108",
                "cd:OrganizationJuristicNameTH": "บริษัท ตัวอย่าง จำกัด",
                "cd:OrganizationJuristicNameEN": "",
                "cd:OrganizationJuristicRegisterDate": "25441001",
                "cd:OrganizationJuristicObjective": { "td:JuristicObjective": { "td:JuristicObjectiveCode": "46900", "td:JuristicObjectiveTextTH": "การขายส่งสินค้าทั่วไป", "td:JuristicObjectiveTextEN": null } }
              } } ]
            }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "0107544000108"))));

        Assert.Equal(new Dictionary<string, string>
        {
            ["dbd.juristicId"] = "0107544000108",
            ["dbd.name"] = "บริษัท ตัวอย่าง จำกัด",
            ["dbd.registrationDate"] = "2001-10-01",
            ["dbd.objectiveCode"] = "46900",
            ["dbd.objective"] = "การขายส่งสินค้าทั่วไป",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("0107544000108")]
    [InlineData("0 1075 44000 10 8")]
    [InlineData("0107-544-000-108")]
    [InlineData(" 0107544000108 ")]
    public void NormalisesJuristicId(string juristicId)
    {
        var http = new FakeHttp(_ => Ok(Ptt));

        Run(Script, http, Values((Key, juristicId)));

        Assert.Equal(Url + "0107544000108", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("010754400010")] // 12 digits
    [InlineData("0107544000107")] // wrong check digit
    [InlineData("TH0107544000108")]
    public void NoValidJuristicId_ReturnsNoResultWithoutCallingTheApi(string juristicId)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, juristicId)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{ "status": { "code": "1004", "description": "Data not found" }, "data": [] }""")]
    [InlineData("OK", """{ "status": { "code": "1051", "description": "Invalid juristic id format" } }""")]
    [InlineData("OK", """{ "status": { "code": "1000", "description": "Success" }, "data": [] }""")]
    [InlineData("NotFound", "")]
    [InlineData("Forbidden", IncapsulaPage)]
    [InlineData("InternalServerError", """{ "status": { "code": "9999", "description": "Error" } }""")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "0107544000108")), cache));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same number calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "0107544000108")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "0107544000108"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Ptt));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "0107544000108")), cache);
        var second = Run(Script, http, Values((Key, "0107-544-000-108")), cache);

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
