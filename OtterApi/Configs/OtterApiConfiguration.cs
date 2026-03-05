using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtterApi.Interfaces;
using OtterApi.Middleware;
using OtterApi.Models;
using OtterApi.Processors;

namespace OtterApi.Configs;

public static class OtterApiConfiguration
{
    public static readonly List<OtterApiOperator> Operators =
    [
        new()
        {
            Name = "eq",
            SupportsString = true,
            SupportsValueType = true,
            SupportsGuid = true,
            Expression = "{propertyName} == @{index}"
        },
        new()
        {
            Name = "neq",
            SupportsString = true,
            SupportsValueType = true,
            SupportsGuid = true,
            Expression = "{propertyName} != @{index}"
        },
        new()
        {
            Name = "like",
            SupportsString = true,
            SupportsValueType = false,
            SupportsGuid = false,
            Expression = "{propertyName}.Contains(@{index})"
        },
        new()
        {
            Name = "nlike",
            SupportsString = true,
            SupportsValueType = false,
            SupportsGuid = false,
            Expression = "!{propertyName}.Contains(@{index})"
        },
        new()
        {
            Name = "lt",
            SupportsString = false,
            SupportsValueType = true,
            SupportsGuid = false,
            Expression = "{propertyName} < @{index}"
        },
        new()
        {
            Name = "lteq",
            SupportsString = false,
            SupportsValueType = true,
            SupportsGuid = false,
            Expression = "{propertyName} <= @{index}"
        },
        new()
        {
            Name = "gt",
            SupportsString = false,
            SupportsValueType = true,
            SupportsGuid = false,
            Expression = "{propertyName} > @{index}"
        },
        new()
        {
            Name = "gteq",
            SupportsString = false,
            SupportsValueType = true,
            SupportsGuid = false,
            Expression = "{propertyName} >= @{index}"
        },
        new()
        {
            Name = "in",
            SupportsString = true,
            SupportsValueType = true,
            SupportsGuid = true,
            Expression = "@{index}.Contains({propertyName})"
        },
        new()
        {
            Name = "nin",
            SupportsString = true,
            SupportsValueType = true,
            SupportsGuid = true,
            Expression = "!@{index}.Contains({propertyName})"
        }
    ];

    private static readonly List<OtterApiEntity> _otterApiEntityCache = [];

    public static IReadOnlyList<OtterApiEntity> OtterApiEntityCache => _otterApiEntityCache;

    public static OtterApiOptions? OtterApiOptions { get; private set; }

    public static void AddOtterApi<T>(this IServiceCollection serviceCollection, string path) where T : DbContext
    {
        serviceCollection.AddOtterApi<T>(options => options.Path = path);
    }

    public static void AddOtterApi<T>(this IServiceCollection serviceCollection, Action<OtterApiOptions> options) where T : DbContext
    {
        var op = new OtterApiOptions();
        options(op);
        _otterApiEntityCache.AddRange(Init<T>(op));
        serviceCollection.AddTransient<IOtterApiRequestProcessor, OtterApiRequestProcessor>();
    }

    public static List<OtterApiEntity> Init<T>(OtterApiOptions options) where T : DbContext
    {
        OtterApiOptions = options;

        return options.EntityBuilders
            .Select(b => b.Build(typeof(T), options))
            .ToList();
    }

    public static bool IsOperatorSuported(this Type type, string comparisonOperator)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) && Operators.Any(x =>
                x.SupportsString && x.Name.Equals(comparisonOperator, StringComparison.InvariantCultureIgnoreCase)))
        {
            return true;
        }

        if (type.IsValueType
            && Operators.Any(x => x.SupportsValueType && x.Name.Equals(comparisonOperator, StringComparison.InvariantCultureIgnoreCase))
            && type != typeof(Guid))
        {
            return true;
        }

        if (type == typeof(Guid) && Operators.Any(x =>
                x.SupportsGuid && x.Name.Equals(comparisonOperator, StringComparison.InvariantCultureIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static bool IsTypeSupported(this Type type)
    {
        if (type.IsValueType)
        {
            return true;
        }

        if (type == typeof(string) || type == typeof(Guid))
        {
            return true;
        }

        return false;
    }

    public static IApplicationBuilder UseOtterApi(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<OtterApiMiddleware>();
    }
}