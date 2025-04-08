using System;
using System.Linq;
using System.Threading.Tasks;

using CluedIn.Core;
using CluedIn.Core.Providers.ExtendedConfiguration;

namespace CluedIn.ExternalSearch.Providers.GenericRest;

internal class GenericRestExtendedConfigurationProvider : IExtendedConfigurationProvider
{
    internal const string SourceName = "GenericRestExtendedConfigurationProvider";
    private const int DefaultPageSize = 20;

    private static readonly Option[] MethodsOptions = Constants.SupportedMethods
        .Select(name => new Option(name.ToLowerInvariant(), name))
        .ToArray();

    public Task<CanHandleResponse> CanHandle(ExecutionContext context, ExtendedConfigurationRequest request)
    {
        return Task.FromResult(new CanHandleResponse
        {
            CanHandle = request.Source == SourceName
        });
    }

    public Task<ResolveOptionByValueResponse> ResolveOptionByValue(ExecutionContext context, ResolveOptionByValueRequest request)
    {
        var found = request.Key switch
        {
            Constants.KeyName.Method => HandleMethods().Data.SingleOrDefault(item => item.Value.Equals(request.Value, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

        return Task.FromResult(new ResolveOptionByValueResponse
        {
            Option = found,
        });
    }

    public Task<ResolveOptionsResponse> ResolveOptions(ExecutionContext context, ResolveOptionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(request.Key switch
        {
            Constants.KeyName.Method => HandleMethods(),
            _ => ResolveOptionsResponse.Empty,
        });
    }

    private static ResolveOptionsResponse HandleMethods()
    {
        return new ResolveOptionsResponse
        {
            Data = MethodsOptions,
            Total = MethodsOptions.Length,
            Page = 0,
            Take = DefaultPageSize,
        };
    }
}


