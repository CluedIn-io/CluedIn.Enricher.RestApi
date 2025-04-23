using CluedIn.Core;
using CluedIn.Core.Data;
using CluedIn.Core.Data.Parts;
using CluedIn.Core.ExternalSearch;
using CluedIn.Core.Connectors;
using CluedIn.Core.Data.Relational;
using CluedIn.Core.Providers;
using CluedIn.Core.Processing;
using CluedIn.ExternalSearch.Providers.RestApi.Models;
using CluedIn.Rules.Tokens;
using EntityType = CluedIn.Core.Data.EntityType;
using ExecutionContext = CluedIn.Core.ExecutionContext;

using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CluedIn.ExternalSearch.Providers.RestApi
{
    /// <summary>The rest api external search provider.</summary>
    /// <seealso cref="ExternalSearchProviderBase" />
    public class RestApiExternalSearchProvider : ExternalSearchProviderBase, IExtendedEnricherMetadata, IConfigurableExternalSearchProvider, IExternalSearchProviderWithVerifyConnection
    {
        /**********************************************************************************************************
         * FIELDS
         **********************************************************************************************************/

        private static readonly EntityType[] DefaultAcceptedEntityTypes = [EntityType.Organization];

        /**********************************************************************************************************
         * CONSTRUCTORS
         **********************************************************************************************************/

        public RestApiExternalSearchProvider()
            : base(Constants.ProviderId, DefaultAcceptedEntityTypes)
        {
            var nameBasedTokenProvider = new NameBasedTokenProvider("RestApi");

            if (nameBasedTokenProvider.ApiToken != null)
                TokenProvider = new RoundRobinTokenProvider(nameBasedTokenProvider.ApiToken.Split(',', ';'));
        }

        /**********************************************************************************************************
         * METHODS
         **********************************************************************************************************/

        public IEnumerable<EntityType> Accepts(IDictionary<string, object> config, IProvider provider) => Accepts(config);

        private IEnumerable<EntityType> Accepts(IDictionary<string, object> config)
            => Accepts(new RestApiExternalSearchJobData(config));

        private IEnumerable<EntityType> Accepts(RestApiExternalSearchJobData config)
        {
            if (!string.IsNullOrWhiteSpace(config.AcceptedEntityType))
            {
                // If configured, only accept the configured entity types
                return [config.AcceptedEntityType];
            }

            // Fallback to default accepted entity types
            return DefaultAcceptedEntityTypes;
        }

        private bool Accepts(RestApiExternalSearchJobData config, EntityType entityTypeToEvaluate)
        {
            var configurableAcceptedEntityTypes = Accepts(config).ToArray();

            return configurableAcceptedEntityTypes.Any(entityTypeToEvaluate.Is);
        }

        public IEnumerable<IExternalSearchQuery> BuildQueries(ExecutionContext context, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
            => InternalBuildQueries(context, request, new RestApiExternalSearchJobData(config));

        private IEnumerable<IExternalSearchQuery> InternalBuildQueries(ExecutionContext context, IExternalSearchRequest request, RestApiExternalSearchJobData config)
        {
            if (!Accepts(config, request.EntityMetaData.EntityType))
                yield break;

            var entityType = request.EntityMetaData.EntityType;

            var requestInfoConfig = GetRequestConfig(context, request, config);

            if (requestInfoConfig.TryGetValue("url", out var urlValue) && string.IsNullOrWhiteSpace(urlValue))
            {
                context.Log.LogTrace($"Skipped enrichment for record {request.EntityMetaData.OriginEntityCode} because the Url could not be retrieved");
                throw new InvalidOperationException("Unable to retrieve Url.");
            }

            yield return new ExternalSearchQuery(this, entityType, requestInfoConfig);
        }

        public IEnumerable<IExternalSearchQueryResult> ExecuteSearch(ExecutionContext context, IExternalSearchQuery query, IDictionary<string, object> config, IProvider provider)
        {
            var jobData = new RestApiExternalSearchJobData(config);
            var body = string.Empty;
            if (query.QueryParameters.ContainsKey("properties"))
            {
                body = query.QueryParameters.TryGetValue("properties", out var properties)
                    ? properties.FirstOrDefault()
                    : string.Empty;
            }

            var url = string.Empty;
            if (query.QueryParameters.ContainsKey("url"))
            {
                url = query.QueryParameters.TryGetValue("url", out var urlValue)
                    ? urlValue.FirstOrDefault()
                    : string.Empty;
            }

            if (string.IsNullOrEmpty(url))
            {
                context.Log.LogTrace("No parameter for '{Identifier}' in query, skipping execute search",
                    ExternalSearchQueryParameter.Identifier);
            }

            var request = new RequestDto
            {
                Method = jobData.Method,
                Url = url,
                Headers = GetHeaders(jobData.Headers, jobData.ApiKey),
                ApiKey = jobData.ApiKey,
                Body = new BodyDto
                {
                    Properties = JsonConvert.DeserializeObject<List<PropertyDto>>(body ?? string.Empty)
                }
            };

            if (!string.IsNullOrWhiteSpace(jobData.ProcessRequestScript))
            {
                using var engine = new Jint.Engine()
                    .SetValue("log", new Action<object>(o => context.Log.Log(LogLevel.Debug, o?.ToString())))
                    .SetValue("request", request)
                    .Execute(jobData.ProcessRequestScript);

                request = engine.GetValue("request").ToObject() as RequestDto;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Url)) {
                context.Log.LogTrace($"Skipped enrichment for record {Name} because Url is null or empty");
                yield break; //TODO Log if null
            }

            var client = new RestClient(request.Url);
            var restRequest = new RestRequest(GetHttpMethod(request.Method));

            foreach (var header in request.Headers.Where(header => !string.IsNullOrWhiteSpace(header.Key) && !string.IsNullOrWhiteSpace(header.Value)))
            {
                restRequest.AddHeader(header.Key, header.Value);
            }

            restRequest.AddJsonBody(request.Body);
            var restResponse = client.ExecuteAsync(restRequest).Result;

            context.Log.Log(LogLevel.Debug, $"{Name} - {restResponse.StatusCode} - {restResponse.Content}");

            var responseDto = new ResponseDto
            {
                HttpStatus = restResponse.StatusCode.ToString(),
                Content = restResponse.Content,
                ContentType = restResponse.ContentType,
                Headers = restResponse.Headers.Select(x => new HeaderDto { Key = x.Name, Value = x.Value?.ToString() }).ToList()
            };

            if (!string.IsNullOrWhiteSpace(jobData.ProcessResponseScript))
            {
                using var engine = new Jint.Engine()
                    .SetValue("log", new Action<object>(o => context.Log.Log(LogLevel.Debug, $"User Script log: {o}" )))
                    .SetValue("response", responseDto)
                    .Execute(jobData.ProcessResponseScript);

                var response = engine.GetValue("response").ToObject() as ResponseDto;
                responseDto = response ?? throw new ApplicationException("Response after Calling User Script is null");

                if (string.IsNullOrWhiteSpace(responseDto.Content))
                {
                    context.Log.LogWarning($"{Name} - Response Content after Calling User Script is null or empty");
                    yield break;
                }

                context.Log.Log(LogLevel.Debug, $"{Name} - Response after Calling User Script\n{JsonConvert.SerializeObject(response)}");
            }

            if (responseDto.HttpStatus == HttpStatusCode.TooManyRequests.ToString())
            {
                Thread.Sleep(2000); // while developing we observed that the error message says to retry in 2s
                throw new Exception($"Too many requests - Call returned HTTP {responseDto.HttpStatus} - {responseDto.Content}"); // hack the message must start with 'Too many requests' for the core to retry
            }

            if (responseDto.HttpStatus != HttpStatusCode.OK.ToString()) throw new ApplicationException($"Call returned HTTP {responseDto.HttpStatus} - {responseDto.Content}");

            var results = JsonConvert.DeserializeObject<ResultsDto[]>(responseDto.Content);

            yield return new ExternalSearchQueryResult<ResultsDto[]>(query, results);

        }

        public IEnumerable<Clue> BuildClues(ExecutionContext context, IExternalSearchQuery query, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            using (context.Log.BeginScope("{0} {1}: query {2}, request {3}, result {4}", GetType().Name, "BuildClues", query, request, result))
            {
                var resultItem = result.As<ResultsDto[]>();
                var code = new EntityCode(request.EntityMetaData.EntityType, "RestApi",
                    $"{query.QueryKey}{request.EntityMetaData.OriginEntityCode}"
                        .ToDeterministicGuid());
                var clue = new Clue(code, context.Organization);

                PopulateMetadata(clue.Data.EntityData, resultItem, request);

                var logoKey = clue.Data.EntityData.Properties?.Keys.FirstOrDefault(key => key.Contains(".logo"));
                if (!string.IsNullOrEmpty(logoKey) && clue.Data.EntityData.Properties.TryGetValue(logoKey, out var property))
                    this.DownloadPreviewImage(context, property, clue);

                context.Log.LogInformation(
                    "Clue produced, Id: '{Id}' OriginEntityCode: '{OriginEntityCode}' RawText: '{RawText}'", clue.Id,
                    clue.OriginEntityCode, clue.RawText);

                return [clue];
            }
        }

        public IEntityMetadata GetPrimaryEntityMetadata(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            using (context.Log.BeginScope("{0} {1}: request {2}, result {3}", GetType().Name, "GetPrimaryEntityMetadata",
                       request, result))
            {
                var metadata = CreateMetadata(result.As<ResultsDto[]>(), request);

                context.Log.LogInformation(
                    "Primary entity meta data created, Name: '{Name}' OriginEntityCode: '{OriginEntityCode}'",
                    metadata.Name, metadata.OriginEntityCode.Origin.Code);

                return metadata;
            }
        }


        public IPreviewImage GetPrimaryEntityPreviewImage(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request, IDictionary<string, object> config, IProvider provider)
        {
            var metadata = GetPrimaryEntityMetadata(context, result, request, config, provider);
            var logoKey = metadata.Properties?.Keys.FirstOrDefault(key => key.Contains(".logo"));
            if (!string.IsNullOrEmpty(logoKey) && metadata.Properties.TryGetValue(logoKey, out var value))
            {
                return DownloadPreviewImageBlob(context, value);
            };

            return null;
        }

        public ConnectionVerificationResult VerifyConnection(ExecutionContext context, IReadOnlyDictionary<string, object> config)
        {
            var data = new RestApiExternalSearchJobData(config.ToDictionary(x => x.Key, x => x.Value));

            if (string.IsNullOrWhiteSpace(data.Url))
            {
                return new ConnectionVerificationResult(false, "Url must not be blank");
            }

            if (string.IsNullOrWhiteSpace(data.Method))
            {
                return new ConnectionVerificationResult(false, "Method must not be blank");
            }

            if (data.Url.Contains("Vocabulary:"))
            {
                return new ConnectionVerificationResult(false, "Please replace {Vocabulary:xxx} in url with actual value to test connection");
            }

            try
            {
                var body = string.IsNullOrWhiteSpace(data.VocabularyAndProperties) ? null : GetPropertiesTestConnection(data.VocabularyAndProperties);

                var request = new RequestDto
                {
                    Method = data.Method,
                    Url = data.Url,
                    Headers = GetHeaders(data.Headers, data.ApiKey),
                    ApiKey = data.ApiKey,
                    Body = new BodyDto
                    {
                        Properties = body
                    }
                };

                if (!string.IsNullOrWhiteSpace(data.ProcessRequestScript))
                {
                    using var requestEngine = new Jint.Engine()
                        .SetValue("log", new Action<object>(o => context.Log.Log(LogLevel.Debug, o?.ToString())))
                        .SetValue("request", request)
                        .Execute(data.ProcessRequestScript);

                    request = requestEngine.GetValue("request").ToObject() as RequestDto;
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Url))
                {
                    return new ConnectionVerificationResult(false, $"Url - {request?.Url} is invalid");
                }

                var client = new RestClient(request.Url);
                var restRequest = new RestRequest(GetHttpMethod(request.Method));

                foreach (var header in request.Headers.Where(header => !string.IsNullOrWhiteSpace(header.Key) && !string.IsNullOrWhiteSpace(header.Value)))
                {
                    restRequest.AddHeader(header.Key, header.Value);
                }

                restRequest.AddJsonBody(request.Body);
                var restResponse = client.ExecuteAsync(restRequest).Result;

                var responseDto = new ResponseDto
                {
                    HttpStatus = restResponse.StatusCode.ToString(),
                    Content = restResponse.Content,
                    ContentType = restResponse.ContentType,
                    Headers = restResponse.Headers.Select(x => new HeaderDto { Key = x.Name, Value = x.Value?.ToString() }).ToList()
                };

                if (string.IsNullOrWhiteSpace(data.ProcessResponseScript)) return new ConnectionVerificationResult(true);

                using var responseEngine = new Jint.Engine()
                    .SetValue("log", new Action<object>(o => context.Log.Log(LogLevel.Debug, $"User Script log: {o}")))
                    .SetValue("response", responseDto)
                    .Execute(data.ProcessResponseScript);

                var response = responseEngine.GetValue("response").ToObject() as ResponseDto;
                responseDto = response ?? throw new ApplicationException("Response after Calling User Script is null");

                if (string.IsNullOrWhiteSpace(responseDto.Content))
                {
                    return new ConnectionVerificationResult(false, "Response Content after Calling User Script is null or empty");
                }

                if (responseDto.HttpStatus == HttpStatusCode.TooManyRequests.ToString()) return new ConnectionVerificationResult(false, $"Too many requests - Call returned HTTP {responseDto.HttpStatus} - {responseDto.Content}");

                return responseDto.HttpStatus != HttpStatusCode.OK.ToString() ? new ConnectionVerificationResult(false, $"Call returned HTTP {responseDto.HttpStatus} - {responseDto.Content}") : new ConnectionVerificationResult(true);
            }
            catch (Exception ex) 
            { 
                return new ConnectionVerificationResult(false, ex.Message);
            }
        }

        private IEntityMetadata CreateMetadata(IExternalSearchQueryResult<ResultsDto[]> resultItem, IExternalSearchRequest request)
        {
            var metadata = new EntityMetadataPart();

            PopulateMetadata(metadata, resultItem, request);

            return metadata;
        }

        private void PopulateMetadata(IEntityMetadata metadata, IExternalSearchQueryResult<ResultsDto[]> resultItem,
            IExternalSearchRequest request)
        {
            var code = new EntityCode(request.EntityMetaData.EntityType, "RestApi",
                $"{request.Queries.FirstOrDefault()?.QueryKey}{request.EntityMetaData.OriginEntityCode}"
                    .ToDeterministicGuid());
            metadata.EntityType = request.EntityMetaData.EntityType;
            metadata.Name = request.EntityMetaData.Name;
            metadata.OriginEntityCode = code;
            metadata.Codes.Add(request.EntityMetaData.OriginEntityCode);

            foreach (var enrichmentResult in resultItem.Data)
            {
                using var en = enrichmentResult.Data.GetEnumerator();
                while (en.MoveNext())
                {
                    var key = en.Current.Key;
                    key = SanitizeVocabularyKey(key); //TODO Maybe the key can be sanitized in the end of ExecuteSearch(), but the Dictionary results need to be updated
                    metadata.Properties[key] = en.Current.Value.ToString();
                }
            }
        }

        /// <summary>
        /// Sanitize vocabulary key strings by removing non-alphanumeric characters and converting them to camel case.
        /// </summary>
        /// <param name="vocabularyKey">The vocabulary key string</param>
        /// <returns></returns>
        private static string SanitizeVocabularyKey(string vocabularyKey)
        {
            var alphaNumeric = Regex.Replace(vocabularyKey, "[^a-zA-Z0-9. ]", "");
            var segments = alphaNumeric.Split('.');

            return string.Join(".",
                segments.Select((segment, index) =>
                {
                    if (string.IsNullOrEmpty(segment))
                        return null;

                    return index == segments.Length - 1 ? // Check if this is the last segment
                        FormatLabelToProperty(segment) :
                        // Convert to lowercase
                        segment.Replace(" ", "");
                }));
        }

        /// <summary>
        /// Formats the label so it fits the style of the properties (e.g. "Company type" -> "companyType")
        /// </summary>
        /// <param name="label">The label to format</param>
        /// <returns>The formatted label</returns>
        private static string FormatLabelToProperty(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            if (IsAllUpperCase(label))
                return label.ToLower();

            if (IsAllUpperCase(label) || IsCamelCase(label)) 
                return label;

            // Convert to camelCase: first word lowercase, others capitalize first letter
            return string.Join("", label.Split(' ').Select((word, i) =>
                i == 0 ? FirstCharacterToLower(word) : FirstCharacterToUpper(word)));
        }

        private static bool IsAllUpperCase(string text)
        {
            return Regex.IsMatch(text, "^[A-Z]+$");
        }

        private static bool IsCamelCase(string text)
        {
            // Check if first character is lowercase and there’s at least one uppercase letter
            return char.IsLower(text[0]) && text.Any(char.IsUpper);
        }

        /// <summary>
        /// Capitalizes the first character in the string
        /// </summary>
        /// <param name="text">The text that should be capitalized</param>
        /// <returns>The string with the first character capitalized</returns>
        private static string FirstCharacterToUpper(string text)
        {
            return $"{char.ToUpper(text[0])}{text[1..]}";
        }

        /// <summary>
        /// Capitalizes the first character in the string
        /// </summary>
        /// <param name="text">The text that should be capitalized</param>
        /// <returns>The string with the first character capitalized</returns>
        private static string FirstCharacterToLower(string text)
        {
            return $"{char.ToLower(text[0])}{text[1..]}";
        }

        /// <summary>
        /// Retrieve the HTTP request details from the enricher configuration.
        /// </summary>
        /// <param name="context">Execution context</param>
        /// <param name="request">External Search Request</param>
        /// <param name="config">Enricher Configurations</param>
        /// <returns></returns>
        private static Dictionary<string, string> GetRequestConfig(ExecutionContext context, IExternalSearchRequest request, RestApiExternalSearchJobData config)
        {
            var configMap = config?.ToDictionary();

            var requestInfoDict = new Dictionary<string, string>();

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.Method, out var method) && !string.IsNullOrWhiteSpace(method?.ToString()))
            {
                requestInfoDict.Add("method", method.ToString());
            }

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.Url, out var url) && !string.IsNullOrWhiteSpace(url?.ToString()))
            {
                var tokenParser = context.ApplicationContext.Container.Resolve<IRuleTokenParser<IRuleActionToken>>();
                var parsedUrl = tokenParser.Parse((ProcessingContext)context, request.EntityMetaData as IEntityMetadataPart, url.ToString());
                requestInfoDict.Add("url", parsedUrl);
            }

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.ApiKey, out var apiKey) && !string.IsNullOrWhiteSpace(apiKey?.ToString()))
            {
                requestInfoDict.Add("apiKey", apiKey.ToString());
            }

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.VocabularyAndProperties, out var vocabularyAndProperties) && !string.IsNullOrWhiteSpace(vocabularyAndProperties?.ToString()))
            {
                var properties = GetProperties(request, vocabularyAndProperties.ToString());
                requestInfoDict.Add("properties", JsonConvert.SerializeObject(properties));
            }

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.ProcessRequestScript, out var processRequestScript) && !string.IsNullOrWhiteSpace(processRequestScript?.ToString()))
            {
                requestInfoDict.Add("processRequestScript", processRequestScript.ToString());
            }

            if (configMap != null && configMap.TryGetValue(Constants.KeyName.ProcessResponseScript, out var processResponseScript) && !string.IsNullOrWhiteSpace(processResponseScript?.ToString()))
            {
                requestInfoDict.Add("processResponseScript", processResponseScript.ToString());
            }

            return requestInfoDict;
        }

        /// <summary>
        /// Retrieve the HTTP request headers from the enricher configuration.
        /// </summary>
        /// <param name="input">Headers</param>
        /// <param name="apiKey">API Key</param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        private static List<HeaderDto> GetHeaders(string input, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return [];
            }

            // Replace the {APIKey} token in Headers with actual API Key
            if (input.Contains("{APIKey}"))
            {
                input = input.Replace("{APIKey}", apiKey);
            }

            var result = new List<HeaderDto>();

            foreach (var line in input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('=');
                if (parts.Length != 2)
                {
                    throw new FormatException($"Invalid line format: {line}");
                }

                result.Add(new HeaderDto
                {
                    Key = parts[0].Trim(),
                    Value = parts[1].Trim()
                });
            }

            return result;
        }

        /// <summary>
        /// Retrieve the properties and construct an HTTP request body.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="properties"></param>
        /// <returns></returns>
        private static List<PropertyDto> GetProperties(IExternalSearchRequest request, string properties)
        {
            var result = new List<PropertyDto>();
            var listOfProperties = properties.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var property in listOfProperties)
            {
                if (string.IsNullOrWhiteSpace(property)) continue;
                var value = request.QueryParameters.GetValue(property, []);

                if (value.FirstOrDefault() != null)
                {
                    result.Add(new PropertyDto
                    {
                        Key = property,
                        Value = value.FirstOrDefault()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieve the properties and construct an HTTP request body for test connection.
        /// </summary>
        /// <param name="properties"></param>
        /// <returns></returns>
        private static List<PropertyDto> GetPropertiesTestConnection(string properties)
        {
            var result = new List<PropertyDto>();
            var listOfProperties = properties.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var property in listOfProperties)
            {
                if (string.IsNullOrWhiteSpace(property)) continue;
                const string value = "DummyTestValue";

                result.Add(new PropertyDto
                {
                    Key = property,
                    Value = value
                });
            }

            return result;
        }

        /// <summary>
        /// Retrieve the HTTP request method.
        /// </summary>
        /// <param name="methodString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private static Method GetHttpMethod(string methodString)
        {
            if (string.IsNullOrEmpty(methodString))
            {
                throw new ArgumentNullException(nameof(methodString), "Method string cannot be null or empty.");
            }

            return methodString.ToLower() switch
            {
                "get" => Method.GET,
                "post" => Method.POST,
                _ => throw new ArgumentException($"Unsupported HTTP method: {methodString}. Expected 'get' or 'post'.",
                    nameof(methodString))
            };
        }

        // Since this is a configurable external search provider, these methods should never be called
        public override bool Accepts(EntityType entityType) => throw new NotSupportedException();
        public override IEnumerable<IExternalSearchQuery> BuildQueries(ExecutionContext context, IExternalSearchRequest request) => throw new NotSupportedException();
        public override IEnumerable<IExternalSearchQueryResult> ExecuteSearch(ExecutionContext context, IExternalSearchQuery query) => throw new NotSupportedException();
        public override IEnumerable<Clue> BuildClues(ExecutionContext context, IExternalSearchQuery query, IExternalSearchQueryResult result, IExternalSearchRequest request) => throw new NotSupportedException();
        public override IEntityMetadata GetPrimaryEntityMetadata(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request) => throw new NotSupportedException();
        public override IPreviewImage GetPrimaryEntityPreviewImage(ExecutionContext context, IExternalSearchQueryResult result, IExternalSearchRequest request) => throw new NotSupportedException();

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