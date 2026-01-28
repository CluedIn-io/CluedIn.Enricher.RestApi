using System;
using System.Collections.Concurrent;
using ExecutionContext = CluedIn.Core.ExecutionContext;

namespace CluedIn.ExternalSearch.Providers.RestApi.Helper;

public class Cache(ExecutionContext executionContext)
{
    private static readonly ConcurrentDictionary<string, object> Locks = new();

    private object GetLock(string key)
    {
        var lockKey = $"{executionContext.Organization.Id}:{key}";
        return Locks.GetOrAdd(lockKey, _ => new object());
    }

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

        var lockObj = GetLock(key);
        lock (lockObj)
        {
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
}