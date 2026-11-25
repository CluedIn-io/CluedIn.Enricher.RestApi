using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the APR Companies Open Data sample Process Script (docs/samples/apr-serbia) the same way
/// the V2 REST API enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class AprProcessScriptTests
{
    private const string Key = "organization.registrationNumber";
    private const string Url = "https://openapi.apr.gov.rs/api/opendata/companies";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.apr-process-script.js");

    // The real layout of the open data (pretty-printed, one flat object per company), with
    // three real companies from the 2026-10-31 data and one made-up name containing a brace
    private const string Register = """
        {
            "DatumPreseka": "2026-10-31",
            "Podaci": {
                "22005464": {
                    "PoslovnoIme": "PRIVREDNO DRUŠTVO PROPOINT DOO POŽAREVAC",
                    "SifraOpstine": "70947",
                    "NazivOpstine": "ПОЖАРЕВАЦ",
                    "NazivStatus": "Активан",
                    "DatumOsnivanja": "2024-04-15",
                    "NazivPravneForme": "Друштво са ограниченом одговорношћу",
                    "SifraDelatnosti": "4651"
                },
                "07042701": {
                    "PoslovnoIme": "AKCIONARSKO DRUŠTVO INDUSTRIJA MLEKA I MLEČNIH PROIZVODA IMLEK, PADINSKA SKELA",
                    "SifraOpstine": "70203",
                    "NazivOpstine": "ПАЛИЛУЛА (БЕОГРАД)",
                    "NazivStatus": "Активан",
                    "DatumOsnivanja": "1991-03-12",
                    "NazivPravneForme": "Акционарско друштво",
                    "SifraDelatnosti": "1051"
                },
                "22080806": {
                    "PoslovnoIme": "BRAĆA KOSTIĆ 2025 DOO RISTOVAC - U PRINUDNOJ LIKVIDACIJI",
                    "SifraOpstine": "70432",
                    "NazivOpstine": "ВРАЊЕ",
                    "NazivStatus": "У принудној ликвидацији",
                    "DatumOsnivanja": "2025-02-05",
                    "NazivPravneForme": "Друштво са ограниченом одговорношћу",
                    "SifraDelatnosti": "4399"
                },
                "12345678": {
                    "PoslovnoIme": "BRACE {TEST} DOO \"07042701\"",
                    "SifraOpstine": "70203",
                    "NazivOpstine": "ПАЛИЛУЛА (БЕОГРАД)",
                    "NazivStatus": "Активан",
                    "DatumOsnivanja": "2020-01-01",
                    "NazivPravneForme": "Друштво са ограниченом одговорношћу",
                    "SifraDelatnosti": "6201"
                }
            }
        }
        """;

    [Fact]
    public void RegisteredCompany_DownloadsRegisterAndReturnsAprProperties()
    {
        var http = new FakeHttp(_ => Ok(Register));

        var results = Run(Script, http, Values((Key, "07042701")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url, request.Url);
        Assert.Equal("GET", request.Method);
        Assert.False(string.IsNullOrWhiteSpace(request.Header("User-Agent")));

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["apr.registrationNumber"] = "07042701",
            ["apr.name"] = "AKCIONARSKO DRUŠTVO INDUSTRIJA MLEKA I MLEČNIH PROIZVODA IMLEK, PADINSKA SKELA",
            ["apr.status"] = "Активан",
            ["apr.legalForm"] = "Акционарско друштво",
            ["apr.foundationDate"] = "1991-03-12",
            ["apr.municipality"] = "ПАЛИЛУЛА (БЕОГРАД)",
            ["apr.municipalityCode"] = "70203",
            ["apr.activityCode"] = "1051",
            ["apr.dataDate"] = "2026-10-31",
        }, Stringify(result.Data));
        Assert.IsType<string>(result.Data["apr.foundationDate"]);
    }

    [Theory]
    [InlineData("07042701")]
    [InlineData("7042701")]
    [InlineData("0704 2701")]
    [InlineData("MB 07042701")]
    [InlineData("07.04.27.01")]
    public void NormalisesRegistrationNumber(string input)
    {
        var http = new FakeHttp(_ => Ok(Register));

        var result = Assert.Single(Run(Script, http, Values((Key, input))));

        Assert.Equal("07042701", result.Data["apr.registrationNumber"].ToString());
    }

    [Fact]
    public void NameContainingBracesAndAnotherNumber_IsStillFound()
    {
        var http = new FakeHttp(_ => Ok(Register));

        var result = Assert.Single(Run(Script, http, Values((Key, "12345678"))));

        Assert.Equal("BRACE {TEST} DOO \"07042701\"", result.Data["apr.name"].ToString());
    }

    [Fact]
    public void CompanyInLiquidation_StoresItsStatus()
    {
        var http = new FakeHttp(_ => Ok(Register));

        var result = Assert.Single(Run(Script, http, Values((Key, "22080806"))));

        Assert.Equal("У принудној ликвидацији", result.Data["apr.status"].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("123456789")]   // a PIB, not a registration number
    [InlineData("00000000")]
    [InlineData("RS07042701")]
    public void NoValidRegistrationNumber_ReturnsNoResultWithoutCallingTheApi(string registrationNumber)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, registrationNumber)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void CompanyNotInOpenData_ReturnsNoResult()
    {
        var http = new FakeHttp(_ => Ok(Register));

        Assert.Empty(Run(Script, http, Values((Key, "99999999"))));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void ManyRecords_DownloadTheRegisterOnce()
    {
        var http = new FakeHttp(_ => Ok(Register));
        var cache = new FakeCache();

        Assert.Single(Run(Script, http, Values((Key, "07042701")), cache));
        Assert.Single(Run(Script, http, Values((Key, "22005464")), cache));
        Assert.Empty(Run(Script, http, Values((Key, "99999999")), cache));

        Assert.Single(http.Requests);
        Assert.Equal(1, cache.FactoryCalls);
    }

    [Fact]
    public void DownloadInProgressElsewhere_ThrowsErrorTheEnricherRetries()
    {
        // Helper.Cache.GetOrSetFactory returns null when its cluster-wide lock times out
        var http = FakeHttp.MustNotBeCalled();
        var cache = new FakeCache { LockTimesOut = true };

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "07042701")), cache));

        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Theory]
    [InlineData("InternalServerError", "")]
    [InlineData("NotFound", """{"title": "404 Not Found"}""")]
    [InlineData("OK", """{"title": "success"}""")]
    public void RegisterUnavailable_FailsTheRecordAndCachesNothing(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "07042701")), cache));

        Assert.Contains("could not provide", ex.Message);
        Assert.DoesNotContain("TooManyRequests", ex.ToString());
        Assert.Null(cache.Get("apr:companies"));
    }

    [Fact]
    public void RateLimited_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "07042701"))));

        Assert.Contains("TooManyRequests", ex.ToString());
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
