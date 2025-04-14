using System.Collections.Generic;
using CluedIn.Core.Crawling;

namespace CluedIn.ExternalSearch.Providers.RestApi
{
    public class RestApiExternalSearchJobData : CrawlJobData
    {
        public RestApiExternalSearchJobData(IDictionary<string, object> configuration)
        {
            AcceptedEntityType = GetValue<string>(configuration, Constants.KeyName.AcceptedEntityType);
            Url = GetValue<string>(configuration, Constants.KeyName.Url);
            Method = GetValue<string>(configuration, Constants.KeyName.Method);
            VocabularyAndProperties = GetValue<string>(configuration, Constants.KeyName.VocabularyAndProperties);
            ApiKey = GetValue<string>(configuration, Constants.KeyName.ApiKey);
            Headers = GetValue<string>(configuration, Constants.KeyName.Headers);
            ProcessRequestScript = GetValue<string>(configuration, Constants.KeyName.ProcessRequestScript);
            ProcessResponseScript = GetValue<string>(configuration, Constants.KeyName.ProcessResponseScript);
        }

        public IDictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object> {
                { Constants.KeyName.AcceptedEntityType, AcceptedEntityType },
                { Constants.KeyName.Url, Url },
                { Constants.KeyName.Method, Method },
                { Constants.KeyName.Headers, Headers },
                { Constants.KeyName.VocabularyAndProperties, VocabularyAndProperties },
                { Constants.KeyName.ApiKey, ApiKey },
                { Constants.KeyName.ProcessRequestScript, ProcessRequestScript },
                { Constants.KeyName.ProcessResponseScript, ProcessResponseScript },
            };
        }

        public string AcceptedEntityType { get; set; }
        public string Url { get; set; }
        public string Method { get; set; }
        public string VocabularyAndProperties { get; set; }
        public string ApiKey { get; set; }
        public string Headers { get; set; }
        public string ProcessRequestScript { get; set; }
        public string ProcessResponseScript { get; set; }
    }
}
