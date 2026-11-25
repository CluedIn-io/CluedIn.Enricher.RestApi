using System;
using System.Collections.Generic;
using Jint.Runtime;
using Newtonsoft.Json.Linq;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Israeli Companies Registrar sample Process Script (docs/samples/companies-registrar-israel)
/// the same way the V2 REST API enricher does, against a fake <c>http</c> object instead of data.gov.il.
/// </summary>
public class IlcrProcessScriptTests
{
    private const string Key = "organization.companyNumber";
    private const string Url = "https://data.gov.il/api/3/action/datastore_search";
    private const string ResourceId = "f004176c-b85f-4542-8901-7b3176f9a054";
    private const string NumberField = "מספר חברה";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.ilcr-process-script.js");

    // Shortened from a real response for 520013954
    private const string Teva = """
        {
          "success": true,
          "result": {
            "resource_id": "f004176c-b85f-4542-8901-7b3176f9a054",
            "records": [{
              "_id": 494599,
              "מספר חברה": 520013954,
              "שם חברה": "טבע תעשיות פרמצבטיות בע~מ",
              "שם באנגלית": "TEVA PHARMACEUTICAL INDUSTRIES LIMITED",
              "סוג תאגיד": "ישראלית חברה ציבורית",
              "סטטוס חברה": "פעילה",
              "תאור חברה": "",
              "מטרת החברה": "לעסוק בסוגי עיסוק שפורטו בתקנון",
              "תאריך התאגדות": "13/02/1944",
              "חברה ממשלתית": "לא",
              "מגבלות": "מוגבלת",
              "מפרה": "",
              "שנה אחרונה של דוח שנתי (שהוגש)": 2006,
              "שם עיר": "תל אביב - יפו",
              "שם רחוב": "דבורה הנביאה",
              "מספר בית": "124",
              "מיקוד": 6944020,
              "ת.ד.": "",
              "מדינה": "ישראל",
              "תת סטטוס": ""
            }],
            "total": 1
          }
        }
        """;

    private const string NoRecords = """{ "success": true, "result": { "records": [], "total": 0 } }""";

    [Fact]
    public void RegisteredCompany_QueriesByExactNumberAndReturnsIlcrProperties()
    {
        var http = new FakeHttp(_ => Ok(Teva));

        var results = Run(Script, http, Values((Key, "520013954")));

        var request = Assert.Single(http.Requests);
        Assert.Equal("GET", request.Method);
        var uri = new Uri(request.Url);
        Assert.Equal(Url, uri.GetLeftPart(UriPartial.Path));
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(ResourceId, query["resource_id"]);
        Assert.Equal(520013954, (long)JObject.Parse(query["filters"])[NumberField]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["ilcr.companyNumber"] = "520013954",
            ["ilcr.name"] = "טבע תעשיות פרמצבטיות בע\"מ",
            ["ilcr.nameEn"] = "TEVA PHARMACEUTICAL INDUSTRIES LIMITED",
            ["ilcr.companyType"] = "ישראלית חברה ציבורית",
            ["ilcr.status"] = "פעילה",
            ["ilcr.purpose"] = "לעסוק בסוגי עיסוק שפורטו בתקנון",
            ["ilcr.limitation"] = "מוגבלת",
            ["ilcr.lastAnnualReportYear"] = "2006",
            ["ilcr.city"] = "תל אביב - יפו",
            ["ilcr.postalCode"] = "6944020",
            ["ilcr.country"] = "ישראל",
            ["ilcr.incorporationDate"] = "1944-02-13",
            ["ilcr.governmentCompany"] = "False",
            ["ilcr.address"] = "דבורה הנביאה 124, תל אביב - יפו, 6944020",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["ilcr.incorporationDate"]);
    }

    [Theory]
    [InlineData("520013954")]
    [InlineData("52-001395-4")]
    [InlineData("520 013 954")]
    [InlineData("520.013.954")]
    public void NormalisesCompanyNumber(string companyNumber)
    {
        var http = new FakeHttp(_ => Ok(Teva));

        Assert.Single(Run(Script, http, Values((Key, companyNumber))));

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(Assert.Single(http.Requests).Url).Query);
        Assert.Equal(520013954, (long)JObject.Parse(query["filters"])[NumberField]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("52001395")] // 8 digits
    [InlineData("520013955")] // wrong check digit
    [InlineData("320013954")] // doesn't start with 5
    [InlineData("IL520013954")]
    public void NoValidCompanyNumber_ReturnsNoResultWithoutCallingTheApi(string companyNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, companyNumber))));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("כן", "True")]
    [InlineData("לא", "False")]
    public void GovernmentCompany_IsStoredAsBoolean(string value, string expected)
    {
        var http = new FakeHttp(_ => Ok(Teva.Replace("\"חברה ממשלתית\": \"לא\"", $"\"חברה ממשלתית\": \"{value}\"")));

        var result = Assert.Single(Run(Script, http, Values((Key, "520013954"))));

        Assert.Equal(expected, result.Data["ilcr.governmentCompany"].ToString());
    }

    [Theory]
    [InlineData("OK", NoRecords)]
    [InlineData("OK", """{ "success": false, "error": { "message": "Not found" } }""")]
    [InlineData("NotFound", "<!DOCTYPE html><html></html>")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiHasNoAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "520013954")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "520013954")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void OtherCompanyInResponse_IsNotUsed()
    {
        var http = new FakeHttp(_ => Ok(Teva));

        // 520000118 is valid, but the (fake) response is for 520013954
        Assert.Empty(Run(Script, http, Values((Key, "520000118"))));
    }

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("ServiceUnavailable")]
    public void ApiBusy_ThrowsErrorTheEnricherRetries(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "520013954"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Teva));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "520013954")), cache);
        var second = Run(Script, http, Values((Key, "52-001395-4")), cache);

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
