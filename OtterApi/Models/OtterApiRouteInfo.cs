namespace OtterApi.Models;

public class OtterApiRouteInfo
{
    public OtterApiEntity? Entity { get; set; }
    public string? Id { get; set; }
    public string? FilterExpression { get; set; }
    public object[]? FilterValues { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int Page { get; set; }
    public string? SortExpression { get; set; }
    public bool IsCount { get; set; }
    public bool IsPageResult { get; set; }
    public List<string> IncludeExpression { get; set; } = [];

    /// <summary>
    /// Set when the request path matches a named custom route slug
    /// registered via <c>.WithCustomRoute(...)</c>.
    /// </summary>
    public OtterApiCustomRoute? CustomRoute { get; set; }
}