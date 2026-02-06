using System;
using ExecutionContext = CluedIn.Core.ExecutionContext;

namespace CluedIn.ExternalSearch.Providers.RestApi.Helper;

public class Cache(ExecutionContext executionContext)
{
    public object Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentNullException(nameof(key), "Cache key cannot be empty.");

        var cached = executionContext.ApplicationContext.System.Cache.GetItem<object>($"{executionContext.Organization.Id}_{key}");
        return cached;
    }

    public void Set(string key, object value, double expiration)
    {
        if (string.IsNullOrWhiteSpace(key)) 
            throw new ArgumentNullException(nameof(key), "Cache key cannot be empty.");

        var keyWithOrgId = $"{executionContext.Organization.Id}_{key}";
        var cached = executionContext.ApplicationContext.System.Cache.GetItem<object>(keyWithOrgId);

        // The application cache in core is not allow to overwrite existing items, so we check if it exists before adding
        if (cached != null)
        {
            return;
        }

        executionContext.ApplicationContext.System.Cache.SetItem(keyWithOrgId, value, expiration > 0 ? DateTimeOffset.Now.AddMilliseconds(expiration) : null);
    }
}