using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the AJPES Business Register sample Process Script (docs/samples/ajpes-slovenia) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class AjpesProcessScriptTests
{
    private const string RegistrationKey = "organization.registrationNumber";
    private const string TaxKey = "organization.taxNumber";
    private const string Url = "https://www.ajpes.si/restPrsInfo";
    private const string ApiKey = "test-user:se:cret";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.ajpes-process-script.js");

    // Follows the PrsInfoResponse_PrsData schema in restPrsInfo's own OpenAPI specification; values are illustrative
    private const string Subject = """
        {
          "status": 0,
          "message": "OK",
          "payload": {
            "raw": null,
            "formatted": {
              "ident": { "status": "OK" },
              "info": {
                "stMaticna": "5860571000",
                "stDavcna": "92719236",
                "idDDV": "SI92719236",
                "bZbrisan": "N",
                "dtUstanovitev": "1991-04-02T00:00:00",
                "dtUstanovitevSpecified": true,
                "dtVpis": "1995-03-27T00:00:00",
                "dtVpisSpecified": true,
                "dtZbrisan": "0001-01-01T00:00:00",
                "dtZbrisanSpecified": false,
                "firma": {
                  "oblika": "Delniška družba",
                  "velikostRS": "velika",
                  "naslovF": { "ulica": "Verovškova ulica", "stHisna": "57", "sifPosta": "1000", "posta": "Ljubljana", "obcina": "Ljubljana" },
                  "imeFirme": [ { "popolnoIme": "Krka, tovarna zdravil, d.d., Novo mesto", "kratkoIme": "Krka, d.d." } ]
                },
                "glavnaDejavnost": { "sifDejavnost": "21.200", "dejavnostOpis": "Proizvodnja farmacevtskih preparatov" },
                "sektor": { "sektorizacija": "Nefinančne družbe" },
                "kapital": { "vreKapital": 54732264.71, "vreKapitalSpecified": true, "valuta": "EUR" }
              }
            }
          }
        }
        """;

    private static string ResponseWithStatus(int status, string message)
        => $$"""{ "payload": { "raw": null, "formatted": null }, "status": {{status}}, "message": "{{message}}", "timestamp": null }""";

    [Fact]
    public void RegistrationNumber_PostsGetAndReturnsAjpesProperties()
    {
        var http = new FakeHttp(_ => Ok(Subject));

        var results = Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "/get", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal("test-user", (string)request.Body["ident"]["uporabnik"]);
        Assert.Equal("se:cret", (string)request.Body["ident"]["geslo"]);
        Assert.Equal("PRS_ON_P", (string)request.Body["ident"]["shema"]);
        Assert.Equal("5860571000", (string)request.Body["maticna"]);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["ajpes.registrationNumber"] = "5860571000",
            ["ajpes.taxNumber"] = "92719236",
            ["ajpes.vatId"] = "SI92719236",
            ["ajpes.name"] = "Krka, tovarna zdravil, d.d., Novo mesto",
            ["ajpes.shortName"] = "Krka, d.d.",
            ["ajpes.legalForm"] = "Delniška družba",
            ["ajpes.address"] = "Verovškova ulica 57, 1000 Ljubljana",
            ["ajpes.municipality"] = "Ljubljana",
            ["ajpes.mainActivityCode"] = "21.200",
            ["ajpes.mainActivity"] = "Proizvodnja farmacevtskih preparatov",
            ["ajpes.sector"] = "Nefinančne družbe",
            ["ajpes.size"] = "velika",
            ["ajpes.capital"] = "54732264.71 EUR",
            ["ajpes.foundationDate"] = "1991-04-02",
            ["ajpes.registrationDate"] = "1995-03-27",
        }, Stringify(result.Data));
        Assert.IsType<string>(result.Data["ajpes.foundationDate"]);
    }

    [Theory]
    [InlineData("5860571000", "5860571000")]
    [InlineData("5860571", "5860571000")]
    [InlineData("5860 571 000", "5860571000")]
    [InlineData("5860571.000", "5860571000")]
    public void NormalisesRegistrationNumber(string input, string expected)
    {
        var http = new FakeHttp(_ => Ok(Subject));

        Run(Script, http, Values((RegistrationKey, input)), apiKey: ApiKey);

        Assert.Equal(expected, (string)Assert.Single(http.Requests).Body["maticna"]);
    }

    [Theory]
    [InlineData("92719236")]
    [InlineData("SI92719236")]
    [InlineData("SI 9271 9236")]
    public void TaxNumberWithoutRegistrationNumber_FindsThenGets(string taxNumber)
    {
        var http = new FakeHttp(r => r.Url.EndsWith("/find")
            ? Ok("""{ "status": 0, "payload": { "formatted": { "prsData": [ { "popolno_ime": "Krka, d.d.", "maticna": "5860571000" } ] } } }""")
            : Ok(Subject));

        var result = Assert.Single(Run(Script, http, Values((TaxKey, taxNumber)), apiKey: ApiKey));

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(Url + "/find", http.Requests[0].Url);
        Assert.Equal("92719236", (string)http.Requests[0].Body["davcna"]);
        Assert.Equal("5860571000", (string)http.Requests[1].Body["maticna"]);
        Assert.Equal("Krka, tovarna zdravil, d.d., Novo mesto", result.Data["ajpes.name"].ToString());
    }

    [Fact]
    public void TaxNumberNotFound_ReturnsNoResult()
    {
        var http = new FakeHttp(_ => Ok("""{ "status": 0, "payload": { "formatted": { "prsData": [] } } }"""));

        Assert.Empty(Run(Script, http, Values((TaxKey, "92719236")), apiKey: ApiKey));
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("123456", null)]
    [InlineData("12345678901", null)]
    [InlineData("SI5860571000", null)]
    [InlineData(null, "1234567")]
    [InlineData(null, "02719236")]
    [InlineData(null, "HR92719236")]
    public void NoValidIdentifier_ReturnsNoResultWithoutCallingTheApi(string registrationNumber, string taxNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((RegistrationKey, registrationNumber), (TaxKey, taxNumber)), apiKey: ApiKey);

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("only-a-username")]
    [InlineData(":password")]
    [InlineData("username:")]
    public void MissingOrIncompleteCredentials_ReturnNoResultWithoutCallingTheApi(string apiKey)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: apiKey));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void RejectedUser_FailsTheRecordWithoutRetrying()
    {
        // Real answer from restPrsInfo for a wrong username or password
        var http = new FakeHttp(_ => Ok(ResponseWithStatus(4099, "Napaka pri avtentikaciji uporabnika. Podrobnosti: Napačno uporabniško ime ali geslo.")));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: ApiKey));

        Assert.Contains("rejected the user", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void UnknownDataScheme_FailsTheRecordWithoutRetrying()
    {
        // Real answer from restPrsInfo for a scheme the user doesn't have
        var http = new FakeHttp(_ => Ok(ResponseWithStatus(4010, "Napaka pri validaciji vhodnih podatkov! Napačna šifra storitve: 'PRS_ON_P'.")));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: ApiKey));

        Assert.Contains("DATA_SCHEME", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", """{ "payload": { "raw": null, "formatted": null }, "status": 4040, "message": "Subjekt ne obstaja." }""")]
    [InlineData("OK", """{ "payload": { "raw": null, "formatted": null }, "status": 4010, "message": "Napačna matična številka." }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndCachesNothing(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((RegistrationKey, "5860571000")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((RegistrationKey, "5860571000")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("ServiceUnavailable")]
    public void Busy_ThrowsErrorTheEnricherRetries(string httpStatus)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: ApiKey));

        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void DeletedSubject_StoresDeletedAndDeletionDate()
    {
        var deleted = Subject
            .Replace("\"bZbrisan\": \"N\"", "\"bZbrisan\": \"D\"")
            .Replace("\"dtZbrisan\": \"0001-01-01T00:00:00\"", "\"dtZbrisan\": \"2024-03-09T00:00:00\"")
            .Replace("\"dtZbrisanSpecified\": false", "\"dtZbrisanSpecified\": true");
        var http = new FakeHttp(_ => Ok(deleted));

        var result = Assert.Single(Run(Script, http, Values((RegistrationKey, "5860571000")), apiKey: ApiKey));

        Assert.Equal("True", result.Data["ajpes.deleted"].ToString());
        Assert.Equal("2024-03-09", result.Data["ajpes.deletionDate"].ToString());
    }

    [Fact]
    public void SameSubjectAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Subject));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((RegistrationKey, "5860571000")), cache, ApiKey);
        var second = Run(Script, http, Values((RegistrationKey, "5860571")), cache, ApiKey);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [RegistrationKey, TaxKey], ApiKey);

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
