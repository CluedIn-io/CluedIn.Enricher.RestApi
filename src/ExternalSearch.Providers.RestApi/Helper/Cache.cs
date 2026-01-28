using System;
using CluedIn.Core;

namespace CluedIn.ExternalSearch.Providers.RestApi.Helper;

public class Cache(ExecutionContext executionContext)
{
    public object Get(string key)
    {
        var cached = executionContext.ApplicationContext.System.Cache.GetItem<object>($"{executionContext.Organization.Id}_{key}");
        return cached;
    }

    public void Set(string key, object value, double expiration)
    {
        var keyWithOrgId = $"{executionContext.Organization.Id}_{key}";
        var cached = executionContext.ApplicationContext.System.Cache.GetItem<object>(keyWithOrgId);

        if (cached != null)
        {
            return;
        }

        executionContext.ApplicationContext.System.Cache.SetItem(keyWithOrgId, value, DateTimeOffset.Now.AddMilliseconds(expiration));
    }
}