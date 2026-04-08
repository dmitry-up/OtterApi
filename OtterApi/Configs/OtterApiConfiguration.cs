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
        new() { Name = "eq",    SupportsString = true,  SupportsValueType = true,  SupportsGuid = true  },
        new() { Name = "neq",   SupportsString = true,  SupportsValueType = true,  SupportsGuid = true  },
        new() { Name = "like",  SupportsString = true,  SupportsValueType = false, SupportsGuid = false },
        new() { Name = "nlike", SupportsString = true,  SupportsValueType = false, SupportsGuid = false },
        new() { Name = "lt",    SupportsString = false, SupportsValueType = true,  SupportsGuid = false },
        new() { Name = "lteq",  SupportsString = false, SupportsValueType = true,  SupportsGuid = false },
        new() { Name = "gt",    SupportsString = false, SupportsValueType = true,  SupportsGuid = false },
        new() { Name = "gteq",  SupportsString = false, SupportsValueType = true,  SupportsGuid = false },
        new() { Name = "in",    SupportsString = true,  SupportsValueType = true,  SupportsGuid = true  },
        new() { Name = "nin",   SupportsString = true,  SupportsValueType = true,  SupportsGuid = true  },
    ];

    public static void AddOtterApi<T>(this IServiceCollection serviceCollection, string path) where T : DbContext
    {
        serviceCollection.AddOtterApi<T>(options => options.Path = path);
    }

    public static void AddOtterApi<T>(this IServiceCollection serviceCollection, Action<OtterApiOptions> configure) where T : DbContext
    {
        var options  = new OtterApiOptions();
        configure(options);

        var entities = options.EntityBuilders
            .Select(b => b.Build(typeof(T), options))
            .ToList();

        var registry = new OtterApiRegistry(entities, options);

        serviceCollection.AddSingleton(registry);
        serviceCollection.AddTransient<IOtterApiRequestProcessor, OtterApiRequestProcessor>();
    }

    public static List<OtterApiEntity> Init<T>(OtterApiOptions options) where T : DbContext
    {

        return options.EntityBuilders
            .Select(b => b.Build(typeof(T), options))
            .ToList();
    }

    public static bool IsOperatorSupported(this Type type, string comparisonOperator)
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

    /// <inheritdoc cref="IsOperatorSupported"/>
    [Obsolete("Typo in the original name — use IsOperatorSupported instead. This overload will be removed in a future version.")]
    public static bool IsOperatorSuported(this Type type, string comparisonOperator)
        => IsOperatorSupported(type, comparisonOperator);

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