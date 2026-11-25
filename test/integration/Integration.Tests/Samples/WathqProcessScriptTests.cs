using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Wathq (Saudi Arabia) sample Process Script (docs/samples/wathq-saudi-arabia) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class WathqProcessScriptTests
{
    private const string Key = "organization.crNumber";
    private const string Url = "https://api.wathq.sa/commercial-registration/fullinfo/";
    private const string ApiKey = "test-api-key";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.wathq-process-script.js");

    // Illustrative full information response, with the field names of the Wathq fullinfo model
    private const string Registration = """
        {
          "crName": "Example Trading Company",
          "crNumber": "1010000000",
          "crEntityNumber": "7000000000",
          "issueDate": "1431/06/18",
          "expiryDate": "1448/06/18",
          "crMainNumber": "1010000000",
          "businessType": { "id": "201", "name": "شركة ذات مسؤولية محدودة", "nameEn": "Limited Liability Company" },
          "status": { "id": "active", "name": "نشط", "nameEn": "Active" },
          "location": { "id": "1010", "name": "Riyadh" },
          "isEcommerce": false,
          "capital": { "paidAmount": 500000, "subscribedAmount": 500000, "announcedAmount": 500000 },
          "address": {
            "general": { "website": "www.example.sa", "email": "info@example.sa", "telephone1": "0110000000", "fax1": "" },
            "national": { "buildingNumber": "1234", "streetName": "King Fahd Road", "city": "Riyadh", "zipcode": "12345",
                          "district": { "id": "1", "name": "Al Olaya" } }
          },
          "activities": { "description": "Trading", "isic": [ { "id": "4610", "name": "وكلاء البيع", "nameEn": "Wholesale on a fee or contract basis" }, { "id": "4690", "name": "تجارة الجملة غير المتخصصة" } ] }
        }
        """;

    [Fact]
    public void Registration_GetsFullInfoAndReturnsWathqProperties()
    {
        var http = new FakeHttp(_ => Ok(Registration));

        var results = Run(Script, http, Values((Key, "1010000000")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "1010000000?language=en", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal(ApiKey, request.Header("apiKey"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["wathq.crNumber"] = "1010000000",
            ["wathq.name"] = "Example Trading Company",
            ["wathq.entityNumber"] = "7000000000",
            ["wathq.mainCrNumber"] = "1010000000",
            ["wathq.businessType"] = "Limited Liability Company",
            ["wathq.status"] = "Active",
            ["wathq.issueDate"] = "1431/06/18",
            ["wathq.expiryDate"] = "1448/06/18",
            ["wathq.city"] = "Riyadh",
            ["wathq.address"] = "1234 King Fahd Road, Al Olaya, Riyadh 12345",
            ["wathq.website"] = "www.example.sa",
            ["wathq.email"] = "info@example.sa",
            ["wathq.phone"] = "0110000000",
            ["wathq.activities"] = "Wholesale on a fee or contract basis; تجارة الجملة غير المتخصصة",
            ["wathq.paidCapital"] = "500000",
            ["wathq.ecommerce"] = "False",
        }, Stringify(result.Data));
    }

    [Fact]
    public void ArabicOnlyNamesAndTimestamps_AreHandled()
    {
        var http = new FakeHttp(_ => Ok("""
            { "crNumber": "1010000000", "crName": "مؤسسة مثال", "status": { "name": "نشط" }, "issueDate": "2010-06-01T00:00:00", "address": {} }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "1010000000")), apiKey: ApiKey));

        Assert.Equal("نشط", result.Data["wathq.status"]);
        Assert.Equal("2010-06-01", result.Data["wathq.issueDate"]);
        Assert.False(result.Data.ContainsKey("wathq.address"));
        Assert.False(result.Data.ContainsKey("wathq.ecommerce"));
    }

    [Theory]
    [InlineData("1010000000")]
    [InlineData("1010 000 000")]
    [InlineData("CR 1010000000")]
    [InlineData("c.r. 1010-000-000")]
    [InlineData("١٠١٠٠٠٠٠٠٠")]
    public void NormalisesCrNumber(string crNumber)
    {
        var http = new FakeHttp(_ => Ok(Registration));

        Run(Script, http, Values((Key, crNumber)), apiKey: ApiKey);

        Assert.Equal(Url + "1010000000?language=en", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("101000000")]
    [InlineData("10100000000")]
    [InlineData("0010000000")]
    [InlineData("CR")]
    [InlineData("101000000X")]
    public void NoValidCrNumber_ReturnsNoResultWithoutCallingTheApi(string crNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, crNumber)), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoApiKey_ReturnsNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "1010000000")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("NotFound", """{"code":"404.2.1","message":"No Results Found"}""")]
    [InlineData("BadRequest", """{"code":"400.1.1","message":"Input data is required"}""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "1010000000")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((Key, "1010000000")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("Unauthorized", """{"code":"401.1.1","message":"Invalid ApiKey"}""")]
    [InlineData("Forbidden", "")]
    public void ApiKeyRejected_ThrowsErrorThatIsNotRetried(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "1010000000")), apiKey: "wrong"));

        Assert.Contains("API key", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "1010000000")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Registration));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "1010000000")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "CR 1010 000 000")), cache, ApiKey);

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
