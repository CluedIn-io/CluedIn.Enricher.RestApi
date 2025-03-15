using System;
using System.Collections.Generic;
using System.Linq;
using CluedIn.Core.Data.Relational;
using CluedIn.Core.Providers;

namespace CluedIn.ExternalSearch.Providers.GenericRest
{
    public static class Constants
    {
        public const string ComponentName = "GenericRest";
        public const string ProviderName = "Generic REST";
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
                        "text": "Add the API key to enable the enricher to retrieve information from a specific API. For example, the Vatlayer enricher requires an access key to authenticate with the Vatlayer API."
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
            public const string Endpoint = "endpoint";
            public const string Headers = "headers";
            public const string VocabularyAndProperties = "vocabularyAndProperties";
        }

        public static string About { get; set; } = "The Generic REST Enricher retrieves data resources from a wide variety of endpoints, offering flexible and seamless access to diverse data sources.";
        public static string Icon { get; set; } = "Resources.GenericRest.svg";
        public static string Domain { get; set; } = "N/A";

        public static IEnumerable<Control> Properties { get; set; } = new List<Control>()
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
                DisplayName = "Endpoint",
                Type = "input",
                IsRequired = false,
                Name = KeyName.Endpoint,
                Help = "The endpoint that will be used for retrieving data."
            },
            new()
            {
                DisplayName = "Headers",
                Type = "input",
                IsRequired = false,
                Name = KeyName.Headers,
                Help = "The headers for the endpoint that will be used for retrieving data."
            },
            new()
            {
                DisplayName = "Vocabulary and Properties",
                Type = "input",
                IsRequired = false,
                Name = KeyName.VocabularyAndProperties,
                Help = "The vocabulary and properties will be sent to the endpoint."
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