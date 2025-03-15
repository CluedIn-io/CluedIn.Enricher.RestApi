using CluedIn.Core;
using CluedIn.Core.Data;
using CluedIn.Core.Data.Parts;
using CluedIn.Core.ExternalSearch;
using CluedIn.Crawling.Helpers;
using CluedIn.Core.Connectors;
using CluedIn.Core.Data.Relational;
using CluedIn.Core.Providers;
using CluedIn.Core.Data.Vocabularies;
using EntityType = CluedIn.Core.Data.EntityType;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
//using CluedIn.ExternalSearch.Providers.GenericRest.Vocabularies;
using CluedIn.ExternalSearch.Providers.GenericRest.Models;
using CluedIn.ExternalSearch.Providers.GenericRest.Models.Companies;
using CluedIn.ExternalSearch.Providers.GenericRest.Models.Locations;
using CluedIn.ExternalSearch.Providers.GenericRest.Models.PlaceId;

namespace CluedIn.ExternalSearch.Providers.GenericRest
{
    /// <summary>The googlemaps graph external search provider.</summary>
    /// <seealso cref="ExternalSearchProviderBase" />
    public class GenericRestExternalSearchProvider : ExternalSearchProviderBase, IExtendedEnricherMetadata, IConfigurableExternalSearchProvider, IExternalSearchProviderWithVerifyConnection
    {
        /**********************************************************************************************************
         * FIELDS
         **********************************************************************************************************/

        private static readonly EntityType[] DefaultAcceptedEntityTypes = { EntityType.Organization };

        /**********************************************************************************************************
         * CONSTRUCTORS
         **********************************************************************************************************/

        public GenericRestExternalSearchProvider()
            : base(Constants.ProviderId, DefaultAcceptedEntityTypes)
        {
            var nameBasedTokenProvider = new NameBasedTokenProvider("GoogleMaps");

            if (nameBasedTokenProvider.ApiToken != null)
                TokenProvider = new RoundRobinTokenProvider(nameBasedTokenProvider.ApiToken.Split(',', ';'));
        }

        /**********************************************************************************************************
         * METHODS
         **********************************************************************************************************/

        public IEnumerable<EntityType> Accepts(IDictionary<string, object> config, IProvider provider) => Accepts(config);

        private IEnumerable<EntityType> Accepts(IDictionary<string, object> config)
            => Accepts(new GenericRestExternalSearchJobData(config));

        private IEnumerable<EntityType> Accepts(GenericRestExternalSearchJobData config)
        {
            if (!string.IsNullOrWhiteSpace(config.AcceptedEntityType))
            {
                // If configured, only accept the configured entity types
                return new EntityType[] { config.AcceptedEntityType };
            }

            // Fallback to default accepted entity types
            return DefaultAcceptedEntityTypes;
        }

        private bool Accepts(GenericRestExternalSearchJobData config, EntityType entityTypeToEvaluate)
        {
            var configurableAcceptedEntityTypes = Accepts(config).ToArray();

            return configurableAcceptedEntityTypes.Any(entityTypeToEvaluate.Is);
        }

        public IEnumerable<IExternalSearchQuery> BuildQueries(ExecutionContext context, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
            => InternalBuildQueries(context, request, new GenericRestExternalSearchJobData(config));

        private IEnumerable<IExternalSearchQuery> InternalBuildQueries(ExecutionContext context, IExternalSearchRequest request, GenericRestExternalSearchJobData config)
        {
            if (!Accepts(config, request.EntityMetaData.EntityType))
                yield break;

            if (!string.IsNullOrWhiteSpace(config?.Header))
            {
                if (request.EntityMetaData.Properties.GetValue(config.Header)?.ToLowerInvariant() != "true")
                {
                    context.Log.LogTrace($"Skipped enrichment for record {request.EntityMetaData.OriginEntityCode} because VocabularyKey {config.Header} value was not true. Actual value: {request.EntityMetaData.Properties.GetValue(config.Header)}");
                    yield break;
                }
            }

            //var existingResults = request.GetQueryResults<CompanyDetailsResponse>(this).ToList();

            //bool NameFilter(string value)
            //{
            //    return existingResults.Any(r => string.Equals(r.Data.Result.Name, value, StringComparison.InvariantCultureIgnoreCase));
            //}

            //bool AddressFilter(string value)
            //{
            //    return existingResults.Any(r => string.Equals(r.Data.Result.FormattedAddress, value, StringComparison.InvariantCultureIgnoreCase));
            //}

            var entityType = request.EntityMetaData.EntityType;

            var configMap = config?.ToDictionary();
            var organizationName = GetValue(request, configMap, Constants.KeyName.Endpoint);
            var organizationAddress = GetValue(request, configMap, Constants.KeyName.Headers);
            var organizationZip = GetValue(request, configMap, Constants.KeyName.VocabularyAndProperties);
            
            //if (organizationName != null && organizationName.Count > 0
            //    && organizationAddress != null && organizationAddress.Count > 0
            //    && organizationZip != null && organizationZip.Count > 0
            //    && organizationState != null && organizationState.Count > 0
            //       && organizationCity != null && organizationCity.Count > 0
            //    && organizationCountry != null && organizationCountry.Count > 0)
            //{
            //    foreach (var nameValue in organizationName.Where(v => !NameFilter(v)))
            //    {
            //        foreach (var addressValue in organizationAddress.Where(v => !AddressFilter(v)))
            //        {
            //            foreach (var cityValue in organizationCity.Where(v => !AddressFilter(v)))
            //            {
            //                foreach (var zipValue in organizationZip.Where(v => !AddressFilter(v)))
            //                {
            //                    foreach (var stateValue in organizationState.Where(v => !AddressFilter(v)))
            //                    {
            //                        foreach (var countryValue in organizationCountry.Where(v => !AddressFilter(v)))
            //                        {
            //                            var companyDict = new Dictionary<string, string>
            //                            {
            //                                {"companyName", nameValue },
            //                                {"companyAddress", $"{addressValue}, {cityValue}, {zipValue}, {stateValue}, {countryValue}" }
            //                            };

            //                            yield return new ExternalSearchQuery(this, entityType, companyDict);
            //                        }
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}
            //else if (organizationName != null && organizationName.Count > 0 && organizationAddress != null && organizationAddress.Count > 0)
            //{
            //    foreach (var nameValue in organizationName.Where(v => !NameFilter(v)))
            //    {
            //        foreach (var addressValue in organizationAddress.Where(v => !AddressFilter(v)))
            //        {
            //            var companyDict = new Dictionary<string, string>
            //            {
            //                {"companyName", nameValue },
            //                {"companyAddress", addressValue }
            //            };
            //            yield return new ExternalSearchQuery(this, entityType, companyDict);
            //        }
            //    }
            //}

            //if (organizationName != null && organizationName.Count > 0)
            //{
            //    foreach (var value in organizationName.Where(v => !NameFilter(v)))
            //    {
            //        var nameDict = new Dictionary<string, string>
            //        {
            //            {"companyName", value },
            //        };
            //        yield return new ExternalSearchQuery(this, entityType, nameDict);
            //    }
            //}

            //if (organizationAddress != null && organizationAddress.Count > 0)
            //{
            //    foreach (var value in organizationAddress.Where(v => !AddressFilter(v)))
            //    {
            //        var addressDict = new Dictionary<string, string>
            //        {
            //            {"companyAddress", value }
            //        };
            //        yield return new ExternalSearchQuery(this, entityType, addressDict);
            //    }
            //}

            //if (locationAddress != null && locationAddress.Count > 0)
            //{
            //    foreach (var locationNameValue in locationAddress.Where(v => !AddressFilter(v)))
            //    {
            //        var locationDict = new Dictionary<string, string>
            //                {
            //                    {"locationName", locationNameValue },
            //                    {"coordinates", $"{latitude.FirstOrDefault() ?? string.Empty},{longitude.FirstOrDefault() ?? string.Empty}" }
            //                };

            //        yield return new ExternalSearchQuery(this, entityType, locationDict);
            //    }
            //}


            //if (personAddress != null && personAddress.Count > 0 && personAddressCity != null && personAddressCity.Count > 0)
            //{
            //    foreach (var locationNameValue in personAddress.Where(v => !AddressFilter(v)))
            //    {
            //        foreach (var locationCityValue in personAddressCity.Where(v => !AddressFilter(v)))
            //        {

            //            var locationDict = new Dictionary<string, string>
            //            {
            //                {"locationName", $"{locationNameValue}, {locationCityValue}" },
            //                {"coordinates", $"{latitude.FirstOrDefault() ?? string.Empty},{longitude.FirstOrDefault() ?? string.Empty}" }
            //            };

            //            yield return new ExternalSearchQuery(this, entityType, locationDict);
            //        }
            //    }
            //}
            //else if (personAddress != null && personAddress.Count > 0)
            //{
            //    foreach (var locationNameValue in personAddress.Where(v => !AddressFilter(v)))
            //    {
            //        var locationDict = new Dictionary<string, string>
            //            {
            //                {"locationName", locationNameValue },
            //                {"coordinates", $"{latitude.FirstOrDefault() ?? string.Empty},{longitude.FirstOrDefault() ?? string.Empty}" }
            //            };

            //        yield return new ExternalSearchQuery(this, entityType, locationDict);
            //    }
            //}

            //if (userAddress != null && userAddress.Count > 0)
            //{
            //    foreach (var locationNameValue in userAddress.Where(v => !AddressFilter(v)))
            //    {
            //        var locationDict = new Dictionary<string, string>
            //        {
            //            {"locationName", locationNameValue },
            //            {"coordinates", $"{latitude.FirstOrDefault() ?? string.Empty},{longitude.FirstOrDefault() ?? string.Empty}" }
            //        };

            //        yield return new ExternalSearchQuery(this, entityType, locationDict);
            //    }
            //}
        }

        private static HashSet<string> GetValue(IExternalSearchRequest request, IDictionary<string, object> config, string keyName)
        {
            if (!config.TryGetValue(keyName, out var customVocabKey) || string.IsNullOrWhiteSpace(customVocabKey?.ToString())) return [];

            var value = request.QueryParameters.GetValue(customVocabKey.ToString(), []);
            return value;
        }

        public IEnumerable<IExternalSearchQueryResult> ExecuteSearch(ExecutionContext context, IExternalSearchQuery query, IDictionary<string, object> config, IProvider provider)
        {
            var jobData = new GenericRestExternalSearchJobData(config);

            bool isCompany = false;
            var apiToken = jobData.VocabularyAndProperties;

            var client = new RestClient("https://maps.googleapis.com/maps/api");
            var output = "json";
            var placeDetailsEndpoint = $"place/details/{output}?";
            var placeIdEndpoint = $"place/textsearch/{output}?";

            var placeIdRequest = new RestRequest(placeIdEndpoint, Method.GET);
            placeIdRequest.AddQueryParameter("key", apiToken);

            if (query.QueryParameters.ContainsKey("companyName") || query.QueryParameters.ContainsKey("companyAddress"))
            {
                var input = new
                {
                    name = query.QueryParameters.TryGetValue("companyName", out var name) ? name.FirstOrDefault() : string.Empty,
                    address = query.QueryParameters.TryGetValue("companyAddress", out var address) ? address.FirstOrDefault() : string.Empty
                };
                var encodedInput = input.name + " " + input.address;
                placeIdRequest.AddQueryParameter("query", encodedInput);

                isCompany = true;
            }
            else
            {
                if (query.QueryParameters.ContainsKey("locationName"))
                {
                    placeIdRequest.AddParameter("query", query.QueryParameters["locationName"].First());
                }

                if (query.QueryParameters.ContainsKey("coordinates"))
                {
                    var transformedCoordinates = string.Join("", query.QueryParameters["coordinates"]);
                    var splitCoordinates = transformedCoordinates.Split(',');
                    placeIdRequest.AddParameter("location", $"{splitCoordinates[0]} {splitCoordinates[1]}");
                }
            }

            IRestResponse<PlaceIdResponse> placeIdResponse = null;

            try
            {
                context.Log.LogTrace("Making Google Maps call. Request: ", JsonUtility.Serialize(placeIdRequest.Parameters));
                placeIdResponse = client.ExecuteAsync<PlaceIdResponse>(placeIdRequest).Result;
            }
            catch (Exception exception)
            {
                context.Log.LogError("Could not fetch PlaceIdResponse from Google Maps", exception);
            }

            if (placeIdResponse == null)
            {
                yield break;
            }

            if (placeIdResponse.Data.Status.Equals("REQUEST_DENIED"))
            {
                context.Log.LogError("REQUEST DENIED by Google Maps");
                yield break;
            }

            if (placeIdResponse.StatusCode == HttpStatusCode.OK)
            {
                if (placeIdResponse.Data != null && isCompany == false)
                {
                    var request = new RestRequest(placeDetailsEndpoint, Method.GET);
                    foreach (var placeId in placeIdResponse.Data.Results)
                    {
                        request.AddParameter("placeid", placeId.PlaceId);
                        request.AddParameter("key", apiToken);
                    }

                    var response = client.ExecuteAsync<LocationDetailsResponse>(request).Result;
                    if (response.Data.Status.Equals("REQUEST_DENIED"))
                    {
                        yield break;
                    }

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (response.Data != null)
                            yield return new ExternalSearchQueryResult<LocationDetailsResponse>(query, response.Data);
                    }
                    else if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
                        yield break;
                    else if (response.ErrorException != null)
                        throw new AggregateException(response.ErrorException.Message, response.ErrorException);
                    else
                        throw new ApplicationException("Could not execute external search query - StatusCode:" + response.StatusCode + "; Content: " + response.Content);
                }
                else
                {
                    var request = new RestRequest(placeDetailsEndpoint, Method.GET);
                    foreach (var placeId in placeIdResponse.Data.Results)
                    {
                        request.AddParameter("placeid", placeId.PlaceId);
                        request.AddParameter("key", apiToken);
                    }

                    IRestResponse<CompanyDetailsResponse> response = null;

                    try
                    {
                        context.Log.LogTrace("Making Google Maps call. Request: ", JsonUtility.Serialize(request.Parameters));
                        response = client.ExecuteAsync<CompanyDetailsResponse>(request).Result;
                    }
                    catch (Exception exception)
                    {
                        context.Log.LogError("Could not fetch CompanyDetailsResponse from Google Maps", exception);
                    }

                    if (response == null)
                    {
                        yield break;
                    }

                    if (response.Data.Status.Equals("REQUEST_DENIED"))
                    {
                        context.Log.LogError("REQUEST DENIED by Google Maps");
                        yield break;
                    }

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (response.Data != null)
                            yield return new ExternalSearchQueryResult<CompanyDetailsResponse>(query, response.Data);
                    }
                    else if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
                        yield break;
                    else if (response.ErrorException != null)
                        throw new AggregateException(response.ErrorException.Message, response.ErrorException);
                    else
                        throw new ApplicationException("Could not execute external search query - StatusCode:" + response.StatusCode + "; Content: " + response.Content);
                }
            }
        }

        public IEnumerable<Clue> BuildClues(ExecutionContext context, IExternalSearchQuery query, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            if (result is IExternalSearchQueryResult<LocationDetailsResponse> locationDetailsResult)
            {
                var clue = new Clue(request.EntityMetaData.OriginEntityCode, context.Organization);
                clue.Data.EntityData.Codes.Add(request.EntityMetaData.Codes.First());

                PopulateLocationMetadata(clue.Data.EntityData, locationDetailsResult, request);

                // TODO: If necessary, you can create multiple clues and return them.

                return new[] { clue };
            }
            else if (result is IExternalSearchQueryResult<CompanyDetailsResponse> companyResult)
            {

                var clue = new Clue(request.EntityMetaData.OriginEntityCode, context.Organization);

                PopulateCompanyMetadata(clue.Data.EntityData, companyResult, request);

                // TODO: If necessary, you can create multiple clues and return them.

                return new[] { clue };
            }

            return null;
        }

        public IEntityMetadata GetPrimaryEntityMetadata(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            if (result is IExternalSearchQueryResult<LocationDetailsResponse> locationResult)
            {
                if (locationResult.Data.Result != null)
                {
                    return CreateLocationMetadata(locationResult, request);
                }
            }
            else if (result is IExternalSearchQueryResult<CompanyDetailsResponse> companyResult)
            {
                if (companyResult.Data.Result != null)
                {
                    return CreateCompanyMetadata(companyResult, request);
                }
            }

            return null;
        }

        public override IPreviewImage GetPrimaryEntityPreviewImage(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request)
        {
            return null;
        }

        public IPreviewImage GetPrimaryEntityPreviewImage(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            return null;
        }

        public ConnectionVerificationResult VerifyConnection(ExecutionContext context, IReadOnlyDictionary<string, object> config)
        {
            IDictionary<string, object> configDict = config.ToDictionary(entry => entry.Key, entry => entry.Value);
            var jobData = new GenericRestExternalSearchJobData(configDict);
            var apiToken = jobData.VocabularyAndProperties;
            var client = new RestClient("https://maps.googleapis.com/maps/api");
            var output = "json";
            var placeDetailsEndpoint = $"place/details/{output}?";
            var placeIdEndpoint = $"place/textsearch/{output}?";

            var placeIdRequest = new RestRequest(placeIdEndpoint, Method.GET);
            placeIdRequest.AddQueryParameter("key", apiToken);
            placeIdRequest.AddQueryParameter("query", "Google 1600 Amphitheatre Parkway, Mountain View, CA 94043.");

            IRestResponse<PlaceIdResponse> placeIdResponse = null;
            try
            {
                placeIdResponse = client.ExecuteAsync<PlaceIdResponse>(placeIdRequest).Result;
            }
            catch (Exception exception)
            {
                return new ConnectionVerificationResult(false, $"Could not fetch company details from Google Maps. {exception.Message}");
            }

            if (!placeIdResponse.IsSuccessful)
            {
                return ConstructVerifyConnectionResponse(placeIdResponse);
            }

            if (placeIdResponse.StatusCode != HttpStatusCode.OK)
                return new ConnectionVerificationResult(true, string.Empty);

            var request = new RestRequest(placeDetailsEndpoint, Method.GET);
            foreach (var placeId in placeIdResponse.Data.Results)
            {
                request.AddParameter("placeid", placeId.PlaceId);
                request.AddParameter("key", apiToken);
            }

            IRestResponse<CompanyDetailsResponse> response;

            try
            {
                response = client.ExecuteAsync<CompanyDetailsResponse>(request).Result;
            }
            catch (Exception exception)
            {
                return new ConnectionVerificationResult(false, $"Could not fetch CompanyDetailsResponse from Google Maps. {exception}");
            }

            return response == null ? new ConnectionVerificationResult(true, string.Empty) : ConstructVerifyConnectionResponse(placeIdResponse);
        }

        private ConnectionVerificationResult ConstructVerifyConnectionResponse<T>(IRestResponse<T> response)
        {
            var errorMessageBase = $"{Constants.ProviderName} returned \"{(int)response.StatusCode} {response.StatusDescription}\".";

            if (response.ErrorException != null)
            {
                return new ConnectionVerificationResult(
                    false,
                    $"{errorMessageBase} {(!string.IsNullOrWhiteSpace(response.ErrorException.Message) ? response.ErrorException.Message : "This could be due to breaking changes in the external system")}."
                );
            }

            dynamic responseData = response.Data;
            if (responseData != null && responseData.Status != null &&
                responseData.Status.Equals("REQUEST_DENIED") || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ConnectionVerificationResult(false, $"{errorMessageBase} This could be due to an invalid API key.");
            }

            var regex = new Regex(@"\<(html|head|body|div|span|img|p\>|a href)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);
            var isHtml = regex.IsMatch(response.Content);

            var errorMessage = response.IsSuccessful
                ? string.Empty
                : string.IsNullOrWhiteSpace(response.Content) || isHtml
                    ? $"{errorMessageBase} This could be due to breaking changes in the external system."
                    : $"{errorMessageBase} {response.Content}.";

            return new ConnectionVerificationResult(response.IsSuccessful, errorMessage);
        }

        private IEntityMetadata CreateLocationMetadata(IExternalSearchQueryResult<LocationDetailsResponse> resultItem, IExternalSearchRequest request)
        {
            var metadata = new EntityMetadataPart();

            PopulateLocationMetadata(metadata, resultItem, request);

            return metadata;
        }

        private IEntityMetadata CreateCompanyMetadata(IExternalSearchQueryResult<CompanyDetailsResponse> resultItem, IExternalSearchRequest request)
        {
            var metadata = new EntityMetadataPart();

            PopulateCompanyMetadata(metadata, resultItem, request);

            return metadata;
        }

        private void PopulateLocationMetadata(IEntityMetadata metadata, IExternalSearchQueryResult<LocationDetailsResponse> resultItem, IExternalSearchRequest request)
        {
            metadata.EntityType = request.EntityMetaData.EntityType;
            metadata.Name = request.EntityMetaData.Name;
            metadata.OriginEntityCode = request.EntityMetaData.OriginEntityCode;

            ////metadata.Properties[GoogleMapsVocabulary.Location.ComponentsAddress] = JsonUtility.Serialize(resultItem.Data.Result.AddressComponents);
            //foreach (var component in resultItem.Data.Result.AddressComponents)
            //{
            //    switch (component.Types.First())
            //    {
            //        case "route":
            //            metadata.Properties[GoogleMapsVocabulary.Location.NameStreet] = component.ShortName;
            //            break;
            //        case "colloquial_area":
            //            metadata.Properties[GoogleMapsVocabulary.Location.NameCity] = component.ShortName;
            //            break;
            //        case "locality":
            //            metadata.Properties[GoogleMapsVocabulary.Location.NameCity] = component.ShortName;
            //            break;
            //        case "administrative_area_level_1":
            //            metadata.Properties[GoogleMapsVocabulary.Location.AdministrativeArea] = component.ShortName;
            //            break;
            //        case "country":
            //            metadata.Properties[GoogleMapsVocabulary.Location.CodeCountry] = component.ShortName;
            //            break;
            //        case "postal_code":
            //            metadata.Properties[GoogleMapsVocabulary.Location.CodePostal] = component.ShortName;
            //            break;
            //    }

            //}

            //metadata.Properties[GoogleMapsVocabulary.Location.Name] = resultItem.Data.Result.Name.PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Location.Geometry] = JsonUtility.Serialize(resultItem.Data.Result.Geometry);
            //metadata.Properties[GoogleMapsVocabulary.Location.Latitude] = resultItem.Data.Result.Geometry.Location.Lat;
            //metadata.Properties[GoogleMapsVocabulary.Location.Longitude] = resultItem.Data.Result.Geometry.Location.Lng;
            //metadata.Properties[GoogleMapsVocabulary.Location.FormattedAddress] = resultItem.Data.Result.FormattedAddress.PrintIfAvailable();
        }

        private void PopulateCompanyMetadata(IEntityMetadata metadata, IExternalSearchQueryResult<CompanyDetailsResponse> resultItem, IExternalSearchRequest request)
        {
            metadata.EntityType = request.EntityMetaData.EntityType;
            metadata.Name = request.EntityMetaData.Name;
            metadata.OriginEntityCode = request.EntityMetaData.OriginEntityCode;

            ////metadata.Properties[GoogleMapsVocabulary.Organization.AddressComponents] = JsonUtility.Serialize(resultItem.Data.Result.AddressComponents);
            //foreach (var component in resultItem.Data.Result.AddressComponents)
            //{
            //    switch (component.Types.First())
            //    {
            //        case "street_number":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.StreetNumber] = component.ShortName;
            //            break;
            //        case "route":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.StreetName] = component.ShortName;
            //            break;
            //        case "locality":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.CityName] = component.ShortName;
            //            break;
            //        case "country":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.CountryCode] = component.ShortName;
            //            break;
            //        case "postal_code":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.PostalCode] = component.ShortName;
            //            break;
            //        case "subpremise":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.SubPremise] = component.ShortName;
            //            break;
            //        case "administrative_area_level_1":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.AdministrativeAreaLevel1] = component.ShortName;
            //            break;
            //        case "administrative_area_level_2":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.AdministrativeAreaLevel2] = component.ShortName;
            //            break;
            //        case "neighborhood":
            //            metadata.Properties[GoogleMapsVocabulary.Organization.Neighborhood] = component.ShortName;
            //            break;
            //    }

            //}

            //metadata.Properties[GoogleMapsVocabulary.Organization.AdrAddress] = resultItem.Data.Result.AdrAddress.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.FormattedAddress] = resultItem.Data.Result.FormattedAddress.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.FormattedPhoneNumber] = resultItem.Data.Result.FormattedPhoneNumber.PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Organization.Geometry] = resultItem.Data.Result.Geometry.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Longitude] = resultItem.Data.Result.Geometry.Location.Lng.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Latitude] = resultItem.Data.Result.Geometry.Location.Lat.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Icon] = resultItem.Data.Result.Icon.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Id] = resultItem.Data.Result.PlaceId.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.InternationalPhoneNumber] = resultItem.Data.Result.InternationalPhoneNumber.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Name] = resultItem.Data.Result.Name.PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Organization.OpeningHours] = resultItem.Data.Result.OpeningHours.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.PlaceId] = resultItem.Data.Result.PlaceId.PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Organization.PlusCode] = JsonUtility.Serialize(resultItem.Data.Result.PlusCode);
            //metadata.Properties[GoogleMapsVocabulary.Organization.Rating] = resultItem.Data.Result.Rating.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Reference] = resultItem.Data.Result.Reference.PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Organization.Reviews] = JsonUtility.Serialize(resultItem.Data.Result.Reviews);
            ////metadata.Properties[GoogleMapsVocabulary.Organization.Scope] = resultItem.Data.Result..PrintIfAvailable();
            ////metadata.Properties[GoogleMapsVocabulary.Organization.Types] = resultItem.Data.Result.Types.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Url] = resultItem.Data.Result.Url.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.UserRatingsTotal] = resultItem.Data.Result.UserRatingsTotal.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.UtcOffset] = resultItem.Data.Result.UtcOffset.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Vicinity] = resultItem.Data.Result.Vicinity.PrintIfAvailable();
            //metadata.Properties[GoogleMapsVocabulary.Organization.Website] = resultItem.Data.Result.Website.PrintIfAvailable();

            //metadata.Properties[GoogleMapsVocabulary.Organization.BusinessStatus] = resultItem.Data.Result.BusinessStatus.PrintIfAvailable();
        }

        // Since this is a configurable external search provider, theses methods should never be called
        public override bool Accepts(EntityType entityType) => throw new NotSupportedException();
        public override IEnumerable<IExternalSearchQuery> BuildQueries(ExecutionContext context, IExternalSearchRequest request) => throw new NotSupportedException();
        public override IEnumerable<IExternalSearchQueryResult> ExecuteSearch(ExecutionContext context, IExternalSearchQuery query) => throw new NotSupportedException();
        public override IEnumerable<Clue> BuildClues(ExecutionContext context, IExternalSearchQuery query, IExternalSearchQueryResult result, IExternalSearchRequest request) => throw new NotSupportedException();
        public override IEntityMetadata GetPrimaryEntityMetadata(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request) => throw new NotSupportedException();

        /**********************************************************************************************************
         * PROPERTIES
         **********************************************************************************************************/

        public string Icon { get; } = Constants.Icon;
        public string Domain { get; } = Constants.Domain;
        public string About { get; } = Constants.About;

        public AuthMethods AuthMethods { get; } = Constants.AuthMethods;
        public IEnumerable<Control> Properties { get; } = Constants.Properties;
        public Guide Guide { get; } = Constants.Guide;
        public IntegrationType Type { get; } = Constants.IntegrationType;
    }
}