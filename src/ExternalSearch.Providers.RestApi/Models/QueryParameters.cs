namespace CluedIn.ExternalSearch.Providers.RestApi.Models;

public class QueryParameters
{
    public string Url { get; set; }
    public string Method { get; set; }
    public string Body { get; set; }
    public string ApiKey { get; set; }
    public string Headers { get; set; }
    public string ProcessRequestScript { get; set; }
    public string ProcessResponseScript { get; set; }
}

