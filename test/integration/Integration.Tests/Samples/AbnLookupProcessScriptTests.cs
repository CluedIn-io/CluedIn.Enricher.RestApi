using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the ABN Lookup sample Process Script (docs/samples/abn-lookup-australia) the same way the
/// V2 REST API enricher does, against a fake <c>http</c> object instead of the real service.
/// </summary>
public class AbnLookupProcessScriptTests
{
    private const string Key = "organization.abn";
    private const string Guid = "11111111-2222-3333-4444-555555555555";
    private const string Url = "https://abr.business.gov.au/json/AbnDetails.aspx";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.abn-process-script.js");

    // ABN Lookup answers with JSONP
    private const string Found = """
        callback({"Abn":"51824753556","AbnStatus":"Active","AbnStatusEffectiveFrom":"2000-07-01","Acn":"","AddressDate":"2014-09-12","AddressPostcode":"2600","AddressState":"ACT","BusinessName":["ATO","Australian Tax Office"],"EntityName":"AUSTRALIAN TAXATION OFFICE","EntityTypeCode":"CGE","EntityTypeName":"Commonwealth Government Entity","Gst":"2000-07-01","Message":""})
        """;

    private static string Error(string message) =>
        $$"""callback({"Abn":"","AbnStatus":"","AbnStatusEffectiveFrom":"","Acn":"","AddressDate":null,"AddressPostcode":"","AddressState":"","BusinessName":[],"EntityName":"","EntityTypeCode":"","EntityTypeName":"","Gst":null,"Message":"{{message}}"})""";

    [Fact]
    public void ActiveAbn_CallsAbnLookupWithGuidAndReturnsAbnProperties()
    {
        var http = new FakeHttp(_ => Ok(Found));

        var results = Run(Script, http, Values((Key, "51824753556")), apiKey: Guid);

        var request = Assert.Single(http.Requests);
        Assert.Equal($"{Url}?abn=51824753556&callback=callback&guid={Guid}", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["abn.abn"] = "51824753556",
            ["abn.status"] = "Active",
            ["abn.statusEffectiveFrom"] = "2000-07-01",
            ["abn.entityName"] = "AUSTRALIAN TAXATION OFFICE",
            ["abn.entityType"] = "Commonwealth Government Entity",
            ["abn.entityTypeCode"] = "CGE",
            ["abn.businessNames"] = "ATO; Australian Tax Office",
            ["abn.state"] = "ACT",
            ["abn.postcode"] = "2600",
            ["abn.gstRegisteredFrom"] = "2000-07-01",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["abn.gstRegisteredFrom"]);
    }

    [Theory]
    [InlineData("51824753556")]
    [InlineData("51 824 753 556")]
    [InlineData("ABN 51 824 753 556")]
    [InlineData("abn51-824-753-556")]
    public void NormalisesAbn(string abn)
    {
        var http = new FakeHttp(_ => Ok(Found));

        Run(Script, http, Values((Key, abn)), apiKey: Guid);

        Assert.StartsWith($"{Url}?abn=51824753556&", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("51824753557")] // fails the modulus 89 check
    [InlineData("051824753556")]
    public void NoValidAbn_ReturnsNoResultWithoutCallingAbnLookup(string abn)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, abn)), apiKey: Guid));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoGuid_ReturnsNoResultWithoutCallingAbnLookup(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "51824753556")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void GuidRejected_ThrowsErrorThatFailsTheRecord()
    {
        var http = new FakeHttp(_ => Ok(Error("The GUID entered is not recognised as a Registered Party")));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "51824753556")), apiKey: Guid));

        Assert.Contains("GUID", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", "Search text is not a valid ABN or ACN")]
    [InlineData("OK", "")] // ABN not found: empty details
    public void AbnLookupReportsNoAbn_ReturnsNoResultAndIsNotCached(string httpStatus, string message)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, Error(message)));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "51824753556")), cache, Guid));
        Assert.Empty(Run(Script, http, Values((Key, "51824753556")), cache, Guid));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("InternalServerError", "Server Error")]
    [InlineData("GatewayTimeout", "")]
    [InlineData("OK", "not jsonp")]
    public void AbnLookupCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        Assert.Empty(Run(Script, http, Values((Key, "51824753556")), apiKey: Guid));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("ServiceUnavailable")]
    public void AbnLookupBusy_ThrowsErrorTheEnricherRetries(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "51824753556")), apiKey: Guid));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameAbnAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Found));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "51824753556")), cache, Guid);
        var second = Run(Script, http, Values((Key, "51 824 753 556")), cache, Guid);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingAbnLookup()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], Guid);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
