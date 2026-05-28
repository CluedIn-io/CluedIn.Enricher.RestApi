using CluedIn.Core.Threading;
using System;
using Microsoft.Extensions.Logging;
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

    public object GetOrSetValue(string key, object value, double expiration)
    {
        return GetOrSetInternal(key, () => value, expiration);
    }

    public object GetOrSetFactory(string key, Func<object> factory, double expiration)
    {
        return GetOrSetInternal(key, factory, expiration);
    }

    private object GetOrSetInternal(
        string key,
        Func<object> factory,
        double expiration)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var cached = Get(key);
        if (cached != null)
        {
            return cached;
        }

        var keyWithOrgId = $"{executionContext.Organization.Id}_{key}";
        try
        {
            using var scope = executionContext.ApplicationContext.System.Locks.CreateLockingScope();
            using var applicationLock = scope.GetClusterWideExclusiveLock($"RestApiEnricher_Cache_Lock_{keyWithOrgId}", TimeSpan.FromSeconds(30));

            cached = Get(key);
            if (cached != null)
            {
                return cached;
            }

            var value = factory();

            Set(key, value, expiration);

            return value;
        }
        catch (ApplicationLockProviderException ex)
        {
            executionContext.Log.LogWarning(ex, "[Enricher] Lock Exception Getting REST API enricher cache {Key}", key);
            return null;
        }
    }
}