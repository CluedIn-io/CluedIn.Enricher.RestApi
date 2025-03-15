using System.Collections.Generic;
using CluedIn.Core.Crawling;

namespace CluedIn.ExternalSearch.Providers.GenericRest
{
    public class GenericRestExternalSearchJobData : CrawlJobData
    {
        public GenericRestExternalSearchJobData(IDictionary<string, object> configuration)
        {
            AcceptedEntityType = GetValue<string>(configuration, Constants.KeyName.AcceptedEntityType);
            Header = GetValue<string>(configuration, Constants.KeyName.Headers);
            Endpoint = GetValue<string>(configuration, Constants.KeyName.Endpoint);
            VocabularyAndProperties = GetValue<string>(configuration, Constants.KeyName.VocabularyAndProperties);
        }

        public IDictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object> {
                { Constants.KeyName.AcceptedEntityType, AcceptedEntityType },
                { Constants.KeyName.Endpoint, Endpoint },
                { Constants.KeyName.Headers, Header },
                { Constants.KeyName.VocabularyAndProperties, VocabularyAndProperties },

            };
        }

        public string VocabularyAndProperties { get; set; }
        public string AcceptedEntityType { get; set; }
        public string Header { get; set; }
        public string Endpoint { get; set; }
    }
}
