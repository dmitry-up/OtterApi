using System.Text.Json;
using OtterApi.Builders;
using OtterApi.Interfaces;

namespace OtterApi.Configs;

public class OtterApiOptions
{
    public string Path { get; set; } = string.Empty;

    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Maximum number of items that can be returned in a single request via <c>?pagesize=</c>
    /// or in a <c>/pagedresult</c> response. Applies to regular list requests and pagedresult.
    /// When <c>0</c> there is no server-side limit (use with caution on large tables).
    /// When set, any client-supplied <c>pagesize</c> value greater than <c>MaxPageSize</c>
    /// is silently clamped to <c>MaxPageSize</c>.
    /// Default is <c>1000</c>.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    internal List<IOtterApiEntityBuilder> EntityBuilders { get; } = [];

    public OtterApiEntityBuilder<T> Entity<T>(string route) where T : class
    {
        var builder = new OtterApiEntityBuilder<T>(route);
        EntityBuilders.Add(builder);
        return builder;
    }
}