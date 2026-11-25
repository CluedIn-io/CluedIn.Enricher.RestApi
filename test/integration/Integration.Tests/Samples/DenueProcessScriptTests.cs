using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the INEGI DENUE sample Process Script (docs/samples/denue-mexico) the same way the V2 REST
/// API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class DenueProcessScriptTests
{
    private const string Key = "organization.denueId";
    private const string Url = "https://www.inegi.org.mx/app/api/denue/v1/consulta/Ficha/";
    private const string ApiKey = "11111111-2222-3333-4444-555555555555";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.denue-process-script.js");

    // The Ficha example response from INEGI's API documentation
    private const string Loncheria = """
        [{
          "CLEE": "01001722514004531",
          "Id": "34185",
          "Nombre": "LONCHERÍA LOS CUATES",
          "Razon_social": "",
          "Clase_actividad": "Restaurantes con servicio de preparación de tacos y tortas",
          "Estrato": "0 a 5 personas",
          "Tipo_vialidad": "CALLE",
          "Calle": "INEGI",
          "Num_Exterior": "201",
          "Num_Interior": "",
          "Colonia": "JARDIN DE LAS BUGAMBILIAS",
          "CP": "20280",
          "Ubicacion": "AGUASCALIENTES, Aguascalientes, AGUASCALIENTES",
          "Telefono": "4492214831",
          "Correo_e": "",
          "Sitio_internet": "",
          "Tipo": "Fijo",
          "Longitud": "-102.28485913",
          "Latitud": "21.85796749",
          "CentroComercial": "",
          "TipoCentroComercial": "",
          "NumLocal": ""
        }]
        """;

    [Fact]
    public void KnownEstablishment_GetsFichaAndReturnsDenueProperties()
    {
        var http = new FakeHttp(_ => Ok(Loncheria));

        var results = Run(Script, http, Values((Key, "34185")), apiKey: ApiKey);

        var request = Assert.Single(http.Requests);
        Assert.Equal($"{Url}34185/{ApiKey}", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["denue.id"] = "34185",
            ["denue.clee"] = "01001722514004531",
            ["denue.name"] = "LONCHERÍA LOS CUATES",
            ["denue.activity"] = "Restaurantes con servicio de preparación de tacos y tortas",
            ["denue.employeeRange"] = "0 a 5 personas",
            ["denue.address"] = "CALLE INEGI 201, JARDIN DE LAS BUGAMBILIAS, 20280",
            ["denue.postalCode"] = "20280",
            ["denue.location"] = "AGUASCALIENTES, Aguascalientes, AGUASCALIENTES",
            ["denue.phone"] = "4492214831",
            ["denue.establishmentType"] = "Fijo",
            ["denue.latitude"] = "21.85796749",
            ["denue.longitude"] = "-102.28485913",
        }, Stringify(result.Data));
    }

    [Fact]
    public void InteriorNumberAndLegalName_AreIncluded()
    {
        var http = new FakeHttp(_ => Ok("""
            [{ "Id": "9321560", "Nombre": "HOTEL", "Razon_social": "OPERADORA HOTELERA AGS DTL S. DE R.L. DE C.V.", "Tipo_vialidad": "CALLE", "Calle": "ZARAGOZA", "Num_Exterior": "103", "Num_Interior": "B", "Colonia": "LOS ARELLANO", "CP": "20290", "Correo_e": "info@example.mx", "Sitio_internet": "www.example.mx" }]
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "9321560")), apiKey: ApiKey));

        Assert.Equal("OPERADORA HOTELERA AGS DTL S. DE R.L. DE C.V.", result.Data["denue.legalName"].ToString());
        Assert.Equal("CALLE ZARAGOZA 103 Int. B, LOS ARELLANO, 20290", result.Data["denue.address"].ToString());
        Assert.Equal("info@example.mx", result.Data["denue.email"].ToString());
        Assert.Equal("www.example.mx", result.Data["denue.website"].ToString());
    }

    [Theory]
    [InlineData("34185")]
    [InlineData("34 185")]
    [InlineData("ID 34185")]
    [InlineData("0034185")]
    public void NormalisesId(string id)
    {
        var http = new FakeHttp(_ => Ok(Loncheria));

        Run(Script, http, Values((Key, id)), apiKey: ApiKey);

        Assert.Equal($"{Url}34185/{ApiKey}", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("ABC123")]
    [InlineData("01001722514004531X")]
    [InlineData("1234567890123")]
    public void NoValidId_ReturnsNoResultWithoutCallingTheApi(string id)
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, id)), apiKey: ApiKey));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void NoToken_ReturnsNoResultWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        Assert.Empty(Run(Script, http, Values((Key, "34185")), apiKey: ""));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", "\"No autorizado. Utilice una clave válida.\"")]
    [InlineData("OK", "No autorizado. Utilice una clave válida.")]
    [InlineData("Unauthorized", "")]
    public void RejectedToken_FailsTheRecordWithoutRetrying(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "34185")), apiKey: ApiKey));

        Assert.Contains("rejected the token", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("OK", "[]")]
    [InlineData("OK", "No se encontraron resultados")]
    [InlineData("OK", """[{ "Id": "99999", "Nombre": "OTHER" }]""")]
    [InlineData("NotFound", "")]
    [InlineData("InternalServerError", "error")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "34185")), cache, ApiKey));
        Assert.Empty(Run(Script, http, Values((Key, "34185")), cache, ApiKey));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "34185")), apiKey: ApiKey));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameIdAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Loncheria));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "34185")), cache, ApiKey);
        var second = Run(Script, http, Values((Key, "34 185")), cache, ApiKey);

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
