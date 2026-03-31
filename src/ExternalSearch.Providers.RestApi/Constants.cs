using System;
using System.Collections.Generic;
using System.Linq;
using CluedIn.Core.Data.Relational;
using CluedIn.Core.ExternalSearch;
using CluedIn.Core.Providers;

namespace CluedIn.ExternalSearch.Providers.RestApi
{
    public static class Constants
    {
        public const string ComponentName = "RestApi";
        public const string ProviderName = "REST API";
        public static readonly Guid ProviderId = Guid.Parse("98648745-e0da-40cf-8724-8f4fb9fe338a");
        public const string Instruction = """
            [
              {
                "type": "bulleted-list",
                "children": [
                  {
                    "type": "list-item",
                    "children": [
                      {
                        "text": "Add the business domain to specify the golden records you want to enrich. Only golden records belonging to that business domain will be enriched."
                      }
                    ]
                  },
                  {
                    "type": "list-item",
                    "children": [
                      {
                        "text": "Add the vocabulary keys to provide the input for the enricher to search for additional information. For example, if you provide the website vocabulary key for the Web enricher, it will use specific websites to look for information about companies. In some cases, vocabulary keys are not required. If you don't add them, the enricher will use default vocabulary keys."
                      }
                    ]
                  },
                  {
                    "type": "list-item",
                    "children": [
                      {
                        "text": "Add the API key to enable the enricher to retrieve information from a specific API"
                      }
                    ]
                  }
                ]
              }
            ]
            """;

        public struct KeyName
        {
            public const string AcceptedEntityType = "acceptedEntityType";
            public const string IncludeConfidenceScore = "includeConfidenceScore";
            public const string Method = "method";
            public const string Url = "url";
            public const string ApiKey = "apiKey";
            public const string Headers = "headers";
            public const string VocabularyAndProperties = "vocabularyAndProperties";
            public const string ProcessRequestScript = "processRequestScript";
            public const string ProcessResponseScript = "processResponseScript";
            public const string ProcessScript = "processScript";
            public const string Version = "version";
        }

        public static string About { get; set; } = "The REST API Enricher retrieves data resources from a wide variety of endpoints, offering flexible and seamless access to diverse data sources.";
        public static string Icon { get; set; } = "Resources.RestApi.svg";
        public static string Domain { get; set; } = "N/A";

        public const string Get = "GET";
        public const string Post = "POST";
        public const string V1 = "V1";
        public const string V2 = "V2";

        private static readonly HashSet<string> SupportedMethodsHashSet = new(StringComparer.OrdinalIgnoreCase)
        {
            Get,
            Post,
        };


        private static readonly HashSet<string> SupportedVersionsHashSet = new(StringComparer.OrdinalIgnoreCase)
        {
            V1,
            V2
        };

        public static ICollection<string> SupportedMethods => SupportedMethodsHashSet;
        public static ICollection<string> SupportedVersions => SupportedVersionsHashSet;

        public static IEnumerable<Control> Properties { get; set; } = new List<Control>
        {
            new()
            {
                DisplayName = "Accepted Business Domain",
                Type = "entityTypeSelector",
                IsRequired = true,
                Name = KeyName.AcceptedEntityType,
                Help = "The business domain that defines the golden records you want to enrich (e.g., /Organization)."
            },
            new()
            {
                DisplayName = "Version",
                Type = "option",
                IsRequired = true,
                Name = KeyName.Version,
                Help = "The version of REST API enricher. When set to V1, Process Request Script and Process Response Script will be used. When set to V2, only a single Process Script will be used.",
                SourceType = ControlSourceType.Dynamic,
                Source = RestApiExtendedConfigurationProvider.SourceName,
                Options = new Dictionary<string, object>
                {
                    { "defaultValue", "v2" }
                }
            },
            new()
            {
                DisplayName = "Method",
                Type = "option",
                IsRequired = true,
                Name = KeyName.Method,
                Help = "The method of endpoint that will be used for retrieving data.",
                SourceType = ControlSourceType.Dynamic,
                Source = RestApiExtendedConfigurationProvider.SourceName,
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Url,
                        Operator = ControlDependencyOperator.Exists,
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.None,
                    },
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    }
                ],
            },
            new()
            {
                DisplayName = "URL",
                Type = "input",
                IsRequired = true,
                Name = KeyName.Url,
                Help = "The endpoint URL that will be used for retrieving data.",
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    }
                ],
            },
            new()
            {
                DisplayName = "API Key",
                Type = "password",
                IsRequired = false,
                Name = KeyName.ApiKey,
                Help = "The authorization api key for the endpoint that will be used for retrieving data.",
            },
            new()
            {
                DisplayName = "Headers",
                Type = "multiline",
                IsRequired = false,
                Name = KeyName.Headers,
                Help = "The headers for the endpoint that will be used for retrieving data.",
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    }
                ],
            },
            new()
            {
                DisplayName = "Vocabulary and Properties",
                Type = "multiline",
                IsRequired = false,
                Name = KeyName.VocabularyAndProperties,
                Help = "The vocabulary and properties will be sent to the endpoint."
            },
            new()
            {
                DisplayName = "Process Request Script",
                Type = "scriptEditor",
                IsRequired = false,
                Name = KeyName.ProcessRequestScript,
                Help = "The JavaScript script that will be used to process the request to external source.",
                Options = new Dictionary<string, object>() {{"Scripting Language", "JavaScript"}},
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    }
                ],
            },
            new()
            {
                DisplayName = "Process Response Script",
                Type = "scriptEditor",
                IsRequired = false,
                Name = KeyName.ProcessResponseScript,
                Help = "The JavaScript script that will be used to process the response from external source.",
                Options = new Dictionary<string, object>() {{"Scripting Language", "JavaScript"}},
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    }
                ],
            },
            new()
            {
                DisplayName = "Process Script",
                Type = "scriptEditor",
                IsRequired = false,
                Name = KeyName.ProcessScript,
                Help = "The JavaScript script that will be used to request and process the response from external source.",
                Options = new Dictionary<string, object>() {{"Scripting Language", "JavaScript"}},
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = KeyName.Version,
                        Operator = ControlDependencyOperator.Equals,
                        Value = "v2",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    },
                ],
            },
            new()
            {
                DisplayName = "Include Confidence Score",
                Type = "checkbox",
                IsRequired = false,
                Name = KeyName.IncludeConfidenceScore,
                Help = "When enabled, the results will include a confidence score, which can be used during data processing.",
                DisplayDependencies =
                [
                    new ControlDisplayDependency
                    {
                        Name = ExternalSearchConstants.EnricherV2SendToLandingZone,
                        Operator = ControlDependencyOperator.NotEquals,
                        Value = "true",
                        UnfulfilledAction = ControlDependencyUnfulfilledAction.Hidden,
                    },
                ]
            },
        };

        public static AuthMethods AuthMethods { get; set; } = new()
        {
            // TODO: Temporary concat properties to create duplicate controls, enricherv2 UI will read both AuthMethods and Properties, but will remove duplicate. Can remove the concat in the future.
            Token = new List<Control>().Concat(Properties) 
        };

        public static Guide Guide { get; set; } = new()
        {
            Instructions = Instruction
        };

        public static IntegrationType IntegrationType { get; set; } = IntegrationType.Enrichment;
    }
}