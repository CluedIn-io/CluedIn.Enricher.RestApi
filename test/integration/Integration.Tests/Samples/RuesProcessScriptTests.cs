using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the RUES (Colombia) sample Process Script (docs/samples/rues-colombia) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real datos.gov.co API.
/// </summary>
public class RuesProcessScriptTests
{
    private const string Key = "organization.nit";
    private const string Url = "https://www.datos.gov.co/resource/c82u-588k.json";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.rues-process-script.js");

    // A real row for NIT 860002964 (Banco de Bogotá)
    private const string BancoDeBogotaRow = """
        {"codigo_camara":"04","camara_comercio":"BOGOTA","matricula":"221830","inscripcion_proponente":"000000000000","razon_social":"BANCO DE BOGOTA","codigo_clase_identificacion":"02","clase_identificacion":"NIT","numero_identificacion":"860002964","nit":"860002964","digito_verificacion":"4","cod_ciiu_act_econ_pri":"6412","fecha_matricula":"19841012","fecha_renovacion":"20260319","ultimo_ano_renovado":"2026","fecha_vigencia":"20700630","codigo_tipo_sociedad":"02","tipo_sociedad":"SOCIEDAD COMERCIAL","codigo_organizacion_juridica":"04","organizacion_juridica":"SOCIEDAD ANONIMA","codigo_categoria_matricula":"01","categoria_matricula":"SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL","codigo_estado_matricula":"01","estado_matricula":"ACTIVA","clase_identificacion_rl":"CEDULA DE CIUDADANIA","num_identificacion_representante_legal":"94312021","representante_legal":"CESAR PRADO VILLEGAS","fecha_actualizacion":"2026/10/23 08:10:16.610000000"}
        """;

    private static string Rows(params string[] rows) => "[" + string.Join(",", rows) + "]";

    private static string Row(string name, string status, string category, string renewal = "20200101", string clase = "02", string numero = "860002964")
        => $$"""{"camara_comercio":"BOGOTA","matricula":"1","razon_social":"{{name}}","codigo_clase_identificacion":"{{clase}}","numero_identificacion":"{{numero}}","nit":"860002964","digito_verificacion":"4","fecha_renovacion":"{{renewal}}","codigo_categoria_matricula":"{{category}}","codigo_estado_matricula":"{{status}}","estado_matricula":"{{(status == "01" ? "ACTIVA" : "CANCELADA")}}"}""";

    [Fact]
    public void RegisteredCompany_GetsRegistrationAndReturnsRuesProperties()
    {
        var http = new FakeHttp(_ => Ok(Rows(BancoDeBogotaRow)));

        var results = Run(Script, http, Values((Key, "860.002.964-4")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "?nit=860002964", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Header("X-App-Token"));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["rues.nit"] = "860002964",
            ["rues.checkDigit"] = "4",
            ["rues.name"] = "BANCO DE BOGOTA",
            ["rues.status"] = "ACTIVA",
            ["rues.category"] = "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL",
            ["rues.legalOrganization"] = "SOCIEDAD ANONIMA",
            ["rues.companyType"] = "SOCIEDAD COMERCIAL",
            ["rues.mainActivityCode"] = "6412",
            ["rues.chamberOfCommerce"] = "BOGOTA",
            ["rues.chamberCode"] = "04",
            ["rues.registrationNumber"] = "221830",
            ["rues.registrationDate"] = "1984-10-12",
            ["rues.renewalDate"] = "2026-03-19",
            ["rues.lastRenewedYear"] = "2026",
            ["rues.expiryDate"] = "2070-06-30",
            ["rues.legalRepresentative"] = "CESAR PRADO VILLEGAS",
            ["rues.updatedDate"] = "2026-10-23",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["rues.registrationDate"]);
    }

    [Fact]
    public void AppToken_IsSentWhenTheApiKeyFieldHasOne()
    {
        var http = new FakeHttp(_ => Ok(Rows(BancoDeBogotaRow)));

        Run(Script, http, Values((Key, "860002964")), apiKey: "my-app-token");

        Assert.Equal("my-app-token", Assert.Single(http.Requests).Header("X-App-Token"));
    }

    [Fact]
    public void RejectedAppToken_ThrowsErrorThatIsNotRetried()
    {
        var http = new FakeHttp(_ => Respond("Forbidden", """{ "code": "permission_denied", "error": true, "message": "Invalid app_token specified" }"""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "860002964")), apiKey: "bogus"));

        Assert.Contains("app token", ex.ToString());
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("860002964")]
    [InlineData("860.002.964")]
    [InlineData("860.002.964-4")]
    [InlineData("860 002 964 - 4")]
    [InlineData("NIT 860002964-4")]
    [InlineData("nit. 860,002,964-4")]
    [InlineData("0860002964")]
    public void NormalisesNit(string nit)
    {
        var http = new FakeHttp(_ => Ok(Rows(BancoDeBogotaRow)));

        Run(Script, http, Values((Key, nit)));

        Assert.Equal(Url + "?nit=860002964", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("860002964-5")] // wrong check digit
    [InlineData("860002964-")]
    [InlineData("CO860002964")]
    [InlineData("12345678901")] // too long
    public void NoValidNit_ReturnsNoResultWithoutCallingTheApi(string nit)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, nit)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void SeveralRegistrations_ActivePrincipalMostRecentlyRenewedOneIsChosen()
    {
        var http = new FakeHttp(_ => Ok(Rows(
            Row("OLD NAME SAS", "03", "01", "20250101"),
            Row("BRANCH OF ACME SAS", "01", "02", "20260101"),
            Row("ACME SAS", "01", "01", "20240101"),
            Row("ACME SAS", "01", "01", "20260301"))));

        var result = Assert.Single(Run(Script, http, Values((Key, "860002964"))));

        Assert.Equal("ACME SAS", result.Data["rues.name"].ToString());
        Assert.Equal("ACTIVA", result.Data["rues.status"].ToString());
        Assert.Equal("2026-03-01", result.Data["rues.renewalDate"].ToString());
    }

    [Fact]
    public void OnlyCancelledRegistration_IsStoredAsCancelled()
    {
        var http = new FakeHttp(_ => Ok(Rows(Row("ACME SAS", "03", "01"))));

        var result = Assert.Single(Run(Script, http, Values((Key, "860002964"))));

        Assert.Equal("CANCELADA", result.Data["rues.status"].ToString());
    }

    [Fact]
    public void RegistrationsUnderDifferentNamesThatRankTheSame_ReturnNoResult()
    {
        var http = new FakeHttp(_ => Ok(Rows(Row("ACME SAS", "01", "01"), Row("OTHER COMPANY SAS", "01", "01"))));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "860002964")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "860002964")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void PeopleListedUnderTheNitWithTheirOwnIdentification_AreIgnored()
    {
        // The extract lists some natural persons under another organisation's NIT
        var http = new FakeHttp(_ => Ok(Rows(
            Row("PEREZ JUAN", "01", "00", clase: "01", numero: "1006484686"),
            Row("GOMEZ ANA", "01", "00", clase: "01", numero: "4166487"))));

        Assert.Empty(Run(Script, http, Values((Key, "860002964"))));
    }

    [Fact]
    public void NaturalPersonRegisteredByTheirOwnNumber_IsAccepted()
    {
        var http = new FakeHttp(_ => Ok(Rows(Row("PEREZ JUAN", "01", "00", clase: "01", numero: "860002964"))));

        var result = Assert.Single(Run(Script, http, Values((Key, "860002964"))));

        Assert.Equal("PEREZ JUAN", result.Data["rues.name"].ToString());
    }

    [Fact]
    public void OpenEndedDatesAndZeroYear_AreLeftOut()
    {
        var http = new FakeHttp(_ => Ok("""
            [{"razon_social":"ACME SAS","codigo_clase_identificacion":"02","nit":"860002964","digito_verificacion":"4","fecha_matricula":"19740819","ultimo_ano_renovado":"0","fecha_vigencia":"99991231","fecha_cancelacion":"20111229","codigo_estado_matricula":"03","estado_matricula":"CANCELADA"}]
            """));

        var data = Stringify(Assert.Single(Run(Script, http, Values((Key, "860002964")))).Data);

        Assert.Equal("1974-08-19", data["rues.registrationDate"]);
        Assert.Equal("2011-12-29", data["rues.cancellationDate"]);
        Assert.False(data.ContainsKey("rues.expiryDate"));
        Assert.False(data.ContainsKey("rues.lastRenewedYear"));
    }

    [Theory]
    [InlineData("OK", "[]")]
    [InlineData("BadRequest", """{ "error": true, "message": "Unrecognized arguments [nitx]" }""")]
    [InlineData("InternalServerError", "")]
    [InlineData("Forbidden", "<html>Forbidden</html>")] // no app token configured: not a key problem
    [InlineData("OK", "not json")]
    public void ApiCannotAnswerOrNoRegistration_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "860002964")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "860002964")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiThrottling_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "860002964"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameNitAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Rows(BancoDeBogotaRow)));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "860002964")), cache);
        var second = Run(Script, http, Values((Key, "860.002.964-4")), cache);

        Assert.Single(http.Requests);
        Assert.Equal(Stringify(Assert.Single(first).Data), Stringify(Assert.Single(second).Data));
    }

    [Fact]
    public void TestConnection_PassesWithPlaceholderValuesWithoutCallingTheApi()
    {
        var http = FakeHttp.MustNotBeCalled();

        var response = TestConnection(Script, http, [Key], apiKey: "my-app-token");

        Assert.Equal("[]", response);
        Assert.Empty(http.Requests);
    }
}
