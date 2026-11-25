using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the Brønnøysund Register Centre sample Process Script (docs/samples/brreg-norway) the same
/// way the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class BrregProcessScriptTests
{
    private const string Key = "organization.organizationNumber";
    private const string Url = "https://data.brreg.no/enhetsregisteret/api/enheter/";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.brreg-process-script.js");

    // Shortened from a real response for 923609016
    private const string Equinor = """
        {
          "organisasjonsnummer": "923609016",
          "navn": "EQUINOR ASA",
          "organisasjonsform": { "kode": "ASA", "beskrivelse": "Allmennaksjeselskap" },
          "hjemmeside": "www.equinor.com",
          "postadresse": { "land": "Norge", "landkode": "NO", "postnummer": "4035", "poststed": "STAVANGER", "adresse": ["Postboks 8500"], "kommune": "STAVANGER", "kommunenummer": "1103" },
          "registreringsdatoEnhetsregisteret": "1995-03-12",
          "registrertIMvaregisteret": true,
          "naeringskode1": { "kode": "06.100", "beskrivelse": "Utvinning av råolje" },
          "antallAnsatte": 21182,
          "forretningsadresse": { "land": "Norge", "landkode": "NO", "postnummer": "4035", "poststed": "STAVANGER", "adresse": ["Forusbeen 50"], "kommune": "STAVANGER", "kommunenummer": "1103" },
          "stiftelsesdato": "1972-09-18",
          "konkurs": false,
          "underAvvikling": false,
          "respons_klasse": "Enhet"
        }
        """;

    [Fact]
    public void RegisteredEntity_GetsEntityAndReturnsBrregProperties()
    {
        var http = new FakeHttp(_ => Ok(Equinor));

        var results = Run(Script, http, Values((Key, "923609016")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "923609016", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["brreg.organizationNumber"] = "923609016",
            ["brreg.name"] = "EQUINOR ASA",
            ["brreg.organizationForm"] = "Allmennaksjeselskap",
            ["brreg.organizationFormCode"] = "ASA",
            ["brreg.businessAddress"] = "Forusbeen 50, 4035 STAVANGER",
            ["brreg.postalAddress"] = "Postboks 8500, 4035 STAVANGER",
            ["brreg.municipality"] = "STAVANGER",
            ["brreg.countryCode"] = "NO",
            ["brreg.industryCode"] = "06.100",
            ["brreg.industry"] = "Utvinning av råolje",
            ["brreg.employees"] = "21182",
            ["brreg.website"] = "www.equinor.com",
            ["brreg.foundationDate"] = "1972-09-18",
            ["brreg.registrationDate"] = "1995-03-12",
            ["brreg.vatRegistered"] = "True",
            ["brreg.bankrupt"] = "False",
            ["brreg.underLiquidation"] = "False",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["brreg.registrationDate"]);
    }

    [Theory]
    [InlineData("923609016")]
    [InlineData("923 609 016")]
    [InlineData("NO 923 609 016 MVA")]
    [InlineData("no923609016mva")]
    [InlineData("923.609.016")]
    public void NormalisesOrganizationNumber(string organizationNumber)
    {
        var http = new FakeHttp(_ => Ok(Equinor));

        Run(Script, http, Values((Key, organizationNumber)));

        Assert.Equal(Url + "923609016", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("923609017")] // wrong check digit
    [InlineData("123456785")] // doesn't start with 8 or 9
    [InlineData("SE556703748501")]
    public void NoValidOrganizationNumber_ReturnsNoResultWithoutCallingTheApi(string organizationNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, organizationNumber)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void DeletedEntity_StoresDeletedAndDeletionDate()
    {
        var http = new FakeHttp(_ => Ok("""
            { "organisasjonsnummer": "923609016", "navn": "OLD COMPANY AS", "organisasjonsform": { "kode": "AS", "beskrivelse": "Aksjeselskap" }, "slettedato": "2024-03-09", "respons_klasse": "SlettetEnhet" }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "923609016"))));

        Assert.Equal(new Dictionary<string, string>
        {
            ["brreg.organizationNumber"] = "923609016",
            ["brreg.name"] = "OLD COMPANY AS",
            ["brreg.organizationForm"] = "Aksjeselskap",
            ["brreg.organizationFormCode"] = "AS",
            ["brreg.deleted"] = "True",
            ["brreg.deletionDate"] = "2024-03-09",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("NotFound", "")]
    [InlineData("Gone", """{ "organisasjonsnummer": "923609016", "slettedato": "2024-03-09" }""")]
    [InlineData("BadRequest", """{ "status": 400, "feilmelding": "Feilaktig forespørsel" }""")]
    [InlineData("InternalServerError", """{ "status": 500 }""")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResult(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "923609016")), cache));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same number calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "923609016")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "923609016"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNumberAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Equinor));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "923609016")), cache);
        var second = Run(Script, http, Values((Key, "923 609 016")), cache);

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
