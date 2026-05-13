using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CluedIn.ExternalSearch.Providers.RestApi.Models;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class AlertConfig
{
    public string Icon { get; set; }
    public string Message { get; set; }
}