using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the BrasilAPI CNPJ sample Process Script (docs/samples/cnpj-brazil) the same way the V2
/// REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class CnpjProcessScriptTests
{
    private const string Key = "organization.cnpj";
    private const string Url = "https://brasilapi.com.br/api/cnpj/v1/";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.cnpj-process-script.js");

    // Shortened from a real response for 33000167000101
    private const string Petrobras = """
        {
          "uf": "RJ",
          "cep": "20031170",
          "cnpj": "33000167000101",
          "email": null,
          "porte": "DEMAIS",
          "bairro": "CENTRO",
          "numero": "65",
          "municipio": "RIO DE JANEIRO",
          "logradouro": "REPUBLICA DO CHILE",
          "cnae_fiscal": 600001,
          "complemento": "",
          "razao_social": "PETROLEO BRASILEIRO S A PETROBRAS",
          "nome_fantasia": "PETROBRAS - EDISE",
          "capital_social": 205431960000,
          "ddd_telefone_1": "2121660000",
          "natureza_juridica": "Sociedade de Economia Mista",
          "situacao_cadastral": 2,
          "cnae_fiscal_descricao": "Extração de petróleo e gás natural",
          "data_inicio_atividade": "1966-09-28",
          "data_situacao_cadastral": "2005-11-03",
          "codigo_natureza_juridica": 2038,
          "descricao_situacao_cadastral": "ATIVA",
          "descricao_tipo_de_logradouro": "AVENIDA",
          "descricao_identificador_matriz_filial": "MATRIZ"
        }
        """;

    [Fact]
    public void RegisteredCnpj_GetsCompanyAndReturnsCnpjProperties()
    {
        var http = new FakeHttp(_ => Ok(Petrobras));

        var results = Run(Script, http, Values((Key, "33000167000101")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "33000167000101", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["cnpj.cnpj"] = "33000167000101",
            ["cnpj.name"] = "PETROLEO BRASILEIRO S A PETROBRAS",
            ["cnpj.tradeName"] = "PETROBRAS - EDISE",
            ["cnpj.status"] = "ATIVA",
            ["cnpj.statusDate"] = "2005-11-03",
            ["cnpj.establishmentType"] = "MATRIZ",
            ["cnpj.legalNature"] = "Sociedade de Economia Mista",
            ["cnpj.legalNatureCode"] = "2038",
            ["cnpj.mainActivityCode"] = "0600001",
            ["cnpj.mainActivity"] = "Extração de petróleo e gás natural",
            ["cnpj.openingDate"] = "1966-09-28",
            ["cnpj.shareCapital"] = "205431960000",
            ["cnpj.companySize"] = "DEMAIS",
            ["cnpj.address"] = "AVENIDA REPUBLICA DO CHILE, 65, CENTRO",
            ["cnpj.city"] = "RIO DE JANEIRO",
            ["cnpj.state"] = "RJ",
            ["cnpj.postalCode"] = "20031170",
            ["cnpj.phone"] = "2121660000",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["cnpj.openingDate"]);
    }

    [Theory]
    [InlineData("33000167000101", "33000167000101")]
    [InlineData("33.000.167/0001-01", "33000167000101")]
    [InlineData(" 33 000 167 0001 01 ", "33000167000101")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    // Alphanumeric CNPJ (issued from July 2026); example from the Receita Federal's specification
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    public void NormalisesCnpj(string cnpj, string expected)
    {
        var http = new FakeHttp(_ => Ok(Petrobras));

        Run(Script, http, Values((Key, cnpj)));

        Assert.Equal(Url + expected, Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("33000167000102")] // wrong check digit
    [InlineData("11111111111111")] // all the same digit
    [InlineData("330001670001011")]
    public void NoValidCnpj_ReturnsNoResultWithoutCallingTheApi(string cnpj)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, cnpj)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void ShortActivityCode_IsPaddedToSevenDigits()
    {
        var http = new FakeHttp(_ => Ok(new { cnpj = "33000167000101", cnae_fiscal = 111301 }));

        var result = Assert.Single(Run(Script, http, Values((Key, "33000167000101"))));

        Assert.Equal("0111301", result.Data["cnpj.mainActivityCode"].ToString());
    }

    [Theory]
    [InlineData("NotFound", """{"message":"CNPJ 33.000.167/0001-01 não encontrado.","type":"not_found","name":"NotFoundError"}""")]
    [InlineData("BadRequest", """{"message":"CNPJ 33.000.167/0001-01 inválido.","type":"bad_request","name":"BadRequestError"}""")]
    [InlineData("InternalServerError", """{"message":"Todos os serviços de CNPJ retornaram erro.","type":"service_error","name":"CNPJ_PROMISE_ERROR"}""")]
    [InlineData("GatewayTimeout", "Gateway Timeout")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "33000167000101")), cache));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same CNPJ calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "33000167000101")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "33000167000101"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameCnpjAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Petrobras));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "33000167000101")), cache);
        var second = Run(Script, http, Values((Key, "33.000.167/0001-01")), cache);

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
