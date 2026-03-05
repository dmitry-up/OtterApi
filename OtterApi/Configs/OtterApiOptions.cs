using System.Text.Json;
using OtterApi.Builders;
using OtterApi.Interfaces;

namespace OtterApi.Configs;

public class OtterApiOptions
{
    public string Path { get; set; } = string.Empty;

    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    internal List<IOtterApiEntityBuilder> EntityBuilders { get; } = [];

    public OtterApiEntityBuilder<T> Entity<T>(string route) where T : class
    {
        var builder = new OtterApiEntityBuilder<T>(route);
        EntityBuilders.Add(builder);
        return builder;
    }
}