using System.Text.Json;
using OtterApi.Builders;
using OtterApi.Interfaces;

namespace OtterApi.Configs;

public class OtterApiOptions
{
    public string Path { get; set; } = string.Empty;

    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Hard cap on the number of items returned in a single list request.
    /// Applies to all GET list endpoints, <c>/pagedresult</c>, and custom routes.
    /// <list type="bullet">
    ///   <item><c>0</c> — no server-side limit (use with caution on large tables).</item>
    ///   <item>When the client omits <c>?pagesize</c>, <c>MaxPageSize</c> is used as the
    ///         default page size, preventing unbounded full-table scans.</item>
    ///   <item>When the client supplies <c>?pagesize=N</c> where N &gt; <c>MaxPageSize</c>,
    ///         the value is silently clamped to <c>MaxPageSize</c>.</item>
    /// </list>
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