using System.Collections.Generic;
using Jint.Runtime;
using Xunit;
using static CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples.SampleScriptRunner;

namespace CluedIn.ExternalSearch.Providers.RestApi.Integration.Tests.Samples;

/// <summary>
/// Runs the YTJ sample Process Script (docs/samples/ytj-finland) the same way the V2 REST API
/// enricher does, against a fake <c>http</c> object instead of the real API.
/// </summary>
public class YtjProcessScriptTests
{
    private const string Key = "organization.businessId";
    private const string Url = "https://avoindata.prh.fi/opendata-ytj-api/v3/companies?businessId=";

    // Embedded from docs/samples by the test project, so the tests always run the published sample
    private static readonly string Script = ReadEmbeddedScript("Samples.ytj-process-script.js");

    // Shortened from a real response for 0112038-9
    private const string Nokia = """
        {
          "totalResults": 1,
          "companies": [
            {
              "businessId": {
                "value": "0112038-9",
                "registrationDate": "1978-03-15",
                "source": "3"
              },
              "euId": {
                "value": "FIFPRO.0112038-9",
                "source": "1"
              },
              "names": [
                {
                  "name": "Nokia Oyj",
                  "type": "1",
                  "registrationDate": "1997-09-01",
                  "version": 1,
                  "source": "1"
                },
                {
                  "name": "Oy Nokia Ab",
                  "type": "1",
                  "registrationDate": "1966-06-10",
                  "endDate": "1997-08-31",
                  "version": 2,
                  "source": "1"
                }
              ],
              "mainBusinessLine": {
                "type": "70100",
                "descriptions": [
                  {
                    "languageCode": "3",
                    "description": "Activities of head offices"
                  },
                  {
                    "languageCode": "1",
                    "description": "Pääkonttorien toiminta"
                  },
                  {
                    "languageCode": "2",
                    "description": "Verksamheter som utövas av huvudkontor"
                  }
                ],
                "typeCodeSet": "TOIMI4",
                "registrationDate": "2026-01-01",
                "source": "2"
              },
              "website": {
                "url": "www.nokia.com",
                "registrationDate": "2019-07-01",
                "source": "0"
              },
              "companyForms": [
                {
                  "type": "17",
                  "descriptions": [
                    {
                      "languageCode": "1",
                      "description": "Julkinen osakeyhtiö"
                    },
                    {
                      "languageCode": "3",
                      "description": "Public limited company"
                    },
                    {
                      "languageCode": "2",
                      "description": "Publikt aktiebolag"
                    }
                  ],
                  "registrationDate": "1997-09-01",
                  "version": 1,
                  "source": "1"
                }
              ],
              "companySituations": [],
              "addresses": [
                {
                  "type": 1,
                  "street": "Karakaari",
                  "postCode": "02610",
                  "postOffices": [
                    {
                      "city": "ESBO",
                      "languageCode": "2",
                      "municipalityCode": "049"
                    },
                    {
                      "city": "ESPOO",
                      "languageCode": "1",
                      "municipalityCode": "049"
                    }
                  ],
                  "buildingNumber": "7",
                  "entrance": "",
                  "apartmentNumber": "",
                  "co": ""
                },
                {
                  "type": 2,
                  "street": "",
                  "postCode": "00045",
                  "postOffices": [
                    {
                      "city": "NOKIA GROUP",
                      "languageCode": "1",
                      "municipalityCode": "091"
                    },
                    {
                      "city": "NOKIA GROUP",
                      "languageCode": "2",
                      "municipalityCode": "091"
                    }
                  ],
                  "postOfficeBox": "226",
                  "buildingNumber": "",
                  "entrance": "",
                  "apartmentNumber": "",
                  "co": ""
                }
              ],
              "tradeRegisterStatus": "1",
              "status": "2",
              "registrationDate": "1896-12-19",
              "lastModified": "2026-11-10T17:07:58"
            }
          ]
        }
        """;

    [Fact]
    public void RegisteredCompany_GetsCompanyAndReturnsYtjProperties()
    {
        var http = new FakeHttp(_ => Ok(Nokia));

        var results = Run(Script, http, Values((Key, "0112038-9")));

        var request = Assert.Single(http.Requests);
        Assert.Equal(Url + "0112038-9", request.Url);
        Assert.Equal("GET", request.Method);

        var result = Assert.Single(results);
        Assert.Equal(100, result.Score);
        Assert.Equal(new Dictionary<string, string>
        {
            ["ytj.businessId"] = "0112038-9",
            ["ytj.euId"] = "FIFPRO.0112038-9",
            ["ytj.name"] = "Nokia Oyj",
            ["ytj.companyForm"] = "Public limited company",
            ["ytj.companyFormCode"] = "17",
            ["ytj.mainBusinessLineCode"] = "70100",
            ["ytj.mainBusinessLine"] = "Activities of head offices",
            ["ytj.website"] = "www.nokia.com",
            ["ytj.visitingAddress"] = "Karakaari 7, 02610 ESPOO",
            ["ytj.postalAddress"] = "PL 226, 00045 NOKIA GROUP",
            ["ytj.city"] = "ESPOO",
            ["ytj.postalCode"] = "02610",
            ["ytj.registrationDate"] = "1896-12-19",
            ["ytj.businessIdStatus"] = "Valid",
            ["ytj.tradeRegisterStatus"] = "Registered",
        }, Stringify(result.Data));
        // Kept as text, not turned into a DateTime by the enricher's JSON deserialisation
        Assert.IsType<string>(result.Data["ytj.registrationDate"]);
    }

    [Fact]
    public void CeasedCompanyInLiquidation_StoresStatusSituationAndEndDate()
    {
        var http = new FakeHttp(_ => Ok("""
            {
              "totalResults": 1,
              "companies": [{
                "businessId": { "value": "0112038-9" },
                "names": [{ "name": "Old Name Oy", "type": "1", "endDate": "2020-01-01" }, { "name": "Example Oy", "type": "1" }],
                "companySituations": [
                  { "type": "SELTILA", "registrationDate": "2024-01-01" },
                  { "type": "SANE", "registrationDate": "2019-01-01", "endDate": "2020-01-01" }
                ],
                "status": "2",
                "tradeRegisterStatus": "4",
                "registrationDate": "2001-05-01",
                "endDate": "2025-02-03"
              }]
            }
            """));

        var result = Assert.Single(Run(Script, http, Values((Key, "0112038-9"))));

        Assert.Equal(new Dictionary<string, string>
        {
            ["ytj.businessId"] = "0112038-9",
            ["ytj.name"] = "Example Oy",
            ["ytj.registrationDate"] = "2001-05-01",
            ["ytj.endDate"] = "2025-02-03",
            ["ytj.businessIdStatus"] = "Valid",
            ["ytj.tradeRegisterStatus"] = "Ceased",
            ["ytj.situation"] = "Liquidation",
        }, Stringify(result.Data));
    }

    [Theory]
    [InlineData("0112038-9")]
    [InlineData("01120389")]
    [InlineData("112038-9")]
    [InlineData("FI01120389")]
    [InlineData("fi 0112038-9")]
    [InlineData(" 0112038 - 9 ")]
    public void NormalisesBusinessId(string businessId)
    {
        var http = new FakeHttp(_ => Ok(Nokia));

        Run(Script, http, Values((Key, businessId)));

        Assert.Equal(Url + "0112038-9", Assert.Single(http.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0112038-8")] // wrong check digit
    [InlineData("1234")]
    [InlineData("123456789")]
    [InlineData("SE556703748501")]
    public void NoValidBusinessId_ReturnsNoResultWithoutCallingTheApi(string businessId)
    {
        var http = FakeHttp.MustNotBeCalled();

        var results = Run(Script, http, Values((Key, businessId)));

        Assert.Empty(results);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("OK", """{"totalResults":0,"companies":[]}""")]
    [InlineData("BadRequest", "")]
    [InlineData("InternalServerError", "")]
    [InlineData("ServiceUnavailable", "Service Unavailable")]
    [InlineData("OK", "not json")]
    public void ApiCannotAnswer_ReturnsNoResultAndIsNotCached(string httpStatus, string content)
    {
        var http = new FakeHttp(_ => Respond(httpStatus, content));
        var cache = new FakeCache();

        Assert.Empty(Run(Script, http, Values((Key, "0112038-9")), cache));
        Assert.Single(http.Requests);

        // Not cached: the next record with the same Business ID calls the API again
        Assert.Empty(Run(Script, http, Values((Key, "0112038-9")), cache));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public void ApiBusy_ThrowsErrorTheEnricherRetries()
    {
        var http = new FakeHttp(_ => Respond("TooManyRequests", ""));

        var ex = Assert.Throws<JavaScriptException>(() => Run(Script, http, Values((Key, "0112038-9"))));

        // RestApiExternalSearchProvider.ExecuteSearch retries when the exception text contains "TooManyRequests"
        Assert.Contains("TooManyRequests", ex.ToString());
    }

    [Fact]
    public void SameBusinessIdAgain_UsesCachedAnswer()
    {
        var http = new FakeHttp(_ => Ok(Nokia));
        var cache = new FakeCache();

        var first = Run(Script, http, Values((Key, "0112038-9")), cache);
        var second = Run(Script, http, Values((Key, "FI01120389")), cache);

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
